"""The data-collection protocol (brief §6), timed and prompted: the handout asks for at least
5 minutes of signal per activity or condition.

    rest        5 min relaxed                                  (noise floor, PSD, random-process stats)
    biceps      5 min of 5 s flex / 5 s relax, light and strong alternating
    triceps     the same for the triceps
    game_<p>    5 min of play per filter profile (default: full, raw, bandpass), in the order given
                (rotate it between participants), so filtering can be compared by game performance
    breaks      1 min between blocks (the operator can skip them with Enter)

Every step start goes into events.csv as type `protocol` with its block, muscle, level and profile,
which is what the analysis uses to cut the recording into conditions.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Callable

MUSCLE_ACTION = {"biceps": "push UP (biceps)", "triceps": "push DOWN (triceps)"}


@dataclass
class ProtocolStep:
    kind: str                    # ready | rest | flex | relax | break | game
    seconds: float
    prompt: str
    block: str | None = None     # condition label, e.g. rest, biceps, triceps, game_full
    muscle: str | None = None
    level: str | None = None     # light | strong (flex steps)
    profile: str | None = None   # filter profile to switch to when the step starts
    skippable: bool = False
    block_left_after: float = 0.0  # seconds of this block that follow this step (for the countdown)

    def log_fields(self) -> dict[str, Any]:
        return {k: v for k, v in {"block": self.block, "muscle": self.muscle, "level": self.level,
                                  "profile": self.profile, "seconds": self.seconds}.items() if v is not None}


def build_protocol(cfg: dict[str, Any], game_profiles: list[str] | None = None,
                   blocks: list[str] | None = None) -> list[ProtocolStep]:
    """cfg = config.yaml `protocol`. `blocks` limits which conditions run (rest, biceps, triceps, game)."""
    want = set(blocks or ["rest", "biceps", "triceps", "game"])
    ready_s, brk = float(cfg.get("ready_s", 5)), float(cfg.get("break_s", 60))
    steps: list[ProtocolStep] = []

    def pause():
        if brk > 0:
            steps.append(ProtocolStep("break", brk, "Break: relax, shake out the arm (Enter skips)", skippable=True))

    if "rest" in want:
        mins = float(cfg["rest_s"]) / 60
        steps.append(ProtocolStep("ready", ready_s, f"Next: {mins:g} min of REST. Sit still, arm relaxed on the table"))
        steps.append(ProtocolStep("rest", float(cfg["rest_s"]), "REST: relax completely, don't move", block="rest"))
        pause()
    on_s, off_s = float(cfg["on_s"]), float(cfg["off_s"])
    levels = list(cfg.get("levels", ["light", "strong"]))
    for m in ("biceps", "triceps"):
        if m not in want:
            continue
        n = max(1, int(float(cfg["contraction_block_s"]) // (on_s + off_s)))
        steps.append(ProtocolStep("ready", ready_s, f"Next: {m.upper()} block, {n * (on_s + off_s) / 60:g} min: "
                                  f"{MUSCLE_ACTION[m]} when told, relax in between", block=None, muscle=m))
        for i in range(n):
            lv = levels[i % len(levels)]
            how = "LIGHT (easy)" if lv == "light" else "STRONG (about half of maximum)"
            steps.append(ProtocolStep("flex", on_s, f"{m.upper()} {how}: {MUSCLE_ACTION[m]}", m, m, lv))
            steps.append(ProtocolStep("relax", off_s, "relax", m, m))
        pause()
    if "game" in want:
        for prof in game_profiles or list(cfg.get("game_profiles", ["full", "raw", "bandpass"])):
            mins = float(cfg["game_block_s"]) / 60
            steps.append(ProtocolStep("ready", max(ready_s, 10.0),
                                      f"Next: {mins:g} min of GAME with filters '{prof}'. Operator: start L2 in Unity "
                                      "(Enter starts the block now)", profile=prof, skippable=True))
            steps.append(ProtocolStep("game", float(cfg["game_block_s"]),
                                      f"GAME, filters '{prof}': play L2 (start it again when it ends)",
                                      block=f"game_{prof}", profile=prof))
            pause()
    if steps and steps[-1].kind == "break":
        steps.pop()
    # countdown for each block
    left = 0.0
    for s in reversed(steps):
        s.block_left_after = left if s.block else 0.0
        left = (left + s.seconds) if s.block else 0.0
    return steps


@dataclass
class ProtocolRunner:
    """Runs the steps in real time: `tick(now)` every loop; `skip(now)` for skippable waits."""

    steps: list[ProtocolStep]
    on_step: Callable[[ProtocolStep, int], None] | None = None
    index: int = -1
    step_started: float = 0.0
    done: bool = False
    _started_at: float | None = field(default=None, repr=False)

    @property
    def step(self) -> ProtocolStep | None:
        return self.steps[self.index] if 0 <= self.index < len(self.steps) else None

    @property
    def total_s(self) -> float:
        return sum(s.seconds for s in self.steps)

    def remaining(self, now: float) -> float:
        s = self.step
        return 0.0 if s is None else max(0.0, s.seconds - (now - self.step_started))

    def prompt(self, now: float) -> str:
        s = self.step
        if s is None:
            return "protocol done" if self.done else "protocol starting"
        text = f"{s.prompt}  ({self.remaining(now):.0f} s)"
        if s.block:
            left = self.remaining(now) + s.block_left_after
            text += f"   ·   {s.block} block: {int(left // 60)}:{int(left % 60):02d} left"
        return text

    def _advance(self, now: float) -> None:
        self.index += 1
        self.step_started = now
        if self.index >= len(self.steps):
            self.done = True
            return
        if self.on_step is not None:
            self.on_step(self.step, self.index)

    def tick(self, now: float) -> None:
        if self.done:
            return
        if self._started_at is None:
            self._started_at = now
        if self.index < 0 or now - self.step_started >= self.step.seconds:
            self._advance(now)

    def skip(self, now: float) -> None:
        if self.step is not None and self.step.skippable:
            self._advance(now)
