"""Envelope stage: frame grid, moving RMS value, streaming == single-shot, flag counting."""

import numpy as np
import pytest

from emg_bridge.dsp.envelope import LowpassEnvelope, MovingRMS, WindowCount, make_envelope

FS, W, HOP = 2000.0, 200, 20


def run_chunked(env, x, rng):
    ks, vals, i = [], [], 0
    while i < len(x):
        n = int(rng.integers(1, 61))
        k, v = env.process(x[i:i + n])
        ks.append(k)
        vals.append(v)
        i += n
    return np.concatenate(ks), np.vstack(vals)


def test_frame_grid():
    env = MovingRMS(W, HOP, 1)
    ks, _ = env.process(np.ones((1000, 1)))
    assert ks[0] == W - 1                       # first full window
    assert np.all((ks + 1) % HOP == 0)          # one frame per hop
    assert len(ks) == (1000 - W) // HOP + 1


def test_rms_of_a_sine_is_amplitude_over_root_two():
    t = np.arange(4000) / FS
    x = (0.5 * np.sin(2 * np.pi * 100 * t))[:, None]
    _, v = MovingRMS(W, HOP, 1).process(x)
    np.testing.assert_allclose(v[:, 0], 0.5 / np.sqrt(2), rtol=1e-6)


@pytest.mark.parametrize("cls", ["rms", "lowpass"])
def test_streaming_equals_single_shot(cls):
    rng = np.random.default_rng(4)
    x = rng.normal(size=(6000, 2))
    make = (lambda: MovingRMS(W, HOP, 2)) if cls == "rms" else (lambda: LowpassEnvelope(FS, 5.0, W, HOP, 2))
    k1, v1 = make().process(x)
    k2, v2 = run_chunked(make(), x, rng)
    np.testing.assert_array_equal(k1, k2)
    np.testing.assert_allclose(v1, v2, atol=1e-12)


def test_window_count_flags():
    flags = np.zeros((400, 1), dtype=bool)
    flags[250] = True
    ks, counts = WindowCount(W, HOP, 1).process(flags)
    for k, c in zip(ks, counts[:, 0]):
        assert c == (1 if k - W + 1 <= 250 <= k else 0)


def test_make_envelope_from_config():
    env = make_envelope({"method": "rms", "rms_window_ms": 100, "hop_ms": 10, "lowpass_fc_hz": 5}, FS, 2)
    assert (env.window, env.hop) == (200, 20)
    with pytest.raises(ValueError):
        make_envelope({"method": "magic", "rms_window_ms": 100, "hop_ms": 10}, FS, 2)
