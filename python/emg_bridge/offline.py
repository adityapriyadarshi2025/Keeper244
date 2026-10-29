"""Run the bridge's processing over recorded samples, for any filter profile.

Used by the bridge itself (one calibration recording -> thresholds for every profile) and by the
analysis (experiment E4: the same raw EMG through each profile, so differences come from the
processing alone). Same code as the live path: `Pipeline` + `DecisionMachine`.
"""

from __future__ import annotations

from typing import Any, Iterable

import numpy as np

from .control.calibration import Calibration, CalibrationError, CalibrationSet, Step, as_set, compute
from .control.decision import DecisionMachine
from .dsp.filters import MissingHold
from .dsp.pipeline import Pipeline, default_profile, profile_names


def assemble(chunks: Iterable[tuple[int, np.ndarray]]) -> tuple[int, np.ndarray]:
    """[(n0, x)] -> (first n0, contiguous x) with NaN where samples were lost."""
    chunks = [(int(n0), np.asarray(x, dtype=float)) for n0, x in chunks if len(x)]
    if not chunks:
        return 0, np.zeros((0, 2))
    start = chunks[0][0]
    end = max(n0 + len(x) for n0, x in chunks)
    out = np.full((end - start, chunks[0][1].shape[1]), np.nan)
    for n0, x in chunks:
        out[n0 - start:n0 - start + len(x)] = x
    return start, out


def process(x: np.ndarray, fs: float, cfg: dict[str, Any], profile: str | None = None,
            calibration: Calibration | CalibrationSet | None = None, n0: int = 0) -> dict[str, np.ndarray]:
    """One profile over raw samples (volts, (N, 2), NaN = missing). Returns per frame:
    k (absolute sample index), t (= k / fs), e (envelope, V), valid; and with a calibration also
    a (activation), slot (decided, as live) and rest."""
    name = profile or default_profile(cfg)
    x = np.asarray(x, dtype=float)
    pipe = Pipeline.from_config(cfg, name, fs, x.shape[1])
    xf, missing = MissingHold(x.shape[1]).process(x)
    ks, env, miss_c, _, _ = pipe.process(xf, missing.any(axis=1))
    k = ks + n0
    max_missing = float(cfg.get("status", {}).get("max_missing_fraction", 0.25))
    valid = miss_c / pipe.window <= max_missing
    out: dict[str, np.ndarray] = {"k": k, "t": k / fs, "e": env, "valid": valid}
    cals = as_set(calibration)
    if cals is not None:
        cal = cals.get(name)
        d = cfg["decision"]
        dm = DecisionMachine(cal.thresholds, d["dominance_k"], d["dwell_s"], cfg["calibration"]["far_hysteresis_h"])
        a = cal.normalise(env)
        slots = np.zeros(len(k), dtype=int)
        rest = np.zeros(len(k), dtype=bool)
        prev = np.zeros(2)
        for i in range(len(k)):
            if valid[i]:
                prev = a[i]
                slots[i] = dm.step(float(a[i, 0]), float(a[i, 1]), k[i] / fs)
            else:
                a[i] = prev
                slots[i] = dm.slot
            rest[i] = dm.rest(float(a[i, 0]), float(a[i, 1]))
        out.update(a=a, slot=slots, rest=rest)
    return out


def calibrate_profiles(x: np.ndarray, n0: int, fs: float, cfg: dict[str, Any], steps: list[Step],
                       starts: list[int], end: int, profiles: list[str] | None = None,
                       active: str | None = None) -> tuple[CalibrationSet | None, dict[str, str]]:
    """Thresholds for every profile from one calibration recording.

    steps[i] started at absolute sample starts[i]; the last step ended at `end`. Returns the set
    (None if no profile worked) and {profile: error} for the ones that failed."""
    names = profiles or profile_names(cfg)
    bounds = list(starts) + [end]
    cals: dict[str, Calibration] = {}
    errors: dict[str, str] = {}
    for name in names:
        res = process(x, fs, cfg, name, n0=n0)
        segments: dict[str, list[np.ndarray]] = {}
        for i, step in enumerate(steps):
            if step.segment is None or i + 1 >= len(bounds):
                continue
            sel = (res["k"] >= bounds[i]) & (res["k"] < bounds[i + 1]) & res["valid"]
            if sel.any():
                segments.setdefault(step.segment, []).append(res["e"][sel])
        try:
            settings_env = cfg.get("profiles", {}).get(name, {}) or {}
            method = (settings_env.get("envelope", {}) or {}).get("method", cfg["envelope"].get("method", "rms"))
            hop_s = float(cfg["envelope"]["hop_ms"]) / 1000.0
            cal = compute(segments, 1.0 / hop_s, cfg["calibration"], method, "V")
            cal.profile = name
            cals[name] = cal
        except CalibrationError as e:
            errors[name] = str(e)
    if not cals:
        return None, errors
    return CalibrationSet(cals, active or default_profile(cfg)), errors
