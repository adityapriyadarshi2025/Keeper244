"""A stand-in for the Unity game's round loop, at the level of slots and events (no graphics).

Used to generate complete simulated sessions quickly (tools/make_sim_session.py) and in tests.
Same rules as the game (Assets/Scripts/Core/RoundStateMachine.cs, LevelConfig assets):

    WAIT_REST (rest for 0.5 s) -> CUE (T_cue) -> KICK -> FLIGHT (T_flight) -> arrival -> FEEDBACK (1.5 s)
    saved = keeper slot == target at arrival (3-zone levels compare by sign)
    no emg packet for 250 ms during CUE/FLIGHT -> shot_void, and the same shot is replayed

It plugs into the bridge as its `events` object: `poll()` returns [(clock time, message)] like
EventReceiver; `update(now, frames)` feeds it the frames the bridge just produced.
"""

from __future__ import annotations

import random
from dataclasses import dataclass

from .net import protocol as p

LEVELS = {  # id: (zones, T_cue, T_flight)  -- Assets/Config/Levels
    "L1": (3, 1.5, 1.2),
    "L2": (5, 1.5, 1.2),
    "L3": (5, 0.9, 0.8),
}


@dataclass
class _Shot:
    number: int
    target: int


class SimGame:
    def __init__(self, level: str = "L2", shots_per_target: int = 3, feedback_s: float = 1.5, rest_gate_s: float = 0.5,
                 signal_timeout_s: float = 0.25, seed: int = 15):
        self.level = level
        self.zones, self.t_cue, self.t_flight = LEVELS[level]
        self.per_target = shots_per_target
        self.feedback_s, self.rest_gate_s, self.timeout = feedback_s, rest_gate_s, signal_timeout_s
        self.rng = random.Random(seed)
        self.out: list[tuple[float, dict]] = []
        self.active = False
        self.state = "idle"
        self.slot, self.rest, self.last_rx = 0, False, None
        self.shots: list[_Shot] = []
        self.i = 0
        self.state_t = 0.0
        self.rest_since: float | None = None
        self.saves = 0
        self.levels_played = 0
        self.results: list[tuple[str, int, int, bool]] = []  # (level, target, keeper slot, saved)

    # --- what the bridge sees ------------------------------------------------------------------
    def poll(self) -> list[tuple[float, dict]]:
        out, self.out = self.out, []
        return out

    def close(self) -> None:
        pass

    def _emit(self, now: float, name: str, **fields) -> None:
        self.out.append((now, p.event(name, **fields)))

    # --- control ---------------------------------------------------------------------------------
    def start(self, now: float) -> None:
        """Operator starts the level (and restarts it whenever it ends) until stop()."""
        self.active = True
        self._start_level(now)

    def stop(self) -> None:
        self.active = False
        self.state = "idle"

    def _start_level(self, now: float) -> None:
        half = (self.zones - 1) // 2
        targets = [t for t in range(-half, half + 1) for _ in range(self.per_target)]
        self.rng.shuffle(targets)
        self.shots = [_Shot(i + 1, t) for i, t in enumerate(targets)]
        self.i, self.saves = 0, 0
        self._emit(now, "level_start", level=self.level, zones=self.zones, shots=len(self.shots))
        self._enter("wait_rest", now)

    def _enter(self, state: str, now: float) -> None:
        self.state, self.state_t = state, now
        self.rest_since = None

    @property
    def signal(self) -> bool:
        return self.last_rx is not None

    # --- the loop --------------------------------------------------------------------------------
    def update(self, now: float, frames) -> None:
        for f in frames:
            if f.seq is not None and f.slot != self.slot:
                self._emit(now, "slot_applied", seq=int(f.seq))
            self.slot, self.rest, self.last_rx = f.slot, f.rest, now
        if not self.active or self.state == "idle":
            return
        lost = self.last_rx is None or now - self.last_rx > self.timeout
        s = self.shots[self.i] if self.i < len(self.shots) else None
        if self.state in ("cue", "flight") and lost:
            self._emit(now, "shot_void", shot=s.number, reason="no_signal")
            self._enter("wait_rest", now)
            return
        if self.state == "wait_rest":
            if lost:
                return
            if self.rest:
                self.rest_since = self.rest_since if self.rest_since is not None else now
                if now - self.rest_since >= self.rest_gate_s:
                    self._emit(now, "shot_cue", shot=s.number, level=self.level, zones=self.zones, target=s.target)
                    self._enter("cue", now)
            else:
                self.rest_since = None
        elif self.state == "cue" and now - self.state_t >= self.t_cue:
            self._emit(now, "kick", shot=s.number)
            self._enter("flight", now)
        elif self.state == "flight" and now - self.state_t >= self.t_flight:
            keeper = self.slot if self.zones == 5 else (self.slot > 0) - (self.slot < 0)
            saved = keeper == s.target
            self.saves += int(saved)
            self.results.append((self.level, s.target, keeper, saved))
            self._emit(now, "arrival", shot=s.number, target=s.target, keeper_slot=keeper, saved=saved)
            self._enter("feedback", now)
        elif self.state == "feedback" and now - self.state_t >= self.feedback_s:
            self.i += 1
            if self.i >= len(self.shots):
                self._emit(now, "level_end", level=self.level, shots=len(self.shots), saves=self.saves)
                self.levels_played += 1
                if self.active:
                    self._start_level(now)
                else:
                    self.state = "idle"
            else:
                self._enter("wait_rest", now)


class NullSender:
    """EmgSender without a socket: numbers the packets and measures the round trip on any clock."""

    def __init__(self, clock):
        from .net.udp_out import RoundTrip
        self.seq = 0
        self.sent = 0
        self.errors = 0
        self.last_error = None
        self.round_trip = RoundTrip(clock=clock)

    def send_emg(self, t, ab, at, slot, rest, status="ok", seq=None, with_rtt=True) -> int:
        self.seq += 1
        self.sent += 1
        self.round_trip.sent(self.seq)
        return self.seq

    def send_calib(self, *_a) -> bool:
        return True

    def close(self) -> None:
        pass
