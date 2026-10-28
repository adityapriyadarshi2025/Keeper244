"""Calibration (brief §4.4): rest, MVC and comfort holds -> per-muscle normalisation + thresholds.

    a_k(t)   = (e_k(t) - mu_rest,k) / (e_MVC,k - mu_rest,k), clipped to [0, 1.5]
    sigma_n  = sd_rest / (e_MVC - mu_rest)
    th_on    = max(0.10, 4 * sigma_n)        th_off = 0.7 * th_on
    th_far   = (a_light + a_strong) / 2      (hysteresis band +/- h in the decision)
    checks   : a_light >= th_on + 0.05  and  a_strong - a_light >= 0.15

`compute()` is pure maths on envelope arrays (tested). `Procedure` is the guided sequence of
steps the bridge runs with the participant; it collects envelope frames per step and calls
`compute()` at the end. All durations and constants come from config.yaml `calibration`.
"""

from __future__ import annotations

import json
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Iterable

import numpy as np

from .decision import Thresholds

MUSCLES = ("biceps", "triceps")


class CalibrationError(ValueError):
    """The recorded data cannot give a usable calibration (e.g. MVC not above rest)."""


@dataclass
class ChannelCalibration:
    muscle: str
    mu_rest: float
    sd_rest: float
    e_mvc: float
    sigma_n: float
    th_on: float
    th_off: float
    th_far: float
    a_light: float | None = None
    a_strong: float | None = None
    light_ok: bool | None = None
    gap_ok: bool | None = None

    def normalise(self, e: np.ndarray | float, clip: tuple[float, float] = (0.0, 1.5)) -> np.ndarray:
        a = (np.asarray(e, dtype=float) - self.mu_rest) / (self.e_mvc - self.mu_rest)
        return np.clip(a, clip[0], clip[1])


@dataclass
class Calibration:
    """Biceps and triceps calibration. Envelope units are those of the source (volts)."""

    biceps: ChannelCalibration
    triceps: ChannelCalibration
    envelope_method: str = "rms"
    units: str = "V"
    created: str = field(default_factory=lambda: time.strftime("%Y-%m-%dT%H:%M:%S"))
    clip: tuple[float, float] = (0.0, 1.5)
    notes: list[str] = field(default_factory=list)

    @property
    def channels(self) -> tuple[ChannelCalibration, ChannelCalibration]:
        return self.biceps, self.triceps

    def normalise(self, e: np.ndarray) -> np.ndarray:
        """(F, 2) envelope -> (F, 2) activations, or (2,) -> (2,)."""
        e = np.asarray(e, dtype=float)
        out = np.empty_like(e)
        out[..., 0] = self.biceps.normalise(e[..., 0], self.clip)
        out[..., 1] = self.triceps.normalise(e[..., 1], self.clip)
        return out

    @property
    def thresholds(self) -> Thresholds:
        b, t = self.biceps, self.triceps
        return Thresholds((b.th_on, t.th_on), (b.th_off, t.th_off), (b.th_far, t.th_far))

    @property
    def checks_passed(self) -> bool:
        return all(c.light_ok is not False and c.gap_ok is not False for c in self.channels)

    def to_dict(self) -> dict[str, Any]:
        d = asdict(self)
        d["clip"] = list(self.clip)
        return d

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> "Calibration":
        return cls(biceps=ChannelCalibration(**d["biceps"]), triceps=ChannelCalibration(**d["triceps"]),
                   envelope_method=d.get("envelope_method", "rms"), units=d.get("units", "V"),
                   created=d.get("created", ""), clip=tuple(d.get("clip", (0.0, 1.5))), notes=list(d.get("notes", [])))

    def save(self, path: str | Path) -> Path:
        path = Path(path)
        path.write_text(json.dumps(self.to_dict(), indent=2) + "\n", encoding="utf-8")
        return path

    @classmethod
    def load(cls, path: str | Path) -> "Calibration":
        return cls.from_dict(json.loads(Path(path).read_text(encoding="utf-8")))

    def summary_lines(self) -> list[str]:
        out = []
        for c in self.channels:
            line = (f"{c.muscle:<8} rest {c.mu_rest * 1e6:6.1f} ± {c.sd_rest * 1e6:4.1f} µV   MVC {c.e_mvc * 1e6:7.1f} µV   "
                    f"on {c.th_on:.3f}  off {c.th_off:.3f}  far {c.th_far:.3f}")
            if c.a_light is not None:
                line += f"   light {c.a_light:.2f}  strong {c.a_strong:.2f}"
                flags = []
                if c.light_ok is False:
                    flags.append("LIGHT TOO WEAK")
                if c.gap_ok is False:
                    flags.append("LIGHT/STRONG TOO CLOSE")
                if flags:
                    line += "   !! " + ", ".join(flags)
            out.append(line)
        return out


def moving_average_max(x: np.ndarray, n: int) -> float:
    """Highest n-sample moving average of x (e_MVC uses a 500 ms average)."""
    x = np.asarray(x, dtype=float)
    if len(x) == 0:
        return float("nan")
    n = max(1, min(int(n), len(x)))
    cs = np.concatenate([[0.0], np.cumsum(x)])
    return float(np.max((cs[n:] - cs[:-n]) / n))


def steady_part(x: np.ndarray, trim: float) -> np.ndarray:
    """Drop the first and last `trim` fraction of a hold (the ramps in and out)."""
    x = np.asarray(x, dtype=float)
    k = int(len(x) * trim)
    return x[k:len(x) - k] if len(x) - 2 * k >= 1 else x


def compute_channel(muscle: str, rest: np.ndarray, mvc: Iterable[np.ndarray], frame_rate_hz: float, cfg: dict[str, Any],
                    light: Iterable[np.ndarray] = (), strong: Iterable[np.ndarray] = ()) -> ChannelCalibration:
    rest = np.asarray(rest, dtype=float)
    rest = rest[np.isfinite(rest)]
    if len(rest) < 2:
        raise CalibrationError(f"{muscle}: no usable rest data")
    mu, sd = float(np.mean(rest)), float(np.std(rest, ddof=1))
    n_avg = int(round(cfg.get("mvc_smoothing_ms", 500) * frame_rate_hz / 1000.0))
    mvc_vals = [moving_average_max(m[np.isfinite(m)], n_avg) for m in mvc if np.isfinite(m).sum() > 0]
    if not mvc_vals:
        raise CalibrationError(f"{muscle}: no MVC data")
    e_mvc = float(np.nanmax(mvc_vals))
    if not e_mvc > mu + 3 * sd:
        raise CalibrationError(f"{muscle}: MVC ({e_mvc * 1e6:.1f} µV) is not clearly above rest "
                               f"({mu * 1e6:.1f} ± {sd * 1e6:.1f} µV). Check the electrodes and redo the MVC.")
    span = e_mvc - mu
    sigma_n = sd / span
    th_on = max(float(cfg["theta_on_min"]), float(cfg["theta_on_noise_k"]) * sigma_n)
    th_off = float(cfg["theta_off_ratio"]) * th_on

    trim = float(cfg.get("hold_trim", 0.2))

    def level(holds: Iterable[np.ndarray]) -> float | None:
        means = []
        for hold in holds:
            hold = np.asarray(hold, dtype=float)
            hold = steady_part(hold[np.isfinite(hold)], trim)
            if len(hold):
                means.append(float(np.mean((hold - mu) / span)))
        return float(np.mean(means)) if means else None

    a_light, a_strong = level(light), level(strong)
    if a_light is not None and a_strong is not None:
        th_far = (a_light + a_strong) / 2.0
        light_ok = a_light >= th_on + float(cfg["check_light_margin"])
        gap_ok = a_strong - a_light >= float(cfg["check_light_strong_gap"])
    else:
        th_far = float(cfg.get("default_theta_far", 0.35))
        light_ok = gap_ok = None
    return ChannelCalibration(muscle, mu, sd, e_mvc, sigma_n, th_on, th_off, th_far, a_light, a_strong, light_ok, gap_ok)


def compute(segments: dict[str, list[np.ndarray]], frame_rate_hz: float, cfg: dict[str, Any],
            envelope_method: str = "rms", units: str = "V") -> Calibration:
    """`segments` maps "rest" -> [env (F, 2)], "mvc_biceps" / "mvc_triceps" / "light_biceps" /
    "strong_biceps" / "light_triceps" / "strong_triceps" -> [env (F, 2) per trial]."""
    rest = np.vstack(segments["rest"]) if segments.get("rest") else np.zeros((0, 2))
    chans = []
    for i, m in enumerate(MUSCLES):
        chans.append(compute_channel(
            m, rest[:, i], [s[:, i] for s in segments.get(f"mvc_{m}", [])], frame_rate_hz, cfg,
            [s[:, i] for s in segments.get(f"light_{m}", [])], [s[:, i] for s in segments.get(f"strong_{m}", [])]))
    cal = Calibration(chans[0], chans[1], envelope_method=envelope_method, units=units,
                      clip=tuple(cfg.get("activation_clip", (0.0, 1.5))))
    if not cal.checks_passed:
        cal.notes.append("validation failed: redo the comfort holds with more distinct light and strong efforts")
    return cal


# ---------------------------------------------------------------------------------------------
# Guided procedure


@dataclass
class Step:
    """One step of the calibration sequence. `segment` = where its frames go (None = not recorded)."""

    kind: str                 # settle | rest | ready | mvc | light | strong | pause
    seconds: float
    prompt: str
    muscle: str | None = None
    segment: str | None = None
    skippable: bool = False   # waits the operator may skip (Enter / n)


def build_steps(cfg: dict[str, Any], comfort: bool = True, quick: bool = False) -> list[Step]:
    """The §4.4 sequence: settle, rest, MVC x3 per muscle, comfort light/strong x3 per muscle."""
    c = dict(cfg)
    if quick:
        c.update(cfg.get("quick", {}))
    steps: list[Step] = []
    if c.get("settle_s", 0) > 0:
        steps.append(Step("settle", c["settle_s"], "Electrodes on: let the gel settle (press Enter to skip)", skippable=True))
    steps.append(Step("ready", c.get("ready_s", 3), "Next: REST. Relax your arm completely"))
    steps.append(Step("rest", c["rest_s"], "REST: relax completely", segment="rest"))
    for m in MUSCLES:
        action = "push UP (biceps)" if m == "biceps" else "push DOWN (triceps)"
        for i in range(int(c["mvc_trials"])):
            steps.append(Step("ready", c.get("ready_s", 3), f"Next: MAXIMUM {m.upper()} {i + 1}/{c['mvc_trials']}: {action}", m))
            steps.append(Step("mvc", c["mvc_trial_s"], f"MAXIMUM {m.upper()}: {action} as hard as you can!", m, f"mvc_{m}"))
            last = i == int(c["mvc_trials"]) - 1 and m == MUSCLES[-1]
            if c.get("mvc_rest_s", 0) > 0 and not (last and not comfort):
                steps.append(Step("pause", c["mvc_rest_s"], "Rest (press Enter to skip)", m, skippable=True))
    if comfort:
        for m in MUSCLES:
            for level in ("light", "strong"):
                for i in range(int(c["comfort_trials"])):
                    how = "LIGHT (comfortable, easy)" if level == "light" else "STRONG (about half of maximum)"
                    steps.append(Step("ready", c.get("ready_s", 3), f"Next: {m.upper()} {how} {i + 1}/{c['comfort_trials']}", m))
                    steps.append(Step(level, c["comfort_trial_s"], f"HOLD {m.upper()} {how}", m, f"{level}_{m}"))
                    if c.get("comfort_rest_s", 0) > 0:
                        steps.append(Step("pause", c["comfort_rest_s"], "Relax", m))
    return steps


class Procedure:
    """Runs `build_steps()` in real time. The bridge calls `tick(now)` every loop and
    `on_frames(env)` with every envelope frame; when `done`, read `result` (or `error`)."""

    def __init__(self, steps: list[Step], cfg: dict[str, Any], frame_rate_hz: float, envelope_method: str = "rms",
                 units: str = "V", on_step=None):
        self.steps = steps
        self.cfg = cfg
        self.frame_rate_hz = frame_rate_hz
        self.envelope_method = envelope_method
        self.units = units
        self.on_step = on_step          # callback(step, index) when a step starts (logging, sim player)
        self.index = -1
        self.step_started = 0.0
        self.segments: dict[str, list[np.ndarray]] = {}
        self._current: list[np.ndarray] = []
        self.result: Calibration | None = None
        self.error: str | None = None
        self.done = False

    @property
    def step(self) -> Step | None:
        return self.steps[self.index] if 0 <= self.index < len(self.steps) else None

    def remaining(self, now: float) -> float:
        s = self.step
        return 0.0 if s is None else max(0.0, s.seconds - (now - self.step_started))

    def prompt(self, now: float) -> str:
        s = self.step
        if s is None:
            return "calibration done" if self.done else "calibration starting"
        return f"[{self.index + 1}/{len(self.steps)}] {s.prompt}  ({self.remaining(now):.0f} s)"

    def skip(self, now: float) -> None:
        """Operator skips a waiting step (settle / rest between MVCs)."""
        if self.step is not None and self.step.skippable:
            self._advance(now)

    def _finish_step(self) -> None:
        s = self.step
        if s is not None and s.segment and self._current:
            self.segments.setdefault(s.segment, []).append(np.vstack(self._current))
        self._current = []

    def _advance(self, now: float) -> None:
        self._finish_step()
        self.index += 1
        self.step_started = now
        if self.index >= len(self.steps):
            self._complete()
            return
        if self.on_step is not None:
            self.on_step(self.step, self.index)

    def _complete(self) -> None:
        self.done = True
        try:
            self.result = compute(self.segments, self.frame_rate_hz, self.cfg, self.envelope_method, self.units)
        except CalibrationError as e:
            self.error = str(e)

    def tick(self, now: float) -> None:
        if self.done:
            return
        if self.index < 0 or now - self.step_started >= self.step.seconds:
            self._advance(now)

    def on_frames(self, env: np.ndarray, valid: np.ndarray | None = None) -> None:
        """env: (F, 2) envelope frames; valid: (F,) bool (False = frame had missing samples)."""
        s = self.step
        if self.done or s is None or s.segment is None or len(env) == 0:
            return
        env = np.asarray(env, dtype=float)
        if valid is not None:
            env = env[np.asarray(valid, dtype=bool)]
        if len(env):
            self._current.append(env)
