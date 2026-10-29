"""Filter profiles: settings, one calibration for every profile, live switching, offline == live."""

import csv

import numpy as np
import pytest

from emg_bridge.bridge import Bridge
from emg_bridge.config import load_config
from emg_bridge.control.calibration import Calibration, CalibrationSet, Procedure, build_steps
from emg_bridge.dsp.filters import PASSTHROUGH, design_chain, frequency_response
from emg_bridge.dsp.pipeline import describe, profile_names, profile_settings
from emg_bridge.io.recorder import Recorder
from emg_bridge.offline import assemble, process
from emg_bridge.sources.simulated import AutoPlayer, SimulatedSource
from test_bridge import FakeClock, advance, sim_calibration

CFG = load_config()
FS = 2000.0


def test_profiles_in_config():
    assert profile_names(CFG) == ["full", "bandpass", "raw", "smooth", "fast"]
    assert profile_settings(CFG, "smooth")["envelope"]["rms_window_ms"] == 250
    assert profile_settings(CFG, "smooth")["filters"] == CFG["filters"]
    assert "notch" in describe(CFG, "full") and "notch" not in describe(CFG, "bandpass")
    assert describe(CFG, "raw").startswith("raw: high-pass 1 Hz;")
    with pytest.raises(KeyError):
        profile_settings(CFG, "magic")


def test_raw_profile_keeps_mains_and_full_removes_it():
    raw = design_chain(FS, profile_settings(CFG, "raw")["filters"])
    full = design_chain(FS, profile_settings(CFG, "full")["filters"])
    (m_raw,), _ = frequency_response(raw, FS, np.array([50.0]))
    (m_full,), _ = frequency_response(full, FS, np.array([50.0]))
    assert m_raw > -0.1 and m_full < -30
    (dc,), _ = frequency_response(raw, FS, np.array([0.01]))
    assert dc < -30  # DC removed
    np.testing.assert_array_equal(design_chain(FS, {"bandpass": {"enabled": False}}), PASSTHROUGH)


def calibrated_bridge(tmp_path):
    clock = FakeClock()
    src = SimulatedSource(FS, CFG["simulator"], clock=clock, seed=9)
    player = AutoPlayer(src, CFG["simulator"], clock=clock)
    proc = Procedure(build_steps(CFG["calibration"], quick=True), CFG["calibration"], 100.0,
                     on_step=lambda s, i: player.on_calibration_step(s))
    rec = Recorder(tmp_path, "prof", {"source": {"fs": FS}})
    b = Bridge(CFG, src, mode="calibrate", procedure=proc, player=player, recorder=rec, clock=clock, log=lambda *_: None)
    b.start()
    while not b.calibration_done:
        clock.t += 0.005
        b.step()
    return b, src, clock, rec


def test_one_calibration_gives_every_profile(tmp_path):
    b, src, clock, rec = calibrated_bridge(tmp_path)
    cals = b.calibrations
    assert cals.names == profile_names(CFG) and cals.active == "full"
    full, raw, smooth = cals.get("full"), cals.get("raw"), cals.get("smooth")
    assert raw.biceps.mu_rest > 2 * full.biceps.mu_rest      # mains + broadband noise stay in the raw envelope
    assert all(c.checks_passed for c in cals.profiles.values())
    assert smooth.biceps.e_mvc == pytest.approx(full.biceps.e_mvc, rel=0.1)
    back = CalibrationSet.load(rec.dir / "calibration.json")
    assert back.names == cals.names and back.get("raw").biceps.e_mvc == raw.biceps.e_mvc


def test_switching_profiles_live(tmp_path):
    b, src, clock, rec = calibrated_bridge(tmp_path)
    b.enter_play()
    src.set_drive(0.55, 0.0)
    assert advance(b, clock, 0.6)[-1].slot == 2
    b.on_key("f")                                   # full -> bandpass
    frames = advance(b, clock, 0.6)
    assert b.profile == "bandpass" and frames[-1].slot == 2
    assert b.set_profile("raw") and advance(b, clock, 0.6)[-1].slot == 2
    src.set_drive(0.0, 0.0)
    assert advance(b, clock, 0.6)[-1].slot == 0
    assert not b.set_profile("nonsense")
    b.close()
    rows = list(csv.DictReader(open(rec.dir / "envelope.csv")))
    assert {r["profile"] for r in rows} == {"full", "bandpass", "raw"}
    ev = [(e["type"], e["name"]) for e in csv.DictReader(open(rec.dir / "events.csv"))]
    assert ("profile", "bandpass") in ev and ("profile", "raw") in ev


def test_a_single_old_style_calibration_cannot_switch():
    clock = FakeClock()
    src = SimulatedSource(FS, CFG["simulator"], clock=clock)
    b = Bridge(CFG, src, mode="play", calibration=sim_calibration(), clock=clock, log=lambda *_: None)
    b.start()
    assert b.available_profiles == ["full"] and not b.set_profile("raw")


def test_offline_process_matches_the_live_bridge(tmp_path):
    b, src, clock, rec = calibrated_bridge(tmp_path)
    b.enter_play()
    chunks = []
    orig = src.read

    def spy():
        c = orig()
        chunks.append((c.n0, c.x.copy()))
        return c
    src.read = spy
    b._reset_dsp(b.expected_n)          # start both from a clean filter state at the same sample
    live = []
    for drive in [(0.22, 0), (0.55, 0), (0, 0), (0, 0.6), (0.3, 0.3), (0, 0)]:
        src.set_drive(*drive)
        live += advance(b, clock, 0.5)
    n0, x = assemble(chunks)
    off = process(x, FS, CFG, "full", b.calibrations, n0=n0)
    assert len(off["k"]) == len(live)
    np.testing.assert_array_equal(off["k"], [f.k for f in live])
    np.testing.assert_allclose(off["e"], np.array([f.e for f in live]), rtol=1e-9)
    assert off["slot"].tolist() == [f.slot for f in live]
