"""The live monitor reads the bridge from another thread: those reads must never fail or slow down."""

import threading
import time

import numpy as np
import pytest

from emg_bridge.bridge import Bridge
from emg_bridge.config import load_config
from emg_bridge.io.recorder import Recorder, RunningFit, measured_fs
from emg_bridge.sources.simulated import SimulatedSource
from test_bridge import sim_calibration

CFG = load_config()


def test_running_fit_matches_polyfit():
    rng = np.random.default_rng(0)
    t = np.cumsum(rng.uniform(0.015, 0.025, 3000)) + 1234.5      # bursty arrivals
    y = np.round((t - t[0]) * 1998.7 + rng.normal(0, 15, len(t))) + 10**6
    fit = RunningFit()
    for a, b in zip(t, y):
        fit.add(a, b)
    assert fit.fs == pytest.approx(measured_fs(t, y), rel=1e-9)
    assert fit.fs == pytest.approx(1998.7, rel=1e-3)


def test_running_fit_needs_two_seconds():
    fit = RunningFit()
    for i in range(50):
        fit.add(i * 0.01, i * 20.0)
    assert fit.fs is None


def test_snapshot_from_another_thread_while_the_bridge_runs(tmp_path):
    src = SimulatedSource(2000.0, CFG["simulator"], seed=5)
    rec = Recorder(tmp_path, "threads", {"source": {"fs": 2000.0}})
    b = Bridge(CFG, src, mode="play", calibration=sim_calibration(), recorder=rec, log=lambda *_: None)
    errors, reads = [], [0]

    def monitor():
        while not b.stop_requested:
            try:
                if b.started:
                    b.snapshot()
                    b.status_line()
                    reads[0] += 1
            except Exception as e:  # noqa: BLE001 - the test is that nothing escapes
                errors.append(repr(e))
            time.sleep(0.001)

    th = threading.Thread(target=monitor)
    th.start()
    b.run(duration=3.0, poll_s=0.0005)
    th.join(timeout=2)
    b.close()
    assert not errors, errors[:3]
    assert reads[0] > 500
    assert rec.fs_measured == pytest.approx(2000, rel=0.01)
