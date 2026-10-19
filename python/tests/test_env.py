"""Phase 0 smoke tests: the environment works and config.yaml is sane.

Real pipeline tests (brief §5.6) arrive in Phase 4.
"""

import importlib

import numpy as np
import pytest
from scipy import signal

from emg_bridge.config import load_config


@pytest.mark.parametrize("module", ["numpy", "scipy", "yaml", "pandas", "matplotlib"])
def test_core_packages_import(module):
    importlib.import_module(module)


def test_config_has_required_sections():
    cfg = load_config()
    for section in ["acquisition", "filters", "envelope", "calibration", "decision", "network"]:
        assert section in cfg, f"config.yaml missing section: {section}"


def test_bandpass_designs_and_is_stable():
    cfg = load_config()
    fs = cfg["acquisition"]["fs_nominal_hz"]
    bp = cfg["filters"]["bandpass"]
    assert bp["high_hz"] < fs / 2, "band-pass top edge must be below Nyquist"
    sos = signal.butter(bp["order"], [bp["low_hz"], bp["high_hz"]], btype="bandpass", fs=fs, output="sos")
    _, poles, _ = signal.sos2zpk(sos)
    assert np.all(np.abs(poles) < 1.0)


def test_notch_removes_at_least_30_db_at_50_hz():
    cfg = load_config()
    fs = cfg["acquisition"]["fs_nominal_hz"]
    notch = cfg["filters"]["notch"]
    b, a = signal.iirnotch(notch["freqs_hz"][0], notch["q"], fs=fs)
    _, h = signal.freqz(b, a, worN=[50.0], fs=fs)
    assert 20 * np.log10(abs(h[0]) + 1e-15) <= -30.0


def test_ports_are_distinct():
    net = load_config()["network"]
    assert net["out_port"] != net["in_port"]
