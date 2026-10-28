"""Brief §5.6 filter tests: 50 Hz notch depth, flat passband, chunked == single-shot."""

import numpy as np
import pytest
from scipy import signal

from emg_bridge.config import load_config
from emg_bridge.dsp.filters import MissingHold, StreamingSOS, design_chain, frequency_response

CFG = load_config()
FS = float(CFG["acquisition"]["fs_nominal_hz"])


def chain():
    return design_chain(FS, CFG["filters"])


def test_chain_removes_at_least_30_db_at_50_hz():
    mag_db, _ = frequency_response(chain(), FS, np.array([50.0]))
    assert mag_db[0] <= -30.0


def test_passband_is_flat_away_from_the_notch():
    f = np.linspace(80, 300, 50)
    mag_db, _ = frequency_response(chain(), FS, f)
    assert np.max(np.abs(mag_db)) < 0.1


def test_stopbands_attenuate():
    mag_db, _ = frequency_response(chain(), FS, np.array([2.0, 5.0, 900.0]))
    assert np.all(mag_db < -20.0)


def test_group_delay_is_small_in_the_emg_band():
    f = np.linspace(100, 250, 30)
    _, gd = frequency_response(chain(), FS, f)
    assert np.all(np.abs(gd) < 0.003)  # brief §4.6: ~1 ms in the main band


def test_top_edge_is_lowered_for_a_slow_device():
    sos = design_chain(500.0, CFG["filters"])  # 450 Hz would be above Nyquist (250 Hz)
    assert np.all(np.abs(signal.sos2zpk(sos)[1]) < 1)


@pytest.mark.parametrize("seed", [0, 1, 2])
def test_chunked_equals_single_shot(seed):
    rng = np.random.default_rng(seed)
    x = rng.normal(size=(5000, 2)) + np.array([0.3, -0.2])  # with a DC offset
    sos = chain()
    whole = StreamingSOS(sos, 2).process(x)
    f = StreamingSOS(sos, 2)
    parts, i = [], 0
    while i < len(x):
        n = int(rng.integers(1, 97))
        parts.append(f.process(x[i:i + n]))
        i += n
    np.testing.assert_allclose(np.vstack(parts), whole, rtol=0, atol=1e-12)


def test_dc_offset_does_not_ring_at_start():
    x = np.full((4000, 1), 1e-3)  # 1 mV DC, e.g. electrode offset
    y = StreamingSOS(chain(), 1).process(x)
    assert np.max(np.abs(y)) < 1e-9


def test_missing_samples_never_reach_the_filter():
    x = np.ones((10, 2))
    x[3:6, 0] = np.nan
    x[0, 1] = np.nan
    h = MissingHold(2)
    filled, missing = h.process(x)
    assert np.isfinite(filled).all()
    assert missing[:, 0].tolist() == [False, False, False, True, True, True, False, False, False, False]
    assert filled[3, 0] == 1.0 and filled[0, 1] == 0.0  # held value, or 0 before any good sample
    nxt, _ = h.process(np.full((2, 2), np.nan))
    np.testing.assert_array_equal(nxt, np.ones((2, 2)))  # carries the last value across chunks
