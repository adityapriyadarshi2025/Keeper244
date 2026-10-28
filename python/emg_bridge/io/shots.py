"""Per-shot metrics on the bridge's clock (brief §3.9, §5.4), from game events + the bridge's slots.

Unity writes its own shots.csv (docs/GAME_LOGS.md). This one is the authoritative copy for the
analysis (E6, E8): it uses the slot Python actually decided, at 100 Hz, on the same clock as the
raw EMG. Definitions match GAME_LOGS.md; in 3-zone levels slots compare by sign.
"""

from __future__ import annotations

from collections import deque
from dataclasses import dataclass, field
from typing import Any

import numpy as np

FIELDS = ["shot_id", "level", "zones", "target_slot", "unity_keeper_slot", "unity_saved", "bridge_slot_at_arrival",
          "bridge_saved", "cue_time", "kick_time", "arrival_time", "reaction_time", "wrong_direction", "overshoot",
          "undershoot", "time_in_target_before_arrival", "mean_a_b", "mean_a_t", "false_activations_between_shots",
          "voided"]


def _sign(x: int) -> int:
    return (x > 0) - (x < 0)


@dataclass
class _Open:
    shot: int
    level: str
    zones: int
    target: int
    cue_t: float
    kick_t: float | None = None
    false_acts: int = 0


@dataclass
class ShotTracker:
    """Feed `on_frame(t, slot, a_b, a_t)` for every envelope frame and `on_event(t, msg)` for every
    game event; finished shots come back from `on_event` as dicts (FIELDS)."""

    history_s: float = 20.0
    frames: deque = field(default_factory=lambda: deque(maxlen=4000))
    current: _Open | None = None
    level: str = ""
    _wait_slot: int = 0
    _false_acts: int = 0

    def on_frame(self, t: float, slot: int, a_b: float, a_t: float) -> None:
        self.frames.append((t, slot, a_b, a_t))
        if self.current is None:  # between shots (feedback + WAIT_REST): count each departure from centre
            if slot != 0 and self._wait_slot == 0:
                self._false_acts += 1
            self._wait_slot = slot

    def on_event(self, t: float, msg: dict[str, Any]) -> dict[str, Any] | None:
        name = msg.get("name")
        if name == "level_start":
            self.level, self.current, self._false_acts = str(msg.get("level", "")), None, 0
        elif name == "shot_cue":
            self.current = _Open(int(msg["shot"]), str(msg.get("level", self.level)), int(msg["zones"]),
                                 int(msg["target"]), t, false_acts=self._false_acts)
        elif name == "kick" and self.current is not None:
            self.current.kick_t = t
        elif name == "arrival" and self.current is not None:
            row = self._finish(t, msg)
            self.current, self._false_acts = None, 0
            self._wait_slot = self.frames[-1][1] if self.frames else 0  # still flexing at arrival is not a new departure
            return row
        elif name == "shot_void" and self.current is not None:
            row = self._finish(t, None)
            self.current = None
            return row
        return None

    def _finish(self, t_arr: float, msg: dict[str, Any] | None) -> dict[str, Any]:
        s = self.current
        assert s is not None
        three = s.zones == 3
        sel = [f for f in self.frames if s.cue_t <= f[0] <= t_arr]
        conv = (lambda v: _sign(v)) if three else (lambda v: v)
        target = s.target
        slots = [conv(f[1]) for f in sel]
        at_arr = slots[-1] if slots else 0
        row: dict[str, Any] = {k: "" for k in FIELDS}
        row.update(shot_id=s.shot, level=s.level, zones=s.zones, target_slot=target, cue_time=round(s.cue_t, 4),
                   kick_time="" if s.kick_t is None else round(s.kick_t, 4), arrival_time=round(t_arr, 4),
                   bridge_slot_at_arrival=at_arr, bridge_saved=int(at_arr == target),
                   false_activations_between_shots=s.false_acts, voided=int(msg is None))
        if msg is not None:
            row.update(unity_keeper_slot=int(msg["keeper_slot"]), unity_saved=int(bool(msg["saved"])))
        if not sel:
            return row
        ts = np.array([f[0] for f in sel])
        first_move = next((v for v in slots if v != 0), 0)
        if target != 0:
            hit = next((i for i, v in enumerate(slots) if _sign(v) == _sign(target)), None)
            row["reaction_time"] = "" if hit is None else round(ts[hit] - s.cue_t, 4)
            row["wrong_direction"] = int(first_move != 0 and _sign(first_move) != _sign(target))
            row["overshoot"] = int(any(_sign(v) == _sign(target) and abs(v) > abs(target) for v in slots))
            row["undershoot"] = int(_sign(at_arr) == _sign(target) and abs(at_arr) < abs(target))
        else:
            row["wrong_direction"] = int(first_move != 0)
            row["overshoot"] = row["undershoot"] = 0
        # continuous hold of the target slot ending at arrival
        i = len(slots) - 1
        while i >= 0 and slots[i] == target:
            i -= 1
        hold = sel[i + 1:]
        row["time_in_target_before_arrival"] = round(t_arr - hold[0][0], 4) if hold else 0.0
        if hold:
            row["mean_a_b"] = round(float(np.mean([f[2] for f in hold])), 4)
            row["mean_a_t"] = round(float(np.mean([f[3] for f in hold])), 4)
        return row
