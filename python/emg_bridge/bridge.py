"""The real-time bridge (brief §4.1): source -> filters -> envelope -> %MVC -> decision -> UDP.

One `Bridge.step()` reads whatever the source has, turns it into 10 ms envelope frames, decides a
slot per frame, sends one `emg` packet per frame (play mode), takes in the game's events and
logs everything. `run()` calls `step()` every few milliseconds. The live monitor reads
`snapshot()` from another thread; keys from any thread go through `on_key()` (queued).

Modes:
    calibrate   guided calibration (control/calibration.py); no packets to Unity
    play        stream to Unity; needs a calibration
    record      log only, with labelled trials (brief §6 protocol); no packets

Filter profiles (dsp/pipeline.py): one runs at a time (`profile`, switch with `set_profile()` or
the `f` key). Calibration records the raw samples and, when it ends, computes thresholds for every
profile from that one recording (offline.calibrate_profiles), so any profile can be played.
"""

from __future__ import annotations

import queue
import threading
import time
from collections import deque
from dataclasses import dataclass
from typing import Any, Callable

import numpy as np

from .control.calibration import Calibration, CalibrationSet, Procedure, as_set
from .control.decision import DecisionMachine
from .dsp.filters import MissingHold
from .dsp.pipeline import Pipeline, default_profile, describe, profile_names
from .io.shots import ShotTracker
from .net import protocol as p
from .offline import assemble, calibrate_profiles

STATUS_PRIORITY = ("no_data", "saturated", "flatline", "ok")


@dataclass
class Frame:
    k: int              # absolute sample index of the window's last sample
    t: float            # bridge clock (s) of that sample
    e: np.ndarray       # envelope (2,), volts
    a: np.ndarray       # activation (2,), fraction of MVC; NaN before calibration
    slot: int
    rest: bool
    status: str
    valid: bool         # False if the window had missing samples
    seq: int | None = None


class Bridge:
    def __init__(self, cfg: dict[str, Any], source, *, mode: str = "play",
                 calibration: Calibration | CalibrationSet | None = None, procedure: Procedure | None = None,
                 sender=None, events=None, recorder=None, player=None, trials: list[str] | None = None,
                 profile: str | None = None, protocol=None, clock: Callable[[], float] = time.perf_counter,
                 log: Callable[[str], None] = print):
        if mode not in ("calibrate", "play", "record"):
            raise ValueError(mode)
        if mode == "play" and calibration is None:
            raise ValueError("play mode needs a calibration (run calibrate first, or pass --calibration)")
        self.cfg, self.source, self.mode = cfg, source, mode
        self.calibrations = as_set(calibration)
        self.profile = profile or (self.calibrations.active if self.calibrations else default_profile(cfg))
        if profile is None and self.calibrations is not None and self.profile not in self.calibrations.names:
            self.profile = self.calibrations.names[0]
        self.calibration = self.calibrations.get(self.profile) if self.calibrations else None
        self.procedure = procedure
        self.protocol = protocol
        self._cal_chunks: list[tuple[int, np.ndarray]] = []
        self._cal_starts: dict[int, int] = {}
        self.sender, self.events, self.recorder, self.player = sender, events, recorder, player
        self.trials = trials or []
        self.trial_index, self.trial_open = 0, None
        self.clock, self.log = clock, log
        self.keys: "queue.Queue[str]" = queue.Queue()
        self.shots = ShotTracker()
        self.lock = threading.RLock()
        self.stop_requested = False
        self.started = False
        self.frames_total = 0
        self.packets = 0
        self.gaps = 0
        self.lost_samples = 0
        self.no_data_since: float | None = None
        self.source_silent = False
        self.last_frame: Frame | None = None
        self.last_prompt = ""
        self.messages: deque[str] = deque(maxlen=8)
        self._next_calib = 0.0
        self._last_rate_t, self._last_rate_frames, self.frame_rate = 0.0, 0, 0.0
        self.decision = self._make_decision() if calibration else None

    def _make_decision(self) -> DecisionMachine:
        d = self.cfg["decision"]
        return DecisionMachine(self.calibration.thresholds, d["dominance_k"], d["dwell_s"],
                               self.cfg["calibration"]["far_hysteresis_h"])

    # --- set-up -----------------------------------------------------------------------------
    def start(self) -> None:
        self.t0 = self.clock()
        self.source.start()
        fs = self.source.fs
        self.fs = fs
        n_ch = len(self.source.channels)
        self.n_ch = n_ch
        self.pipeline = Pipeline.from_config(self.cfg, self.profile, fs, n_ch)
        self.window, self.hop = self.pipeline.window, self.pipeline.hop
        self.frame_hz = fs / self.hop
        self.hold = MissingHold(n_ch)
        st = self.cfg["status"]
        rng = self.source.info.range_v
        self.sat_level = None if not rng else float(st["saturation_fraction"]) * max(abs(rng[0]), abs(rng[1]))
        self.flat_level = float(st["flatline_uv"]) * 1e-6
        self.flat_frames_needed = int(round(float(st["flatline_s"]) * self.frame_hz))
        self.flat_frames = 0
        self.no_data_gap = float(st["no_data_gap_s"])
        self.max_missing = float(st.get("max_missing_fraction", 0.25))
        self.expected_n: int | None = None
        self.base_n = 0          # absolute sample index where the DSP state was last reset
        self.t_first: float | None = None  # bridge time of sample 0 (estimated, see recorder doc)
        self.prev_a = np.full(n_ch, np.nan)
        self.calib_every = float(self.cfg["network"].get("calib_every_s", 1.0))
        # monitor buffers (decimated filtered signal, frames)
        mon = self.cfg.get("monitor", {})
        win = float(mon.get("window_s", 5))
        self.decim = max(1, int(fs // 500))
        self.sig_buf = deque(maxlen=int(win * fs / self.decim))
        self.frame_buf = deque(maxlen=int(win * self.frame_hz))
        self.started = True
        if self.procedure is not None:
            self.procedure.frame_rate_hz = self.frame_hz
        self.note(f"source: {self.source.info.kind}, {fs:g} Hz, {n_ch} channels ({', '.join(self.source.channels)})")
        self.note("filters " + describe(self.cfg, self.profile))
        if self.recorder is not None:
            self.recorder.event(0.0, "profile", self.profile, {"settings": describe(self.cfg, self.profile)})

    # --- filter profiles --------------------------------------------------------------------
    @property
    def available_profiles(self) -> list[str]:
        names = profile_names(self.cfg)
        if self.calibrations is not None:
            names = [n for n in names if n in self.calibrations.names]
        return names

    def set_profile(self, name: str) -> bool:
        """Switch the live processing chain (and its thresholds). Returns False if not possible."""
        if name == self.profile:
            return True
        if name not in profile_names(self.cfg):
            self.note(f"unknown filter profile '{name}'")
            return False
        if self.calibrations is not None and name not in self.calibrations.names:
            self.note(f"no calibration for profile '{name}': calibrate again to get every profile")
            return False
        self.profile = name
        if self.started:
            self.pipeline = Pipeline.from_config(self.cfg, name, self.fs, self.n_ch)
            self.window, self.hop = self.pipeline.window, self.pipeline.hop
            self.base_n = self.expected_n or 0
            self.flat_frames = 0
        if self.calibrations is not None:
            self.calibrations.active = name
            self.calibration = self.calibrations.get(name)
            self.decision = self._make_decision()
            self._next_calib = 0.0  # tell Unity the new thresholds at once
        t = self.now() if self.started else 0.0
        if self.recorder is not None:
            self.recorder.event(t, "profile", name, {"settings": describe(self.cfg, name)})
        self.note(f"[{t:8.3f}] filters " + describe(self.cfg, name))
        return True

    def cycle_profile(self) -> None:
        names = self.available_profiles
        if len(names) > 1:
            i = names.index(self.profile) if self.profile in names else -1
            self.set_profile(names[(i + 1) % len(names)])

    def now(self) -> float:
        return self.clock() - self.t0

    def note(self, msg: str) -> None:
        with self.lock:
            self.messages.append(msg)
        self.log(msg)

    # --- keys ------------------------------------------------------------------------------
    def on_key(self, ch: str) -> None:
        """Thread-safe: queue a key for the bridge loop."""
        self.keys.put(ch)

    def _handle_key(self, ch: str) -> None:
        if ch in ("\n", "\r", "n") and self.procedure is not None and not self.procedure.done:
            self.procedure.skip(self.now())
            return
        if ch in ("\n", "\r", "n") and self.protocol is not None and not self.protocol.done:
            self.protocol.skip(self.now())
            return
        if self.player is not None and self.player.on_key(ch):
            return
        if ch == "f":
            self.cycle_profile()
        elif ch == "k":
            self.mark("operator_mark")
        elif self.mode == "record" and self.trials:
            if ch in ("]", "."):
                self.trial_index = (self.trial_index + 1) % len(self.trials)
                self.note(f"trial: {self.trials[self.trial_index]} (Enter to start)")
            elif ch in ("[", ","):
                self.trial_index = (self.trial_index - 1) % len(self.trials)
                self.note(f"trial: {self.trials[self.trial_index]} (Enter to start)")
            elif ch in ("\n", "\r"):
                if self.trial_open is None:
                    self.trial_open = self.trials[self.trial_index]
                    self.mark(f"start:{self.trial_open}")
                else:
                    self.mark(f"stop:{self.trial_open}")
                    self.trial_open = None
                    self.trial_index = (self.trial_index + 1) % len(self.trials)

    def mark(self, label: str, t: float | None = None) -> None:
        t = self.now() if t is None else t
        if self.recorder is not None:
            self.recorder.event(t, "mark", label)
        self.note(f"[{t:8.3f}] mark {label}")

    # --- the loop ---------------------------------------------------------------------------
    def step(self) -> list[Frame]:
        while True:
            try:
                self._handle_key(self.keys.get_nowait())
            except queue.Empty:
                break
        now = self.now()
        if self.player is not None:
            self.player.update()
        if self.procedure is not None:
            before = self.procedure.index
            self.procedure.tick(now)
            if self.procedure.index != before and self.procedure.step is not None:
                s = self.procedure.step
                self._cal_starts[self.procedure.index] = self.expected_n if self.expected_n is not None else 0
                if self.recorder is not None:
                    self.recorder.event(now, "calibration", s.kind, {"muscle": s.muscle, "seconds": s.seconds})
            if self.procedure.done and self.mode == "calibrate" and not getattr(self, "_cal_finished", False):
                self._finish_calibration(now)
        if self.protocol is not None and self.mode == "play":
            before = self.protocol.index
            self.protocol.tick(now)
            if self.protocol.index != before and self.protocol.step is not None:
                s = self.protocol.step
                if self.recorder is not None:
                    self.recorder.event(now, "protocol", s.kind, s.log_fields())
                if s.profile and s.profile != self.profile:
                    self.set_profile(s.profile)
                if self.player is not None and hasattr(self.player, "on_protocol_step"):
                    self.player.on_protocol_step(s)
                self.note(f"[{now:8.3f}] protocol: {s.prompt}")

        chunk = self.source.read()
        frames = self._process(chunk, now) if len(chunk) else []
        if len(chunk):
            self.no_data_since = None
            if self.source_silent:
                self.source_silent = False
                self.note(f"[{now:8.3f}] signal back")
                if self.recorder is not None:
                    self.recorder.event(now, "status", "signal_back")
        else:
            if self.no_data_since is None:
                self.no_data_since = now
            elif not self.source_silent and now - self.no_data_since >= self.no_data_gap and self.started:
                self.source_silent = True
                self.note(f"[{now:8.3f}] no samples for {self.no_data_gap * 1000:.0f} ms: not sending (Unity shows NO SIGNAL)")
                if self.recorder is not None:
                    self.recorder.event(now, "status", "source_silent")

        for t, label in self.source.poll_marks():
            self.mark(label, t - self.t0)

        if self.events is not None:
            for t_arr, msg in self.events.poll():
                self._on_game_message(t_arr - self.t0, msg)

        if self.mode == "play" and self.sender is not None and now >= self._next_calib and self.calibration:
            th = self.calibration.thresholds
            self.sender.send_calib(th.th_on, th.th_off, th.th_far)
            self._next_calib = now + self.calib_every

        if now - self._last_rate_t >= 1.0:
            self.frame_rate = (self.frames_total - self._last_rate_frames) / max(1e-9, now - self._last_rate_t)
            self._last_rate_t, self._last_rate_frames = now, self.frames_total
        if self.recorder is not None:
            self.recorder.maybe_flush()
        return frames

    def _reset_dsp(self, n0: int) -> None:
        self.pipeline.reset()
        self.base_n = n0
        self.flat_frames = 0

    def _process(self, chunk, now: float) -> list[Frame]:
        n = len(chunk)
        t_arr = chunk.t_arrival - self.t0
        if self.expected_n is not None and chunk.n0 != self.expected_n:
            gap = chunk.n0 - self.expected_n
            self.gaps += 1
            self.lost_samples += max(0, gap)
            self.note(f"[{now:8.3f}] gap in the data: {gap} samples ({gap / self.fs * 1000:.0f} ms); filters restarted")
            if self.recorder is not None:
                self.recorder.event(now, "gap", "samples", {"from": self.expected_n, "to": chunk.n0, "lost": gap})
            self._reset_dsp(chunk.n0)
        if self.expected_n is None:
            self.base_n = chunk.n0
        self.expected_n = chunk.n_end
        # sample clock: never claim a sample happened after it arrived
        est = t_arr - chunk.n_end / self.fs
        self.t_first = est if self.t_first is None else min(self.t_first, est)
        t_samples = self.t_first + (chunk.n0 + np.arange(n)) / self.fs

        if self.recorder is not None:
            self.recorder.raw(chunk.n0, t_samples, chunk.x)
            self.recorder.chunk(t_arr, chunk.n0, n, chunk.dropped)

        if self.mode == "calibrate" and self.procedure is not None and not self.procedure.done:
            self._cal_chunks.append((chunk.n0, np.array(chunk.x, dtype=float)))
        x, missing = self.hold.process(chunk.x)
        sat = (np.abs(x) >= self.sat_level).any(axis=1) if self.sat_level is not None else None
        ks, env, miss_c, sat_c, y = self.pipeline.process(x, missing.any(axis=1), sat)

        with self.lock:
            self.sig_buf.extend(zip(t_samples[::self.decim], y[::self.decim, 0], y[::self.decim, 1]))

        frames = []
        for i, k_rel in enumerate(ks):
            k = int(k_rel) + self.base_n
            frames.append(self._frame(k, env[i], int(miss_c[i]) / self.window > self.max_missing, int(sat_c[i]) > 0))
        return frames

    def _frame(self, k: int, e: np.ndarray, has_missing: bool, saturated: bool) -> Frame:
        """has_missing: more than `status.max_missing_fraction` of the window was missing -> the
        frame is not trusted (status no_data, the decision holds its last slot)."""
        t = self.t_first + k / self.fs
        if np.all(e < self.flat_level):
            self.flat_frames += 1
        else:
            self.flat_frames = 0
        status = ("no_data" if has_missing else "saturated" if saturated
                  else "flatline" if self.flat_frames >= self.flat_frames_needed else "ok")
        valid = not has_missing
        a = np.full(2, np.nan)
        slot, rest = 0, True
        if self.calibration is not None:
            a = self.calibration.normalise(e) if valid else self.prev_a
            if not np.all(np.isfinite(a)):
                a = np.zeros(2)
            self.prev_a = a
            if valid:
                slot = self.decision.step(float(a[0]), float(a[1]), k / self.fs)
            else:
                slot = self.decision.slot  # hold the decision through missing data
            rest = self.decision.rest(float(a[0]), float(a[1]))
        if self.procedure is not None and not self.procedure.done:
            self.procedure.on_frames(e[None, :], np.array([valid]))
        seq = None
        if self.mode == "play" and self.sender is not None:
            seq = self.sender.send_emg(t, float(a[0]), float(a[1]), slot, rest, status)
            self.packets += 1
        frame = Frame(k, t, e.copy(), np.asarray(a, dtype=float).copy(), slot, rest, status, valid, seq)
        self.frames_total += 1
        self.last_frame = frame
        if self.recorder is not None:
            self.recorder.envelope(k, t, e, frame.a, slot, rest, status, seq, self.profile)
        if self.calibration is not None:
            self.shots.on_frame(t, slot, float(frame.a[0]), float(frame.a[1]))
        with self.lock:
            self.frame_buf.append((t, e[0], e[1], frame.a[0], frame.a[1], slot))
        return frame

    def _on_game_message(self, t: float, msg: dict[str, Any]) -> None:
        mtype, name = msg.get("type"), msg.get("name")
        if mtype == "event" and name == "slot_applied":
            if self.sender is not None:
                rtt = self.sender.round_trip.echoed(int(msg["seq"]))
                if rtt is not None and self.recorder is not None:
                    self.recorder.latency(t, int(msg["seq"]), rtt)
            return  # logged in latency.csv, not in events.csv
        if self.recorder is not None:
            self.recorder.event(t, mtype, name or "", {k: v for k, v in msg.items() if k not in ("type", "name")})
        if mtype == "cmd" and name == "mark":
            self.note(f"[{t:8.3f}] mark from the game: {msg.get('label')}")
            return
        if mtype == "event":
            if name in ("level_start", "level_end", "shot_void"):
                self.note(f"[{t:8.3f}] {name} " + " ".join(f"{k}={v}" for k, v in msg.items() if k not in ("type", "name")))
            row = self.shots.on_event(t, msg)
            if row is not None and self.recorder is not None:
                self.recorder.shot(row)
            if self.player is not None:
                self.player.on_event(msg, self.clock())

    def _finish_calibration(self, now: float) -> None:
        self._cal_finished = True
        proc = self.procedure
        if self.player is not None and hasattr(self.player, "on_calibration_done"):
            self.player.on_calibration_done()
        if proc.error:
            self.note(f"CALIBRATION FAILED: {proc.error}")
            if self.recorder is not None:
                self.recorder.event(now, "calibration", "failed", {"error": proc.error})
            return
        cals = self._calibrate_all_profiles()
        if cals is None or self.profile not in cals.names:
            online = proc.result
            online.profile = self.profile
            cals = CalibrationSet.single(online) if cals is None else CalibrationSet({**cals.profiles, self.profile: online})
        cals.active = self.profile
        self.calibrations = cals
        self.calibration = cals.get(self.profile)
        self.decision = self._make_decision()
        cal = self.calibration
        if self.recorder is not None:
            self.recorder.save_calibration(cals)
            self.recorder.event(now, "calibration", "done", {"passed": cal.checks_passed, "profiles": cals.names})
        for line in cal.summary_lines():
            self.note(line)
        self.note(f"thresholds for filter profiles: {', '.join(cals.names)} (f switches)")
        if not cal.checks_passed:
            self.note("!! validation failed: redo with more distinct light and strong efforts (brief §4.4)")

    def _calibrate_all_profiles(self) -> CalibrationSet | None:
        """The calibration recording through every profile (offline.calibrate_profiles)."""
        if not self._cal_chunks or self.procedure is None:
            return None
        n0, x = assemble(self._cal_chunks)
        steps = self.procedure.steps
        starts = [self._cal_starts.get(i, n0) for i in range(len(steps))]
        end = self.expected_n if self.expected_n is not None else n0 + len(x)
        try:
            cals, errors = calibrate_profiles(x, n0, self.fs, self.cfg, steps, starts, end, active=self.profile)
        except Exception as e:  # never lose the live calibration over the extra profiles
            self.note(f"other filter profiles not calibrated: {e}")
            return None
        for name, err in errors.items():
            self.note(f"profile {name}: no calibration ({err})")
        self._cal_chunks = []
        return cals

    def enter_play(self) -> None:
        """Switch from calibrate to play in the same session (python -m emg_bridge run)."""
        if self.calibration is None:
            raise RuntimeError("no calibration")
        self.mode = "play"
        self.procedure = None
        self.decision = self._make_decision()
        self._next_calib = 0.0
        if self.recorder is not None:
            self.recorder.event(self.now(), "status", "play")
        self.note("PLAY: streaming to Unity on "
                  f"{self.cfg['network']['host']}:{self.cfg['network']['out_port']}")

    @property
    def calibration_done(self) -> bool:
        return self.procedure is not None and self.procedure.done

    def run(self, duration: float | None = None, poll_s: float = 0.002, on_step: Callable[["Bridge"], None] | None = None,
            until: Callable[["Bridge"], bool] | None = None) -> None:
        if not self.started:
            self.start()
        end = None if duration is None else self.now() + duration
        try:
            while not self.stop_requested:
                self.step()
                if on_step is not None:
                    on_step(self)
                if until is not None and until(self):
                    break
                if end is not None and self.now() >= end:
                    break
                time.sleep(poll_s)
        except Exception as e:  # the monitor thread must hear about it; the session log is still closed
            self.note(f"BRIDGE ERROR: {type(e).__name__}: {e}")
            raise
        finally:
            self.stop_requested = True  # tells the monitor window to close

    def stop(self) -> None:
        self.stop_requested = True

    def close(self) -> dict[str, Any]:
        try:
            self.source.stop()
        except Exception as e:  # never lose the log over a device error
            self.note(f"source stop: {e}")
        stats = self.stats()
        if self.recorder is not None:
            self.recorder.close(**stats)
        return stats

    # --- status -----------------------------------------------------------------------------
    def stats(self) -> dict[str, Any]:
        rt = self.sender.round_trip.summary() if self.sender is not None else {}
        return {"frames": self.frames_total, "packets": self.packets, "gaps": self.gaps,
                "lost_samples": self.lost_samples, "round_trip_ms": rt,
                "device_dropped": getattr(self.source, "dropped", 0),
                "device_pads": getattr(self.source, "pads", 0)}

    def snapshot(self) -> dict[str, Any]:
        with self.lock:
            sig = np.array(self.sig_buf) if self.sig_buf else np.zeros((0, 3))
            fr = np.array(self.frame_buf) if self.frame_buf else np.zeros((0, 6))
            messages = list(self.messages)
        prompt = self.procedure.prompt(self.now()) if self.procedure is not None and not self.procedure.done else ""
        if not prompt and self.protocol is not None and not self.protocol.done and self.mode == "play":
            prompt = self.protocol.prompt(self.now())
        return {"signal": sig, "frames": fr, "last": self.last_frame, "prompt": prompt, "profile": self.profile,
                "trial": (self.trials[self.trial_index] if self.trials else None, self.trial_open),
                "calibration": self.calibration, "frame_rate": self.frame_rate,
                "fs_measured": self.recorder.fs_measured if self.recorder is not None else None,
                "rtt_ms": self.sender.round_trip.latest_ms if self.sender is not None else None,
                "messages": messages, "mode": self.mode, "silent": self.source_silent,
                "player": self.player.describe() if self.player is not None else ""}

    def status_line(self) -> str:
        f = self.last_frame
        if f is None:
            return "waiting for samples..."
        parts = [self.mode.upper(), f"filters {self.profile}", f"{self.frame_rate:5.1f} fr/s", f"status {f.status}"]
        if np.all(np.isfinite(f.a)):
            parts.append(f"a_b {f.a[0]:.2f} a_t {f.a[1]:.2f} slot {f.slot:+d}")
        else:
            parts.append(f"e_b {f.e[0] * 1e6:6.1f} µV e_t {f.e[1] * 1e6:6.1f} µV")
        if self.sender is not None and self.sender.round_trip.latest_ms is not None:
            parts.append(f"rtt {self.sender.round_trip.latest_ms:.0f} ms")
        if self.source_silent:
            parts.append("NO SAMPLES")
        return "  ".join(parts)


def frame_to_packet(frame: Frame) -> dict[str, Any]:
    """The `emg` message a frame becomes (for tests and docs)."""
    return p.emg(frame.seq or 0, frame.t, float(frame.a[0]), float(frame.a[1]), frame.slot, frame.rest, frame.status)
