"""Python -> Unity (port 5005): numbered `emg` packets and `calib` messages (docs/PROTOCOL.md).

Used by tools/fake_sender.py now and by the real bridge in P4.
"""

from __future__ import annotations

import socket
import statistics
import time
from collections import OrderedDict
from typing import Any, Callable

from . import protocol as p

Clock = Callable[[], float]


class RoundTrip:
    """Round-trip latency from Unity's `slot_applied` echoes (brief §5.3, experiment E8).

    Remember when each packet was sent; when Unity echoes its seq, the difference is the time from
    "bridge sends a slot" to "the keeper moved in Unity", including Unity's frame time.
    """

    def __init__(self, keep: int = 4000, clock: Clock = time.perf_counter):
        self._sent: OrderedDict[int, float] = OrderedDict()
        self._keep = keep
        self._clock = clock
        self.samples_s: list[float] = []

    def sent(self, seq: int, t: float | None = None) -> None:
        self._sent[seq] = self._clock() if t is None else t
        while len(self._sent) > self._keep:
            self._sent.popitem(last=False)

    def echoed(self, seq: int, t: float | None = None) -> float | None:
        """Round trip in seconds for packet `seq`, or None if it is unknown (too old / never sent)."""
        sent_at = self._sent.pop(seq, None)
        if sent_at is None:
            return None
        rtt = (self._clock() if t is None else t) - sent_at
        self.samples_s.append(rtt)
        return rtt

    @property
    def latest_ms(self) -> float | None:
        return self.samples_s[-1] * 1000.0 if self.samples_s else None

    def summary(self) -> dict[str, float]:
        """n, median_ms, p95_ms, max_ms (empty dict before the first echo)."""
        if not self.samples_s:
            return {}
        ms = sorted(s * 1000.0 for s in self.samples_s)
        return {
            "n": len(ms),
            "median_ms": statistics.median(ms),
            "p95_ms": ms[min(len(ms) - 1, int(round(0.95 * (len(ms) - 1))))],
            "max_ms": ms[-1],
        }


class EmgSender:
    """Sends `emg` packets with seq += 1 each, and `calib` messages. Never raises on a send."""

    def __init__(self, host: str = "127.0.0.1", port: int = p.PYTHON_TO_UNITY_PORT, clock: Clock = time.perf_counter):
        self.addr = (host, port)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.seq = 0
        self.sent = 0
        self.errors = 0
        self.last_error: str | None = None
        self.round_trip = RoundTrip(clock=clock)

    def send(self, msg: dict[str, Any]) -> bool:
        data = p.encode(msg)  # validates: a contract bug fails loudly here, not in Unity
        try:
            self.sock.sendto(data, self.addr)
        except OSError as e:
            self.errors += 1
            self.last_error = f"{type(e).__name__}: {e}"
            return False
        self.sent += 1
        return True

    def send_emg(self, t: float, ab: float, at: float, slot: int, rest: bool, status: str = "ok",
                 seq: int | None = None, with_rtt: bool = True) -> int:
        """Send one envelope sample. Returns its seq. Pass `seq` only to fake a late packet."""
        if seq is None:
            self.seq += 1
            seq = self.seq
            self.round_trip.sent(seq)
        lo, hi = 0.0, 1.5  # brief §4.7: activations clipped to [0, 1.5]
        msg = p.emg(seq, t, min(hi, max(lo, ab)), min(hi, max(lo, at)), slot, rest, status,
                    rtt_ms=self.round_trip.latest_ms if with_rtt else None)
        self.send(msg)
        return seq

    def send_calib(self, th_on: tuple[float, float], th_off: tuple[float, float], th_far: tuple[float, float]) -> bool:
        return self.send(p.calib(th_on, th_off, th_far))

    def close(self) -> None:
        self.sock.close()
