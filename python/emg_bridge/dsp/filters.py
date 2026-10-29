"""Causal streaming filters for the online pipeline (brief §4.2 stages 1–2, §4.3).

Every filter here is a cascade of second-order sections run with `scipy.signal.sosfilt`,
with the per-channel state `zi` carried from one chunk to the next. Filtering a signal in
chunks therefore gives exactly the same output as filtering it in one go (tested).
Never use `filtfilt` online: it needs future samples. Offline analysis may use `zero_phase`.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np
from scipy import signal


def design_bandpass(fs: float, low_hz: float, high_hz: float, order: int = 4) -> np.ndarray:
    """Butterworth band-pass as SOS. `order` is the prototype order (scipy doubles it for a band-pass)."""
    nyq = fs / 2.0
    if not 0 < low_hz < high_hz < nyq:
        raise ValueError(f"band-pass {low_hz}-{high_hz} Hz must sit inside 0-{nyq:g} Hz (fs = {fs:g} Hz)")
    return signal.butter(order, [low_hz, high_hz], btype="bandpass", fs=fs, output="sos")


def design_notch(fs: float, f0_hz: float, q: float) -> np.ndarray:
    """IIR notch (`iirnotch`) as one SOS row. −3 dB width ≈ f0 / Q."""
    if not 0 < f0_hz < fs / 2.0:
        raise ValueError(f"notch at {f0_hz} Hz is outside 0-{fs / 2:g} Hz")
    b, a = signal.iirnotch(f0_hz, q, fs=fs)
    return signal.tf2sos(b, a)


PASSTHROUGH = np.array([[1.0, 0.0, 0.0, 1.0, 0.0, 0.0]])  # one SOS section that changes nothing


def design_chain(fs: float, filters_cfg: dict[str, Any]) -> np.ndarray:
    """The configured filters as one SOS cascade (config.yaml `filters`, or a profile's override):
    an optional DC-removal high-pass, the band-pass (unless `enabled: false`), then the notches."""
    sections = []
    hp = filters_cfg.get("highpass", {})
    if hp.get("enabled", False):
        sections.append(signal.butter(int(hp.get("order", 1)), float(hp["cutoff_hz"]), btype="highpass", fs=fs,
                                      output="sos"))
    bp = filters_cfg.get("bandpass", {})
    if bp and bp.get("enabled", True):
        high = min(float(bp["high_hz"]), 0.45 * fs)  # brief §4.2: lower the top edge if fs < 1000 Hz
        sections.append(design_bandpass(fs, float(bp["low_hz"]), high, int(bp["order"])))
    notch = filters_cfg.get("notch", {})
    if notch.get("enabled", False):
        for f0 in notch.get("freqs_hz", []):
            if f0 < fs / 2.0:
                sections.append(design_notch(fs, float(f0), float(notch["q"])))
    return np.vstack(sections) if sections else PASSTHROUGH.copy()


def zero_phase(x: np.ndarray, sos: np.ndarray) -> np.ndarray:
    """Offline only (analysis, E2): forward-backward filtering, no phase delay."""
    return signal.sosfiltfilt(sos, np.asarray(x, dtype=float), axis=0)


@dataclass
class StreamingSOS:
    """An SOS filter applied to (N, C) chunks with state carried across calls.

    The first chunk initialises the state with `sosfilt_zi(sos) * x[0]`, i.e. as if the first
    sample had been there forever, so a DC offset does not cause a start-up transient.
    """

    sos: np.ndarray
    n_channels: int
    zi: np.ndarray | None = field(default=None, repr=False)

    def __post_init__(self) -> None:
        self.sos = np.atleast_2d(np.asarray(self.sos, dtype=float))

    def reset(self) -> None:
        self.zi = None

    def process(self, x: np.ndarray) -> np.ndarray:
        x = np.asarray(x, dtype=float)
        if x.ndim == 1:
            x = x[:, None]
        if x.shape[1] != self.n_channels:
            raise ValueError(f"expected {self.n_channels} channels, got {x.shape[1]}")
        if len(x) == 0:
            return x.copy()
        if self.zi is None:
            self.zi = signal.sosfilt_zi(self.sos)[:, :, None] * x[0][None, None, :]
        y, self.zi = signal.sosfilt(self.sos, x, axis=0, zi=self.zi)
        return y


class MissingHold:
    """Replace missing samples (NaN) by the last good value per channel before filtering.

    An IIR filter that sees one NaN outputs NaN forever, so missing samples must never reach it.
    Holding the last value keeps the filter quiet; the frames that contain missing samples are
    flagged `no_data` by the envelope stage and are not used for decisions.
    """

    def __init__(self, n_channels: int):
        self.last = np.zeros(n_channels)

    def process(self, x: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        """Returns (x with NaNs filled, bool mask of the samples that were missing)."""
        x = np.array(x, dtype=float, copy=True)
        missing = ~np.isfinite(x)
        if missing.any():
            for c in range(x.shape[1]):
                col, miss = x[:, c], missing[:, c]
                if miss.all():
                    col[:] = self.last[c]
                    continue
                idx = np.where(~miss, np.arange(len(col)), -1)
                np.maximum.accumulate(idx, out=idx)
                filled = np.where(idx >= 0, col[np.clip(idx, 0, None)], self.last[c])
                col[:] = filled
        if len(x):
            self.last = x[-1].copy()
        return x, missing


def frequency_response(sos: np.ndarray, fs: float, freqs_hz: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """(magnitude_db, group_delay_s) at the given frequencies, for reports and tests."""
    freqs_hz = np.asarray(freqs_hz, dtype=float)
    _, h = signal.sosfreqz(sos, worN=freqs_hz, fs=fs)
    mag_db = 20 * np.log10(np.abs(h) + 1e-300)
    phase = np.unwrap(np.angle(h))
    w = 2 * np.pi * freqs_hz
    gd = -np.gradient(phase, w) if len(freqs_hz) > 1 else np.zeros_like(freqs_hz)
    return mag_db, gd
