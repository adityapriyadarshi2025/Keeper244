"""Sources: the simulator's signal model, replay, and the BioRadio code path on the fake SDK."""

import numpy as np
import pytest

from emg_bridge.config import load_config
from emg_bridge.dsp.envelope import MovingRMS
from emg_bridge.dsp.features import psd_welch
from emg_bridge.dsp.filters import StreamingSOS, design_chain
from emg_bridge.sources.bioradio import BioRadioError, BioRadioSource, normalise_mac, unit_scale
from emg_bridge.sources.fake_bioradio import FakeBioRadioSdk
from emg_bridge.sources.replay import ReplaySource
from emg_bridge.sources.simulated import AutoPlayer, KeysPlayer, SimulatedSource

CFG = load_config()
FS = 2000.0


class FakeClock:
    def __init__(self):
        self.t = 0.0

    def __call__(self):
        return self.t


def envelope_uv(x):
    y = StreamingSOS(design_chain(FS, CFG["filters"]), 2).process(x)
    _, e = MovingRMS(200, 20, 2).process(y)
    return e[20:] / 1e-6  # skip the warm-up


def test_simulated_levels_match_the_model():
    sim = SimulatedSource(FS, CFG["simulator"], realtime=False)
    rest = sim.generate(4000)
    sim.set_drive(0.5, 0.0)
    sim.generate(1000)  # ramp
    flex = sim.generate(4000)
    e_rest, e_flex = envelope_uv(rest).mean(0), envelope_uv(flex).mean(0)
    assert 2 < e_rest[0] < 8                      # electrode noise after the band-pass
    assert e_flex[0] == pytest.approx(200, rel=0.1)  # 0.5 x 400 µV
    assert e_flex[1] == pytest.approx(0.08 * 200, rel=0.35)  # crosstalk, plus noise


def test_simulated_has_a_mains_peak_that_the_notch_removes():
    sim = SimulatedSource(FS, CFG["simulator"], realtime=False)
    x = sim.generate(20000)
    f, p = psd_welch(x[:, 0], FS)
    assert p[np.argmin(abs(f - 50))] > 20 * np.median(p[(f > 60) & (f < 200)])
    y = StreamingSOS(design_chain(FS, CFG["filters"]), 2).process(x)
    f, py = psd_welch(y[4000:, 0], FS)
    assert py[np.argmin(abs(f - 50))] < 3 * np.median(py[(f > 60) & (f < 200)])


def test_realtime_pacing_bursts_and_dropout():
    clock = FakeClock()
    sim = SimulatedSource(FS, {**CFG["simulator"], "chunk_ms": 20}, clock=clock)
    sim.start()
    clock.t = 0.015
    assert len(sim.read()) == 0          # less than one 20 ms burst due
    clock.t = 0.050
    c = sim.read()
    assert len(c) == 80 and c.n0 == 0    # two bursts of 40 samples
    sim.dropout(0.5)
    clock.t = 0.30
    assert len(sim.read()) == 0          # link down: samples are lost
    clock.t = 0.60
    c = sim.read()
    assert c.n0 > 80                     # the jump the bridge detects as a gap
    assert c.n_end == int(0.60 * FS) // 40 * 40


def test_saturation_clips_at_the_range():
    sim = SimulatedSource(FS, {**CFG["simulator"], "range_uv": 300}, realtime=False)
    sim.set_drive(3.0, 0.0)
    sim.generate(500)
    x = sim.generate(2000)
    assert np.isclose(np.abs(x[:, 0]).max(), 300e-6)


def test_keys_player_and_auto_player():
    clock = FakeClock()
    sim = SimulatedSource(FS, CFG["simulator"], clock=clock)
    keys = KeysPlayer(sim, CFG["simulator"], clock=clock)
    assert keys.on_key("2") and tuple(sim.drive_target) == (0.55, 0.0)
    assert keys.on_key("3") and tuple(sim.drive_target) == (0.0, 0.22)
    assert keys.on_key(" ") and tuple(sim.drive_target) == (0.0, 0.0)
    assert not keys.on_key("z")
    auto = AutoPlayer(sim, CFG["simulator"], clock=clock)
    auto.on_event({"name": "shot_cue", "zones": 5, "target": -2, "shot": 1, "level": "L2"}, 0.0)
    auto.update()
    assert tuple(sim.drive_target) == (0.0, 0.0)  # not yet: reaction time
    clock.t = 0.4
    auto.update()
    assert tuple(sim.drive_target) == (0.0, 0.55)
    auto.on_event({"name": "shot_void", "shot": 1, "reason": "no_signal"}, 0.4)
    assert tuple(sim.drive_target) == (0.0, 0.0)


def test_replay_streams_the_recording(tmp_path):
    n = np.arange(1000)
    x = np.column_stack([np.sin(n / 10.0), np.cos(n / 10.0)]) * 100
    n = np.r_[n[:500], n[500:] + 40]  # a gap of 40 samples in the recording
    rows = np.column_stack([n, n / FS, x])
    np.savetxt(tmp_path / "raw.csv", rows, delimiter=",", header="n,t,biceps_uv,triceps_uv", comments="", fmt="%.6f")
    (tmp_path / "meta.json").write_text('{"source": {"fs": 2000, "channels": ["biceps", "triceps"]}}')
    src = ReplaySource(tmp_path, speed=0, chunk_s=0.1)
    src.start()
    chunks = []
    while not src.finished:
        chunks.append(src.read())
    assert sum(len(c) for c in chunks) == 1000
    assert any(c.n0 == 540 for c in chunks)               # the gap survives replay
    got = np.vstack([c.x for c in chunks])
    np.testing.assert_allclose(got / 1e-6, x, atol=1e-6)


# --- BioRadio on the fake SDK -------------------------------------------------------------

def bioradio(sdk, **cfg):
    clock = sdk.clock
    return BioRadioSource({**CFG["bioradio"], **cfg}, sdk=sdk, clock=clock, log=lambda *_: None)


def test_bioradio_connects_maps_channels_and_reads_volts():
    clock = FakeClock()
    sdk = FakeBioRadioSdk(clock=clock, units="mV", auto_pattern=False)
    src = bioradio(sdk)
    info = src.connect()
    assert info.fs == 2000 and info.details["picked"] == {"biceps": "Biceps", "triceps": "Triceps"}
    assert info.details["units_reported"] == "mV" and info.range_v == pytest.approx((-0.0075, 0.0075))
    assert info.details["battery"]["charge_state"] == 87.0
    src.start()
    sdk.device.sim.set_drive(0.5, 0.0)
    total = []
    for k in range(1, 151):
        clock.t = k * 0.01
        c = src.read()
        assert c.n0 == sum(len(t) for t in total)
        total.append(c.x)
    x = np.vstack(total)
    assert len(x) == 3000 and x.shape[1] == 2
    assert 50e-6 < np.sqrt(np.mean(x[1000:, 0] ** 2)) < 400e-6   # volts, not mV
    src.stop()
    assert not sdk.device.connected


def test_bioradio_channels_by_name_and_errors():
    sdk = FakeBioRadioSdk(clock=FakeClock())
    src = bioradio(sdk, channels={"biceps": "triceps", "triceps": "biceps"})
    assert src.connect().details["picked"] == {"biceps": "Triceps", "triceps": "Biceps"}
    with pytest.raises(BioRadioError, match="same channel"):
        bioradio(FakeBioRadioSdk(clock=FakeClock()), channels={"biceps": 1, "triceps": "Biceps"}).connect()
    with pytest.raises(BioRadioError, match="we need 2"):
        bioradio(FakeBioRadioSdk(clock=FakeClock(), channel_names=("Ch1",))).connect()
    with pytest.raises(BioRadioError, match="no BioRadio found"):
        bioradio(FakeBioRadioSdk(clock=FakeClock(), present=False)).connect()


def test_bioradio_pads_become_missing_samples():
    clock = FakeClock()
    sdk = FakeBioRadioSdk(clock=clock, pad_every=5)
    src = bioradio(sdk)
    src.start()
    xs = []
    for k in range(1, 101):
        clock.t = k * 0.01
        xs.append(src.read().x)
    x = np.vstack(xs)
    frac = np.isnan(x[:, 0]).mean()
    assert 0.15 < frac < 0.25 and src.pads > 0 and src.dropped > 0


def test_bioradio_button_becomes_a_mark():
    clock = FakeClock()
    sdk = FakeBioRadioSdk(clock=clock)
    src = bioradio(sdk)
    src.start()
    clock.t = 1.5
    sdk.device.press_button()
    assert src.poll_marks() == [(1.5, "bioradio_button")]
    assert src.poll_marks() == []


def test_bioradio_needs_windows_and_the_dll(tmp_path, monkeypatch):
    monkeypatch.setattr("sys.platform", "darwin")
    with pytest.raises(BioRadioError, match="Windows"):
        BioRadioSource({"sdk_dll": str(tmp_path / "BioRadioSDK.dll")}, log=lambda *_: None).connect()
    monkeypatch.setattr("sys.platform", "win32")
    with pytest.raises(BioRadioError, match="not found"):
        BioRadioSource({"sdk_dll": str(tmp_path / "BioRadioSDK.dll")}, log=lambda *_: None).connect()


def test_helpers():
    assert normalise_mac("ec:fe:7e-18 36af") == "ECFE7E1836AF"
    assert unit_scale("µV") == (1e-6, True) and unit_scale("mV") == (1e-3, True)
    assert unit_scale("furlongs") == (1.0, False)
