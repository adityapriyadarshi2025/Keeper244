"""Streaming envelope (brief §4.2 stage 4): moving RMS or rectify + low-pass, one frame per hop.

Both methods emit frames on the same grid: a frame ends at absolute sample k whenever
(k + 1) is a multiple of `hop` and at least `window` samples have been seen. With fs = 2000 Hz,
a 100 ms window and a 10 ms hop that is 200-sample windows every 20 samples, i.e. 100 frames/s.

`WindowCount` counts flagged samples (missing, saturated) over the same windows, so every
envelope frame knows whether it is trustworthy.
"""

from __future__ import annotations

import numpy as np
from scipy import signal

from .filters import StreamingSOS


class _WindowSum:
    """Sum of the last `window` samples, evaluated at the frame grid. Exact (cumsum per chunk)."""

    def __init__(self, window: int, hop: int, n_channels: int):
        if window < 1 or hop < 1:
            raise ValueError("window and hop must be >= 1 sample")
        self.window, self.hop, self.n_channels = int(window), int(hop), int(n_channels)
        self.tail = np.zeros((0, self.n_channels))
        self.n = 0  # samples consumed so far

    def reset(self) -> None:
        self.tail = np.zeros((0, self.n_channels))
        self.n = 0

    def frame_ends(self, n_new: int) -> np.ndarray:
        """Absolute indices k of the frames that end inside the next n_new samples."""
        lo, hi = self.n, self.n + n_new - 1
        first = max(lo, self.window - 1)
        # smallest k >= first with (k + 1) % hop == 0
        first += (-(first + 1)) % self.hop
        return np.arange(first, hi + 1, self.hop, dtype=np.int64)

    def process(self, v: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        v = np.asarray(v, dtype=float)
        if v.ndim == 1:
            v = v[:, None]
        ks = self.frame_ends(len(v))
        buf = np.vstack([self.tail, v]) if len(self.tail) else v
        start = self.n - len(self.tail)  # absolute index of buf[0]
        if len(ks):
            cs = np.vstack([np.zeros((1, self.n_channels)), np.cumsum(buf, axis=0)])
            end = ks - start + 1
            sums = cs[end] - cs[end - self.window]
        else:
            sums = np.zeros((0, self.n_channels))
        self.n += len(v)
        keep = self.window - 1
        self.tail = buf[max(0, len(buf) - keep):] if keep > 0 else np.zeros((0, self.n_channels))
        return ks, sums


class MovingRMS:
    """sqrt(mean(x²)) over the last `window` samples, one value per hop. Group delay ≈ window / 2."""

    def __init__(self, window: int, hop: int, n_channels: int):
        self._sum = _WindowSum(window, hop, n_channels)
        self.window, self.hop = self._sum.window, self._sum.hop

    def reset(self) -> None:
        self._sum.reset()

    def process(self, x: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        x = np.asarray(x, dtype=float)
        ks, sums = self._sum.process(x * x)
        return ks, np.sqrt(np.maximum(sums / self.window, 0.0))


class LowpassEnvelope:
    """|x| through a 2nd-order Butterworth low-pass at fc, sampled on the frame grid.

    DC group delay = sqrt(2) / (2·pi·fc) (brief §4.6). The output is a smoothed mean absolute
    value, not an RMS; calibration normalises it per channel, so the scale does not matter."""

    def __init__(self, fs: float, fc_hz: float, window: int, hop: int, n_channels: int):
        sos = signal.butter(2, fc_hz, btype="lowpass", fs=fs, output="sos")
        self._lp = StreamingSOS(sos, n_channels)
        self._grid = _WindowSum(window, hop, n_channels)  # only used for the frame grid
        self.window, self.hop = self._grid.window, self._grid.hop

    def reset(self) -> None:
        self._lp.reset()
        self._grid.reset()

    def process(self, x: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        x = np.asarray(x, dtype=float)
        if x.ndim == 1:
            x = x[:, None]
        if self._lp.zi is None and len(x):
            self._lp.zi = np.zeros((self._lp.sos.shape[0], 2, x.shape[1]))  # start from 0, not |x[0]|
        y = self._lp.process(np.abs(x))
        n0 = self._grid.n
        ks, _ = self._grid.process(np.zeros_like(y))
        return ks, np.maximum(y[ks - n0], 0.0) if len(ks) else np.zeros((0, x.shape[1]))


class WindowCount:
    """How many flagged samples (e.g. missing or saturated) fall in each envelope window."""

    def __init__(self, window: int, hop: int, n_channels: int):
        self._sum = _WindowSum(window, hop, n_channels)

    def reset(self) -> None:
        self._sum.reset()

    def process(self, flags: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        ks, sums = self._sum.process(np.asarray(flags, dtype=float))
        return ks, np.rint(sums).astype(np.int64)


def make_envelope(cfg_envelope: dict, fs: float, n_channels: int):
    """Envelope + its window/hop in samples, from config.yaml `envelope`."""
    window = max(1, int(round(cfg_envelope["rms_window_ms"] * fs / 1000.0)))
    hop = max(1, int(round(cfg_envelope["hop_ms"] * fs / 1000.0)))
    method = cfg_envelope.get("method", "rms")
    if method == "rms":
        return MovingRMS(window, hop, n_channels)
    if method == "lowpass":
        return LowpassEnvelope(fs, float(cfg_envelope["lowpass_fc_hz"]), window, hop, n_channels)
    raise ValueError(f"envelope.method must be 'rms' or 'lowpass', not {method!r}")
