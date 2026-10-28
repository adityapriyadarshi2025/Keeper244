"""EMG features for offline analysis (brief §7.1). Not used in the real-time loop.

All functions take a 1-D signal (or an (N, C) array, column-wise) in volts and fs in Hz.
"""

from __future__ import annotations

import numpy as np
from scipy import signal


def rms(x: np.ndarray, axis: int = 0) -> np.ndarray:
    x = np.asarray(x, dtype=float)
    return np.sqrt(np.mean(x * x, axis=axis))


def mav(x: np.ndarray, axis: int = 0) -> np.ndarray:
    return np.mean(np.abs(np.asarray(x, dtype=float)), axis=axis)


def waveform_length(x: np.ndarray, axis: int = 0) -> np.ndarray:
    return np.sum(np.abs(np.diff(np.asarray(x, dtype=float), axis=axis)), axis=axis)


def zero_crossings(x: np.ndarray, threshold: float = 0.0, axis: int = 0) -> np.ndarray:
    x = np.moveaxis(np.asarray(x, dtype=float), axis, 0)
    a, b = x[:-1], x[1:]
    return np.sum((a * b < 0) & (np.abs(a - b) >= threshold), axis=0)


def snr_db(contraction: np.ndarray, rest: np.ndarray) -> np.ndarray:
    """20·log10(RMS_contraction / RMS_rest)."""
    return 20 * np.log10(rms(contraction) / rms(rest))


def psd_welch(x: np.ndarray, fs: float, segment_s: float = 1.0) -> tuple[np.ndarray, np.ndarray]:
    """Welch PSD: Hann window, `segment_s` segments (1 s → 1 Hz resolution), 50 % overlap."""
    x = np.asarray(x, dtype=float)
    nperseg = int(min(len(x), round(segment_s * fs)))
    return signal.welch(x, fs=fs, window="hann", nperseg=nperseg, noverlap=nperseg // 2, axis=0)


def median_frequency(f: np.ndarray, pxx: np.ndarray) -> np.ndarray:
    """MDF: the frequency that splits the PSD into two equal-power halves."""
    pxx = np.asarray(pxx, dtype=float)
    cum = np.cumsum(pxx, axis=0)
    half = cum[-1] / 2.0
    idx = np.argmax(cum >= half, axis=0)
    return np.asarray(f)[idx]


def mean_frequency(f: np.ndarray, pxx: np.ndarray) -> np.ndarray:
    f = np.asarray(f, dtype=float)
    pxx = np.asarray(pxx, dtype=float)
    if pxx.ndim == 2:
        f = f[:, None]
    return np.sum(f * pxx, axis=0) / np.sum(pxx, axis=0)


def onset_times(env: np.ndarray, t: np.ndarray, mu_rest: float, sd_rest: float, k: float = 3.0,
                min_duration_s: float = 0.025) -> np.ndarray:
    """Offline reference onsets: envelope > mu + k·sd sustained for ≥ min_duration_s (brief §7.1)."""
    env, t = np.asarray(env, dtype=float), np.asarray(t, dtype=float)
    above = env > mu_rest + k * sd_rest
    onsets = []
    i, n = 0, len(env)
    while i < n:
        if above[i] and (i == 0 or not above[i - 1]):
            j = i
            while j < n and above[j]:
                j += 1
            if t[j - 1] - t[i] >= min_duration_s:
                onsets.append(t[i])
            i = j
        else:
            i += 1
    return np.asarray(onsets)
