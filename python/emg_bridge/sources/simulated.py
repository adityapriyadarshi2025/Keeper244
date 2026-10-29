"""`SimulatedSource`: realistic-enough 2-channel sEMG with no hardware (brief §5.2).

Per channel: white noise through the 20–450 Hz band-pass (the EMG "carrier", unit RMS) times a
drive envelope (fraction of MVC, ramped with a first-order lag), plus crosstalk from the other
muscle, broadband electrode noise, 50 Hz mains, optional motion-artifact bumps, and an ADC clip.
Samples are produced in real time and delivered in bursts like a Bluetooth link. A dropout can
be injected to test NO SIGNAL handling.

Players drive it: `KeysPlayer` (keys 1–4 / space, see `KEY_HELP`), or `AutoPlayer`, which reacts
to the game's `shot_cue` events. Both also act out the calibration prompts.
"""

from __future__ import annotations

import math
import time
from typing import Any, Callable

import numpy as np
from scipy import signal

from ..dsp.filters import StreamingSOS, design_bandpass
from .base import Chunk, EMGSource, SourceInfo

UV = 1e-6
Clock = Callable[[], float]


class SimulatedSource(EMGSource):
    def __init__(self, fs: float, cfg: dict[str, Any], channels: list[str] | None = None, clock: Clock = time.perf_counter,
                 seed: int = 244, realtime: bool = True):
        self.cfg = cfg
        self.clock = clock
        self.realtime = realtime
        self.rng = np.random.default_rng(seed)
        rng_uv = float(cfg.get("range_uv", 15000))
        self.info = SourceInfo("simulated", float(fs), list(channels or ["biceps", "triceps"]), "V",
                               (-rng_uv * UV, rng_uv * UV), {"seed": seed, **{k: v for k, v in cfg.items() if k != "player"}})
        high = min(450.0, 0.45 * fs)
        sos = design_bandpass(fs, 20.0, high, 4)
        self._carrier = StreamingSOS(sos, 2)
        _, h = signal.sosfreqz(sos, worN=8192, fs=fs)
        self._gain = 1.0 / math.sqrt(float(np.mean(np.abs(h) ** 2)))  # white noise -> unit-RMS carrier
        self._carrier.zi = np.zeros((sos.shape[0], 2, 2))
        self.drive_target = np.zeros(2)
        self.drive = np.zeros(2)
        self.n = 0                       # samples generated so far
        self._mains_phase = float(self.rng.uniform(0, 2 * math.pi))
        self._artifacts: list[tuple[int, int, float, np.ndarray]] = []
        self._dropout_until: float | None = None
        self._lost = 0
        self._t_start: float | None = None
        self.running = False

    # --- controls (players call these) ---------------------------------------------------------
    def set_drive(self, biceps: float, triceps: float) -> None:
        """Target muscle drive as a fraction of MVC; the simulated muscle follows with a lag."""
        self.drive_target = np.array([max(0.0, biceps), max(0.0, triceps)], dtype=float)

    def add_artifact(self, channels: tuple[int, ...] = (0, 1), amplitude_uv: float | None = None,
                     duration_s: float = 0.4) -> None:
        """A low-frequency motion-artifact bump (e.g. a cable tug), starting now."""
        amp = float(self.cfg.get("artifact_uv", 1500) if amplitude_uv is None else amplitude_uv)
        mask = np.zeros(2)
        mask[list(channels)] = 1.0
        self._artifacts.append((self.n, max(1, int(duration_s * self.fs)), amp, mask))

    def dropout(self, seconds: float) -> None:
        """Simulate a lost link: nothing arrives for `seconds`, and those samples are lost."""
        self._dropout_until = self.clock() + seconds

    @property
    def in_dropout(self) -> bool:
        return self._dropout_until is not None and self.clock() < self._dropout_until

    # --- EMGSource -----------------------------------------------------------------------------
    def start(self) -> None:
        self._t_start = self.clock()
        self.running = True

    def stop(self) -> None:
        self.running = False

    def generate(self, n: int) -> np.ndarray:
        """The next n samples, (n, 2), in volts. Advances the simulation."""
        if n <= 0:
            return np.zeros((0, 2))
        c = self.cfg
        tau = float(c.get("ramp_tau_s", 0.06))
        k = np.arange(1, n + 1)[:, None]
        decay = np.exp(-k / (tau * self.fs)) if tau > 0 else np.zeros((n, 1))
        drive = self.drive_target + (self.drive - self.drive_target) * decay
        self.drive = drive[-1].copy()

        carrier = self._carrier.process(self.rng.normal(size=(n, 2))) * self._gain
        emg = carrier * drive * float(c.get("mvc_uv", 400))
        xt = float(c.get("crosstalk", 0.08))
        x = emg + xt * emg[:, ::-1]
        x += float(c.get("noise_uv", 6)) * self.rng.normal(size=(n, 2))
        idx = self.n + np.arange(n)
        mains = float(c.get("mains_uv", 20)) * np.sin(2 * math.pi * float(c.get("mains_hz", 50)) * idx / self.fs + self._mains_phase)
        x += mains[:, None] * np.array([1.0, 0.8])
        keep = []
        for start, length, amp, mask in self._artifacts:
            rel = idx - start
            on = (rel >= 0) & (rel < length)
            if on.any():
                x += (amp * np.sin(np.pi * np.clip(rel, 0, length) / length) * on)[:, None] * mask
            if start + length > self.n + n:
                keep.append((start, length, amp, mask))
        self._artifacts = keep
        lim = float(c.get("range_uv", 15000))
        np.clip(x, -lim, lim, out=x)
        self.n += n
        return x * UV

    def read(self) -> Chunk:
        now = self.clock()
        if not self.running:
            return Chunk(self.n, np.zeros((0, 2)), now, 0)
        if self.realtime:
            due = int((now - self._t_start) * self.fs) - self.n
            burst = int(round(float(self.cfg.get("chunk_ms", 0)) * self.fs / 1000.0))
            if burst > 1:
                due = (due // burst) * burst
            due = min(due, int(self.fs))  # never more than 1 s at once
        else:
            due = int(self.fs * 0.01)
        if due <= 0:
            return Chunk(self.n, np.zeros((0, 2)), now, 0)
        if self.in_dropout:
            self.generate(due)           # time passes, samples are lost
            self._lost += due
            return Chunk(self.n, np.zeros((0, 2)), now, 0)
        n0 = self.n
        return Chunk(n0, self.generate(due), now, 0)


# ---------------------------------------------------------------------------------------------
# Players: who is "flexing" the simulated muscles


class SimPlayer:
    """Base: maps slots to drives and follows the calibration prompts."""

    def __init__(self, source: SimulatedSource, cfg: dict[str, Any], clock: Clock = time.perf_counter):
        self.source = source
        p = cfg.get("player", {})
        self.light = float(p.get("light", 0.22))
        self.strong = float(p.get("strong", 0.55))
        self.mvc = float(p.get("mvc", 1.0))
        self.reaction = float(p.get("reaction_s", 0.35))
        self.clock = clock
        self._pending: tuple[float, tuple[float, float]] | None = None
        self.calibrating = False

    def drive_for_slot(self, slot: int, zones: int = 5) -> tuple[float, float]:
        if slot == 0:
            return 0.0, 0.0
        level = self.strong if abs(slot) == 2 else self.light
        return (level, 0.0) if slot > 0 else (0.0, level)

    def set_slot(self, slot: int, zones: int = 5, delay: float = 0.0) -> None:
        self.schedule(self.drive_for_slot(slot, zones), delay)

    def schedule(self, drive: tuple[float, float], delay: float = 0.0) -> None:
        if delay <= 0:
            self._pending = None
            self.source.set_drive(*drive)
        else:
            self._pending = (self.clock() + delay, drive)

    def on_calibration_step(self, step) -> None:
        """Act out a calibration prompt like a cooperative participant."""
        self.calibrating = True
        levels = {"mvc": self.mvc, "light": self.light, "strong": self.strong}
        if step.kind in levels and step.muscle:
            drive = (levels[step.kind], 0.0) if step.muscle == "biceps" else (0.0, levels[step.kind])
            self.schedule(drive, delay=0.3)
        else:
            self.schedule((0.0, 0.0))

    def on_protocol_step(self, step) -> None:
        """Act out a protocol prompt (control/protocol.py): flex on cue, relax otherwise."""
        if step.kind == "flex" and step.muscle:
            level = self.strong if step.level == "strong" else self.light
            self.schedule((level, 0.0) if step.muscle == "biceps" else (0.0, level), delay=0.3)
        elif step.kind != "game":
            self.schedule((0.0, 0.0), delay=0.2)

    def on_calibration_done(self) -> None:
        self.calibrating = False
        self.schedule((0.0, 0.0))

    def on_event(self, msg: dict, now: float) -> None:  # game events (AutoPlayer reacts)
        pass

    def on_key(self, ch: str) -> bool:  # True if the key was used
        return False

    def update(self) -> None:
        if self._pending is not None and self.clock() >= self._pending[0]:
            self.source.set_drive(*self._pending[1])
            self._pending = None

    def describe(self) -> str:
        b, t = self.source.drive_target
        return f"drive biceps {b:.2f} triceps {t:.2f}"


KEY_HELP = ("1 = biceps light (R1)   2 = biceps strong (R2)   3 = triceps light (L1)   4 = triceps strong (L2)   "
            "space/0 = relax   c = co-contraction   m = motion artifact   p = 1 s dropout")


class KeysPlayer(SimPlayer):
    """Keyboard-driven simulated arm. Keys stick until another key (terminals can't see key-up)."""

    KEYS = {"1": 1, "2": 2, "3": -1, "4": -2, "0": 0, " ": 0}

    def on_key(self, ch: str) -> bool:
        ch = ch.lower()
        if ch in self.KEYS:
            self.set_slot(self.KEYS[ch])
            return True
        if ch == "c":
            self.schedule((0.3, 0.3))
            return True
        if ch == "m":
            self.source.add_artifact()
            return True
        if ch == "p":
            self.source.dropout(1.0)
            return True
        return False


class AutoPlayer(SimPlayer):
    """Hands-free player: on `shot_cue` it flexes for the target after `reaction_s` (or, with
    probability `miss`, for a wrong slot), relaxes `relax_after_s` after `arrival`, and at once
    after `shot_void`. Exercises the whole DSP + decision chain end to end."""

    def __init__(self, source: SimulatedSource, cfg: dict[str, Any], clock: Clock = time.perf_counter, seed: int = 20):
        super().__init__(source, cfg, clock)
        p = cfg.get("player", {})
        self.relax_after = float(p.get("relax_after_s", 0.3))
        self.miss = float(p.get("miss", 0.0))
        self.rng = np.random.default_rng(seed)

    def on_event(self, msg: dict, now: float) -> None:
        name = msg.get("name")
        if name == "shot_cue":
            zones, target = int(msg["zones"]), int(msg["target"])
            slot = target
            if self.miss > 0 and self.rng.random() < self.miss:
                half = (zones - 1) // 2
                slot = int(self.rng.choice([s for s in range(-half, half + 1) if s != target]))
            self.set_slot(slot, zones, delay=self.reaction)
        elif name == "arrival":
            self.schedule((0.0, 0.0), delay=self.relax_after)
        elif name in ("shot_void", "level_end"):
            self.schedule((0.0, 0.0))
