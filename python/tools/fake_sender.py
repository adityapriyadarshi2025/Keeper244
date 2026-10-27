#!/usr/bin/env python3
"""Stand-in for the EMG bridge (P3). Drives the Unity game over the real UDP contract with no
electrodes and no DSP: sends `emg` packets at 100 Hz (+ `calib` every second) to port 5005 and
prints the events the game sends back on 5006, checking each against docs/PROTOCOL.md.

Run from the python/ folder, game in "EMG over UDP" mode:

    python tools/fake_sender.py               # keys: drive the keeper from this terminal
    python tools/fake_sender.py --mode auto   # hands-free keeper that reacts to each shot_cue

Keys (keys mode; the slot sticks until you press another):
    q a s d e   or  1 2 3 4 5   slot -2 -1 0 +1 +2   (left far ... right far; +1/+2 = biceps)
    space       relax (slot 0)
    p           pause / resume sending (Unity should show NO SIGNAL within 250 ms)
    o           send one late, out-of-order packet (Unity must ignore it)
    f           toggle status "flatline" (lead-off warning on the HUD)
    x, Ctrl-C   quit and print a summary

Useful test options:
    --miss 0.3                        auto keeper picks a wrong slot 30 % of the time
    --dropout-at 20 --dropout-for 2   stop sending for 2 s, 20 s after start (voids the shot in play)
    --shuffle-every 200               also send a stale packet every 200 packets
    --duration 60                     stop after 60 s
"""

from __future__ import annotations

import argparse
import math
import os
import random
import sys
import time
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))  # so "python tools/fake_sender.py" finds emg_bridge

from emg_bridge.config import load_config  # noqa: E402
from emg_bridge.net import protocol as p  # noqa: E402
from emg_bridge.net.udp_in import EventReceiver  # noqa: E402
from emg_bridge.net.udp_out import EmgSender  # noqa: E402

# Brief §4.4 worked example (fraction of MVC), [biceps, triceps]
TH_ON = (0.10, 0.10)
TH_OFF = (0.07, 0.07)
TH_FAR = (0.35, 0.35)
REST_LEVEL, LIGHT_LEVEL, STRONG_LEVEL = 0.02, 0.22, 0.55

KEY_SLOTS = {"q": -2, "a": -1, "s": 0, "d": 1, "e": 2, "1": -2, "2": -1, "3": 0, "4": 1, "5": 2, " ": 0}


def target_activations(slot: int) -> tuple[float, float]:
    """(biceps, triceps) a player would hold for a 5-zone slot: + = biceps, - = triceps, |2| = strong."""
    level = STRONG_LEVEL if abs(slot) == 2 else LIGHT_LEVEL
    if slot > 0:
        return level, REST_LEVEL
    if slot < 0:
        return REST_LEVEL, level
    return REST_LEVEL, REST_LEVEL


class Envelope:
    """Fake activations that ramp like an EMG envelope (first-order lag + a little noise)."""

    def __init__(self, tau_s: float = 0.06, noise: float = 0.004, rng: random.Random | None = None):
        self.tau = tau_s
        self.noise = noise
        self.rng = rng or random.Random(244)
        self.ab = self.at = REST_LEVEL

    def step(self, slot: int, dt: float) -> tuple[float, float, bool]:
        tb, tt = target_activations(slot)
        k = 1.0 if self.tau <= 0 else 1.0 - math.exp(-dt / self.tau)
        self.ab += (tb - self.ab) * k
        self.at += (tt - self.at) * k
        ab = max(0.0, self.ab + self.rng.gauss(0.0, self.noise))
        at = max(0.0, self.at + self.rng.gauss(0.0, self.noise))
        rest = ab < TH_OFF[0] and at < TH_OFF[1]
        return ab, at, rest


class AutoKeeper:
    """Plays the keeper from the game's own events: on `shot_cue` it moves to the target after
    `reaction` seconds (or, with probability `miss`, to a wrong slot); it relaxes `relax_after`
    seconds after `arrival`, and at once after `shot_void`. Good for hands-free end-to-end runs."""

    def __init__(self, reaction: float = 0.35, miss: float = 0.0, relax_after: float = 0.3,
                 rng: random.Random | None = None):
        self.reaction = reaction
        self.miss = miss
        self.relax_after = relax_after
        self.rng = rng or random.Random(20)
        self.current = 0
        self._move: tuple[float, int] | None = None
        self._relax_at: float | None = None

    def on_event(self, msg: dict, now: float) -> None:
        name = msg.get("name")
        if name == "shot_cue":
            zones, target = msg["zones"], msg["target"]
            slot = target
            if self.miss > 0 and self.rng.random() < self.miss:
                half = (zones - 1) // 2
                slot = self.rng.choice([s for s in range(-half, half + 1) if s != target])
            self._move = (now + self.reaction, slot)
            self._relax_at = None
        elif name == "arrival":
            self._relax_at = now + self.relax_after
        elif name in ("shot_void", "level_end"):
            self._move, self._relax_at, self.current = None, None, 0

    def slot(self, now: float) -> int:
        if self._move is not None and now >= self._move[0]:
            self.current = self._move[1]
            self._move = None
        if self._relax_at is not None and now >= self._relax_at:
            self.current = 0
            self._relax_at = None
        return self.current


class KeyReader:
    """Non-blocking single-key reads from the terminal (Windows: msvcrt; macOS/Linux: termios)."""

    def __enter__(self) -> "KeyReader":
        if os.name == "nt":
            import msvcrt
            self._msvcrt = msvcrt
        else:
            import termios
            import tty
            self._fd = sys.stdin.fileno()
            self._old = termios.tcgetattr(self._fd)
            tty.setcbreak(self._fd)
        return self

    def read(self) -> list[str]:
        keys: list[str] = []
        if os.name == "nt":
            while self._msvcrt.kbhit():
                keys.append(self._msvcrt.getwch())
        else:
            import select
            while select.select([sys.stdin], [], [], 0)[0]:
                ch = sys.stdin.read(1)
                if not ch:
                    break
                keys.append(ch)
        return keys

    def __exit__(self, *exc) -> None:
        if os.name != "nt":
            import termios
            termios.tcsetattr(self._fd, termios.TCSADRAIN, self._old)


@dataclass
class Summary:
    seconds: float = 0.0
    paused_seconds: float = 0.0
    packets: int = 0
    stale_packets: int = 0
    send_errors: int = 0
    last_send_error: str | None = None
    events: Counter = field(default_factory=Counter)
    invalid_events: int = 0
    last_invalid: str | None = None
    round_trip: dict = field(default_factory=dict)

    def lines(self) -> list[str]:
        active = self.seconds - self.paused_seconds
        rate = self.packets / active if active > 0 else 0.0
        out = [f"sent {self.packets} emg packets in {self.seconds:.1f} s ({rate:.1f} Hz while sending"
               + (f"; paused {self.paused_seconds:.1f} s" if self.paused_seconds >= 0.05 else "") + ")"
               + (f", plus {self.stale_packets} deliberately out of order" if self.stale_packets else "")]
        ev = ", ".join(f"{k} {v}" for k, v in sorted(self.events.items())) or "none (is the game running, in EMG mode?)"
        out.append(f"events from Unity: {ev}")
        if self.send_errors:
            out.append(f"SEND ERRORS: {self.send_errors} packets did not leave this computer (last: {self.last_send_error})")
        out.append(f"invalid events: {self.invalid_events}" + (f" (last: {self.last_invalid})" if self.last_invalid else ""))
        if self.round_trip:
            rt = self.round_trip
            out.append(f"round trip emg -> slot_applied: n={rt['n']}  median {rt['median_ms']:.1f} ms  "
                       f"p95 {rt['p95_ms']:.1f} ms  max {rt['max_ms']:.1f} ms")
        return out


def describe(msg: dict) -> str:
    rest = {k: v for k, v in msg.items() if k not in ("type", "name")}
    return f"{msg.get('name', msg.get('type')):<13} " + "  ".join(f"{k} {v}" for k, v in rest.items())


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    net = load_config()["network"]
    a = argparse.ArgumentParser(description=__doc__.split("\n\n")[0], formatter_class=argparse.RawDescriptionHelpFormatter,
                                epilog="Keys, options and examples: see the top of this file.")
    a.add_argument("--mode", choices=["keys", "auto"], default="keys")
    a.add_argument("--host", default=net["host"], help="where the game runs (default from config.yaml)")
    a.add_argument("--out-port", type=int, default=net["out_port"], help="game's EMG port (5005)")
    a.add_argument("--in-port", type=int, default=net["in_port"], help="port to receive game events on (5006)")
    a.add_argument("--rate", type=float, default=100.0, help="emg packets per second (envelope hop, 100 Hz)")
    a.add_argument("--reaction", type=float, default=0.35, help="auto: seconds from shot_cue to moving")
    a.add_argument("--miss", type=float, default=0.0, help="auto: probability of moving to a wrong slot")
    a.add_argument("--dropout-at", type=float, default=None, help="stop sending this many seconds after start")
    a.add_argument("--dropout-for", type=float, default=2.0, help="length of that dropout, seconds")
    a.add_argument("--shuffle-every", type=int, default=0, help="send a stale (older seq) packet every N packets")
    a.add_argument("--duration", type=float, default=None, help="quit after this many seconds")
    a.add_argument("--quiet", action="store_true", help="don't print each event")
    a.add_argument("--seed", type=int, default=244)
    return a.parse_args(argv)


def run(args: argparse.Namespace, out=print) -> Summary:
    clock = time.perf_counter
    sender = EmgSender(args.host, args.out_port, clock=clock)
    events = EventReceiver("127.0.0.1" if args.host in ("127.0.0.1", "localhost") else "0.0.0.0", args.in_port, clock=clock)
    rng = random.Random(args.seed)
    auto = AutoKeeper(args.reaction, args.miss, rng=rng) if args.mode == "auto" else None
    env = Envelope(rng=rng)
    summary = Summary()

    manual_slot, paused, flatline, stale_once = 0, False, False, False
    period = 1.0 / args.rate
    t0 = clock()
    next_send = next_calib = t0
    reader = KeyReader() if args.mode == "keys" else None
    if reader is not None:
        reader.__enter__()
    out(f"fake_sender: {args.mode} mode, {args.rate:.0f} Hz to {args.host}:{args.out_port}, "
        f"listening for events on {events.port}. " + ("Keys: q a s d e, space, p, o, f, x." if reader else "Ctrl-C to stop."))
    last_status = ""
    try:
        while True:
            now = clock()
            if args.duration is not None and now - t0 >= args.duration:
                break

            for t, msg in events.poll():
                name = msg.get("name", msg.get("type"))
                summary.events[name] += 1
                if name == "slot_applied":
                    sender.round_trip.echoed(msg["seq"], t)
                elif not args.quiet:
                    out(f"[{t - t0:8.3f}] {describe(msg)}")
                if auto is not None:
                    auto.on_event(msg, now)
            summary.invalid_events = events.invalid
            summary.last_invalid = events.last_error

            if reader is not None:
                for ch in reader.read():
                    ch = ch.lower()
                    if ch in KEY_SLOTS:
                        manual_slot = KEY_SLOTS[ch]
                    elif ch == "p":
                        paused = not paused
                    elif ch == "o":
                        stale_once = True
                    elif ch == "f":
                        flatline = not flatline
                    elif ch in ("x", "\x03"):
                        raise KeyboardInterrupt

            slot = auto.slot(now) if auto is not None else manual_slot
            in_dropout = paused or (args.dropout_at is not None and args.dropout_at <= now - t0 < args.dropout_at + args.dropout_for)

            if now >= next_send:
                ab, at, rest = env.step(slot, period)
                status = "flatline" if flatline else "ok"
                if flatline:
                    ab = at = 0.0
                if not in_dropout:
                    errors_before = sender.errors
                    seq = sender.send_emg(now - t0, ab, at, slot, rest, status)
                    if sender.errors == errors_before:
                        summary.packets += 1
                    elif sender.errors == 1:
                        out(f"  !! cannot send to {args.host}:{args.out_port}: {sender.last_error}")
                    if stale_once or (args.shuffle_every and seq % args.shuffle_every == 0 and seq > 2):
                        sender.send_emg(now - t0, ab, at, slot, rest, status, seq=seq - 2)
                        summary.stale_packets += 1
                        stale_once = False
                else:
                    summary.paused_seconds += period
                next_send += period
                if now - next_send > 0.25:  # the loop stalled: don't burst to catch up
                    next_send = now + period
            if now >= next_calib:
                if not in_dropout:
                    sender.send_calib(TH_ON, TH_OFF, TH_FAR)
                next_calib += 1.0

            if reader is not None:
                rt = sender.round_trip.latest_ms
                status_line = (f"slot {slot:+d}  {'PAUSED (no packets)' if in_dropout else 'sending'}"
                               f"{'  FLATLINE' if flatline else ''}  rtt {'-' if rt is None else f'{rt:.0f} ms'}")
                if status_line != last_status:
                    out(f"  -> {status_line}")
                    last_status = status_line

            time.sleep(max(0.0, min(next_send, next_calib) - clock() - 0.0005))
    except KeyboardInterrupt:
        pass
    finally:
        if reader is not None:
            reader.__exit__(None, None, None)
        summary.seconds = clock() - t0
        summary.round_trip = sender.round_trip.summary()
        summary.send_errors = sender.errors
        summary.last_send_error = sender.last_error
        sender.close()
        events.close()
    return summary


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    if args.mode == "keys" and not sys.stdin.isatty():
        print("keys mode needs an interactive terminal; use --mode auto", file=sys.stderr)
        return 2
    try:
        summary = run(args)
    except OSError as e:
        print(f"fake_sender: {e}", file=sys.stderr)
        return 2
    print("\n" + "\n".join(summary.lines()))
    return 1 if summary.invalid_events else 0


if __name__ == "__main__":
    sys.exit(main())
