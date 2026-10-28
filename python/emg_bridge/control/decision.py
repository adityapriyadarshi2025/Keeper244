"""Keeper slot decision (brief §4.5): hysteresis + dominance ratio + dwell, every envelope frame.

Python is the single source of truth for the slot; Unity only renders it.

    C  (0)   neither muscle active, or both active with neither >= K x the other
    R1 (+1)  biceps dominant,  th_on <= a_b < th_far   (with hysteresis)
    R2 (+2)  biceps dominant,  a_b >= th_far
    L1 (-1)  triceps dominant, th_on <= a_t < th_far
    L2 (-2)  triceps dominant, a_t >= th_far
"""

from __future__ import annotations

from dataclasses import dataclass


def schmitt(x: float, on: float, off: float, prev: bool) -> bool:
    """Hysteresis comparator: switch on at >= on, stay on while > off."""
    return x >= on if not prev else x > off


@dataclass(frozen=True)
class Thresholds:
    """Per-muscle thresholds in normalised units (fraction of MVC), as (biceps, triceps)."""

    th_on: tuple[float, float]
    th_off: tuple[float, float]
    th_far: tuple[float, float]


class DecisionMachine:
    """Feed it (a_b, a_t, now) once per frame; read `slot` (5-zone) or `slot3`.

    `hysteresis=False` or `dwell_s=0` turn the two anti-chatter mechanisms off (experiment E4
    and the tests compare the variants).
    """

    def __init__(self, thresholds: Thresholds, dominance_k: float = 1.5, dwell_s: float = 0.080,
                 far_hysteresis_h: float = 0.05, hysteresis: bool = True):
        self.th = thresholds
        self.k = float(dominance_k)
        self.dwell = float(dwell_s)
        self.h = float(far_hysteresis_h)
        self.hysteresis = bool(hysteresis)
        self.reset()

    def reset(self) -> None:
        self.act_b = self.act_t = False
        self.far = False
        self.prev_direction = 0
        self.slot = 0
        self.pending = 0
        self.pending_since = float("-inf")
        self.candidate = 0
        self.changes = 0

    def _cmp(self, x: float, on: float, off: float, prev: bool) -> bool:
        return schmitt(x, on, off, prev) if self.hysteresis else x >= on

    def step(self, a_b: float, a_t: float, now: float) -> int:
        th = self.th
        self.act_b = self._cmp(a_b, th.th_on[0], th.th_off[0], self.act_b)
        self.act_t = self._cmp(a_t, th.th_on[1], th.th_off[1], self.act_t)

        if self.act_b and (not self.act_t or a_b >= self.k * a_t):
            direction, a, th_far = +1, a_b, th.th_far[0]
        elif self.act_t and (not self.act_b or a_t >= self.k * a_b):
            direction, a, th_far = -1, a_t, th.th_far[1]
        else:
            direction, a, th_far = 0, 0.0, 0.0

        if direction == 0:
            self.far, cand = False, 0
        else:
            if direction != self.prev_direction:
                self.far = False
            if self.hysteresis:
                self.far = schmitt(a, th_far + self.h, th_far - self.h, self.far)
            else:
                self.far = a >= th_far
            cand = direction * (2 if self.far else 1)
        self.prev_direction = direction
        self.candidate = cand

        if cand != self.pending:
            self.pending, self.pending_since = cand, now
        if self.pending != self.slot and now - self.pending_since >= self.dwell - 1e-9:
            self.slot = self.pending
            self.changes += 1
        return self.slot

    @property
    def slot3(self) -> int:
        return (self.slot > 0) - (self.slot < 0)

    def rest(self, a_b: float, a_t: float) -> bool:
        """Both channels below their θ_off: drives Unity's rest gate (protocol field `rest`)."""
        return a_b < self.th.th_off[0] and a_t < self.th.th_off[1]
