"""One processing chain (a "profile"): filters + envelope + per-window flags, streaming.

Profiles exist to answer the handout's question: how does the filtering change the game?
config.yaml `profiles` lists them as overrides of the base `filters` / `envelope` settings:

    full      band-pass 20–450 Hz + 50 Hz notch, 100 ms RMS   (the default)
    bandpass  band-pass only, no notch
    raw       DC removal only (1 Hz high-pass), no EMG filtering
    smooth    full filters, 250 ms RMS   (steadier, slower)
    fast      full filters, 50 ms RMS    (quicker, jumpier)

The live bridge runs one profile at a time (switchable); offline code (emg_bridge/offline.py)
can run any profile over a recording, so every setting sees exactly the same samples.
"""

from __future__ import annotations

import copy
from typing import Any

import numpy as np

from .envelope import WindowCount, make_envelope
from .filters import StreamingSOS, design_chain

DEFAULT_PROFILE = "full"


def _merge(base: dict, over: dict) -> dict:
    out = copy.deepcopy(base)
    for k, v in (over or {}).items():
        out[k] = _merge(out[k], v) if isinstance(v, dict) and isinstance(out.get(k), dict) else copy.deepcopy(v)
    return out


def profile_names(cfg: dict[str, Any]) -> list[str]:
    profs = cfg.get("profiles", {}) or {}
    names = [n for n in profs if n != "default"]
    return names or [DEFAULT_PROFILE]


def default_profile(cfg: dict[str, Any]) -> str:
    return (cfg.get("profiles", {}) or {}).get("default", DEFAULT_PROFILE)


def profile_settings(cfg: dict[str, Any], name: str) -> dict[str, Any]:
    """{"filters": ..., "envelope": ...} for a profile (base config + its overrides)."""
    profs = cfg.get("profiles", {}) or {}
    if name not in profs and name != DEFAULT_PROFILE:
        raise KeyError(f"unknown filter profile '{name}'; config.yaml has {profile_names(cfg)}")
    over = profs.get(name) or {}
    return {"filters": _merge(cfg["filters"], over.get("filters", {})),
            "envelope": _merge(cfg["envelope"], over.get("envelope", {}))}


def describe(cfg: dict[str, Any], name: str) -> str:
    s = profile_settings(cfg, name)
    f, e = s["filters"], s["envelope"]
    parts = []
    if f.get("highpass", {}).get("enabled"):
        parts.append(f"high-pass {f['highpass']['cutoff_hz']:g} Hz")
    if f.get("bandpass", {}).get("enabled", True):
        parts.append(f"band-pass {f['bandpass']['low_hz']:g}–{f['bandpass']['high_hz']:g} Hz")
    if f.get("notch", {}).get("enabled"):
        parts.append("notch " + "/".join(f"{x:g}" for x in f["notch"]["freqs_hz"]) + " Hz")
    if not parts:
        parts.append("no filtering")
    env = f"RMS {e['rms_window_ms']:g} ms" if e.get("method", "rms") == "rms" else f"low-pass {e['lowpass_fc_hz']:g} Hz"
    return f"{name}: {', '.join(parts)}; {env}"


class Pipeline:
    """Streaming chain for one profile. `process()` takes held samples (no NaN) plus per-sample
    flags and returns the envelope frames that completed, with their flag counts."""

    def __init__(self, fs: float, settings: dict[str, Any], n_channels: int, name: str = DEFAULT_PROFILE):
        self.name = name
        self.fs = fs
        self.settings = settings
        self.sos = design_chain(fs, settings["filters"])
        self.chain = StreamingSOS(self.sos, n_channels)
        self.envelope = make_envelope(settings["envelope"], fs, n_channels)
        self.window, self.hop = self.envelope.window, self.envelope.hop
        self.missing = WindowCount(self.window, self.hop, 1)
        self.saturated = WindowCount(self.window, self.hop, 1)
        self.rms = settings["envelope"].get("method", "rms") == "rms"

    @classmethod
    def from_config(cls, cfg: dict[str, Any], name: str, fs: float, n_channels: int) -> "Pipeline":
        return cls(fs, profile_settings(cfg, name), n_channels, name)

    def reset(self) -> None:
        self.chain.reset()
        self.envelope.reset()
        self.missing.reset()
        self.saturated.reset()

    def process(self, x: np.ndarray, missing_rows: np.ndarray, saturated_rows: np.ndarray | None = None):
        """x: (N, C) with missing samples already held; missing_rows / saturated_rows: (N,) bool.
        Returns (ks relative to the last reset, env (F, C), missing counts (F,), saturated counts (F,), y)."""
        n = len(x)
        y = self.chain.process(x)
        y_env = y
        if missing_rows.any():  # missing samples add nothing to the window; the RMS uses the good ones only
            y_env = y.copy()
            y_env[missing_rows] = 0.0
        ks, env = self.envelope.process(y_env)
        _, miss_c = self.missing.process(missing_rows[:, None])
        if len(ks) and self.rms and miss_c.any():
            good = np.maximum(self.window - miss_c[:, 0], 1)
            env = env * np.sqrt(self.window / good)[:, None]
        sat = np.zeros((n, 1), dtype=bool) if saturated_rows is None else saturated_rows[:, None]
        _, sat_c = self.saturated.process(sat)
        return ks, env, miss_c[:, 0], sat_c[:, 0], y
