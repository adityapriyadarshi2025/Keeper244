"""The timed protocol (control/protocol.py): >= 5 min per condition, prompts, profile per game block."""

import csv
import json

import numpy as np
import pytest

from emg_bridge.bridge import Bridge
from emg_bridge.config import load_config
from emg_bridge.control.protocol import ProtocolRunner, build_protocol
from emg_bridge.io.recorder import Recorder
from emg_bridge.main import main
from emg_bridge.sources.simulated import AutoPlayer, SimulatedSource
from test_bridge import FakeClock
from test_main import temp_config
from test_profiles import calibrated_bridge

CFG = load_config()


def block_seconds(steps):
    out = {}
    for s in steps:
        if s.block:
            out[s.block] = out.get(s.block, 0) + s.seconds
    return out


def test_every_condition_lasts_five_minutes():
    steps = build_protocol(CFG["protocol"])
    blocks = block_seconds(steps)
    assert set(blocks) == {"rest", "biceps", "triceps", "game_full", "game_raw", "game_bandpass"}
    assert all(v >= 300 for v in blocks.values())
    flex = [s for s in steps if s.kind == "flex" and s.muscle == "biceps"]
    assert len(flex) == 30 and {s.level for s in flex} == {"light", "strong"}
    assert [s.profile for s in steps if s.kind == "game"] == ["full", "raw", "bandpass"]
    total_min = sum(s.seconds for s in steps) / 60
    assert 30 < total_min < 45


def test_order_and_block_selection():
    steps = build_protocol(CFG["protocol"], game_profiles=["raw", "full"], blocks=["game"])
    assert [s.block for s in steps if s.block] == ["game_raw", "game_full"]
    assert steps[-1].kind == "game"  # no trailing break


def test_runner_prompts_count_down_the_block():
    run = ProtocolRunner(build_protocol(CFG["protocol"], blocks=["biceps"]))
    run.tick(0.0)                      # ready
    assert run.step.kind == "ready"
    run.tick(5.0)                      # first flex
    assert run.step.kind == "flex"
    assert "biceps block: 5:00 left" in run.prompt(5.0)
    assert "biceps block: 4:58 left" in run.prompt(7.0)
    run.skip(7.0)                      # flex steps are not skippable
    assert run.step.kind == "flex"


def test_protocol_in_the_bridge_switches_profiles_and_logs_blocks(tmp_path):
    b, src, clock, rec = calibrated_bridge(tmp_path)
    pcfg = dict(CFG["protocol"], rest_s=4, contraction_block_s=4, game_block_s=2, break_s=1, ready_s=1)
    b.protocol = ProtocolRunner(build_protocol(pcfg, ["raw", "full"]))
    b.enter_play()
    levels = []
    while not b.protocol.done:
        clock.t += 0.005
        frames = b.step()
        s = b.protocol.step
        if frames and s is not None and s.kind == "flex" and clock.t - (b.t0 + b.protocol.step_started) > 0.6:
            levels.append((s.muscle, frames[-1].slot))
    b.close()
    assert ("biceps", 1) in levels or ("biceps", 2) in levels
    assert ("triceps", -1) in levels or ("triceps", -2) in levels
    ev = list(csv.DictReader(open(rec.dir / "events.csv")))
    blocks = [json.loads(e["json"]).get("block") for e in ev if e["type"] == "protocol"]
    assert {"rest", "biceps", "triceps", "game_raw", "game_full"} <= set(blocks)
    prof = [e["name"] for e in ev if e["type"] == "profile"]
    assert prof[-2:] == ["raw", "full"]


def test_protocol_command_wiring(tmp_path):
    cfg, root = temp_config(tmp_path)
    from test_main import sim_calibration
    from emg_bridge.control.calibration import CalibrationSet
    cal = CalibrationSet({"full": sim_calibration()}).save(tmp_path / "cal.json")
    rc = main(["protocol", "--config", str(cfg), "--no-monitor", "--calibration", str(cal), "--blocks", "rest",
               "--short", "--duration", "2", "--player", "auto"])
    assert rc == 0
    (session,) = root.iterdir()
    ev = list(csv.DictReader(open(session / "events.csv")))
    assert [e["name"] for e in ev if e["type"] == "protocol"][:1] == ["ready"]  # 2 s: still in the first step
    assert main(["protocol", "--config", str(cfg), "--no-monitor", "--calibration", str(cal),
                 "--game-profiles", "magic"]) == 2
