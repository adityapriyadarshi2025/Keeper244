"""Lessons from the first BioRadio day (2026-11-04): a lead that is off must stop the calibration
from being used, light/strong holds that came out too close must be easy to redo (h), the whole
calibration must be redoable without quitting (r), and a loose electrode during play must be
flagged live."""

from __future__ import annotations

import csv

import numpy as np

from emg_bridge.bridge import Bridge
from emg_bridge.config import load_config
from emg_bridge.control.calibration import Procedure, build_steps
from emg_bridge.io.recorder import Recorder
from emg_bridge.offline import electrode_problems
from emg_bridge.sources.simulated import AutoPlayer, SimulatedSource

CFG = load_config()
FS = 2000.0


class FakeClock:
    def __init__(self):
        self.t = 100.0

    def __call__(self):
        return self.t


class LeadOff(SimulatedSource):
    """The simulator with switchable lead faults: 'flat' = constant value, 'rail' = at the limit."""

    fault: dict[int, str] = {}

    def read(self):
        ch = super().read()
        for c, kind in self.fault.items():
            if len(ch):
                ch.x[:, c] = 0.0021 if kind == "flat" else 1.01 * CFG["simulator"]["range_uv"] * 1e-6
        return ch


def calibrating_bridge(tmp_path, fault=None, light=None):
    clock = FakeClock()
    src = LeadOff(FS, CFG["simulator"], clock=clock, seed=4)
    src.fault = dict(fault or {})
    player = AutoPlayer(src, CFG["simulator"], clock=clock)
    if light is not None:
        player.light = light
    proc = Procedure(build_steps(CFG["calibration"], quick=True), CFG["calibration"], 100.0,
                     on_step=lambda s, i: player.on_calibration_step(s))
    rec = Recorder(tmp_path, "cal", {"source": {"fs": FS}})
    b = Bridge(CFG, src, mode="calibrate", procedure=proc, player=player, recorder=rec, clock=clock,
               log=lambda *_: None)
    b.start()
    return b, src, clock, player


def run_until(b, clock, cond, seconds=90):
    for _ in range(int(seconds / 0.005)):
        clock.t += 0.005
        b.step()
        if cond():
            return True
    return False


def test_electrode_problems_finds_a_flat_and_a_railed_channel():
    fs = 1000.0
    steps = build_steps(CFG["calibration"], quick=True)
    n = 40 * int(fs)
    x = np.random.default_rng(0).normal(0, 50e-6, size=(n, 2))
    x[:, 1] = 0.002                      # triceps lead off: constant
    starts = list(np.linspace(0, n - 1000, len(steps)).astype(int))
    msgs = electrode_problems(x, 0, fs, steps, starts, n, ["biceps", "triceps"], sat_level=0.18)
    assert len(msgs) == 1 and msgs[0].startswith("triceps: flat") and "rest" in msgs[0]
    x[:, 1] = np.random.default_rng(1).normal(0, 50e-6, size=n)
    x[: n // 2, 0] = 0.19                # biceps at the limit for the first half
    msgs = electrode_problems(x, 0, fs, steps, starts, n, ["biceps", "triceps"], sat_level=0.18)
    assert len(msgs) == 1 and "biceps" in msgs[0] and "limit" in msgs[0]


def test_a_lead_off_holds_the_calibration_until_r_redoes_it(tmp_path):
    b, src, clock, player = calibrating_bridge(tmp_path, fault={1: "flat"})
    assert run_until(b, clock, lambda: b.procedure.done)
    assert b.awaiting_choice and not b.calibration_done
    assert any("triceps" in p for p in b.cal_problems)
    assert "CALIBRATION PROBLEM" in b.snapshot()["prompt"]
    # fix the electrode, press r: the whole sequence again, without the settle wait
    src.fault = {}
    b.on_key("r")
    assert run_until(b, clock, lambda: b.procedure.index >= 0, 1)
    assert not any(s.kind == "settle" for s in b.procedure.steps)
    assert run_until(b, clock, lambda: b.calibration_done)
    assert b.calibration is not None and b.calibration.checks_passed and not b.cal_problems
    b.enter_play()
    assert b.mode == "play"
    b.close()
    names = [(e["type"], e["name"]) for e in csv.DictReader(open(b.recorder.dir / "events.csv"))]
    assert ("calibration", "problems") in names or ("calibration", "failed") in names
    assert ("calibration", "redo") in names and ("calibration", "done") in names


def test_close_light_and_strong_wait_then_h_redoes_only_the_holds(tmp_path):
    b, src, clock, player = calibrating_bridge(tmp_path, light=0.5)   # light ~ strong (0.55): too close
    assert run_until(b, clock, lambda: b.procedure.done)
    assert b.awaiting_choice and b.calibration is not None and not b.calibration.checks_passed
    mvc_before = b.calibration.biceps.e_mvc
    player.light = 0.22
    b.on_key("h")
    assert run_until(b, clock, lambda: b.procedure.index >= 0 and b.procedure.step is not None
                     and b.procedure.step.kind in ("ready", "light"), 2)
    assert {"rest", "mvc_biceps", "mvc_triceps"} <= set(b.procedure.segments)
    assert not any(k.startswith(("light_", "strong_")) for k in b.procedure.segments)
    assert run_until(b, clock, lambda: b.calibration_done)
    c = b.calibration
    assert c.checks_passed and abs(c.biceps.e_mvc - mvc_before) / mvc_before < 0.05   # MVC kept
    b.close()


def test_g_accepts_a_calibration_with_problems(tmp_path):
    b, src, clock, player = calibrating_bridge(tmp_path, light=0.5)
    assert run_until(b, clock, lambda: b.procedure.done)
    assert not b.calibration_done
    b.on_key("g")
    b.step()
    assert b.calibration_done
    b.close()


def test_live_warning_when_an_electrode_comes_off_during_play(tmp_path):
    b, src, clock, player = calibrating_bridge(tmp_path)
    assert run_until(b, clock, lambda: b.calibration_done)
    b.enter_play()
    run_until(b, clock, lambda: False, 1.0)
    assert not b.warnings
    src.fault = {0: "rail"}
    run_until(b, clock, lambda: False, 1.5)
    assert any("BICEPS" in w and "limit" in w for w in b.warnings.values())
    assert b.snapshot()["prompt"].startswith("⚠")
    src.fault = {}
    run_until(b, clock, lambda: False, 1.5)
    assert not b.warnings
    b.close()
    ev = [(e["type"], e["name"]) for e in csv.DictReader(open(b.recorder.dir / "events.csv"))]
    assert ("electrode", "warning") in ev and ("electrode", "ok") in ev
