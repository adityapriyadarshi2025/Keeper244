"""Unity -> Python (port 5006): game events, timestamped on arrival (docs/PROTOCOL.md).

Timestamping here puts the raw EMG and the game events on one clock (brief §5.3).
"""

from __future__ import annotations

import socket
import time
from typing import Any, Callable

from . import protocol as p


class EventReceiver:
    """Non-blocking receiver. `poll()` returns [(arrival_time, message)] for every valid message
    waiting; invalid datagrams are counted in `invalid` and the last reason kept in `last_error`."""

    def __init__(self, host: str = "127.0.0.1", port: int = p.UNITY_TO_PYTHON_PORT,
                 clock: Callable[[], float] = time.perf_counter):
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        try:
            self.sock.bind((host, port))
        except OSError as e:
            self.sock.close()
            raise OSError(f"cannot listen on {host}:{port} ({e}). Is another bridge or fake_sender running?") from e
        self.sock.setblocking(False)
        self._clock = clock
        self.received = 0
        self.invalid = 0
        self.last_error: str | None = None

    @property
    def port(self) -> int:
        return self.sock.getsockname()[1]

    def poll(self, max_messages: int = 1000) -> list[tuple[float, dict[str, Any]]]:
        out: list[tuple[float, dict[str, Any]]] = []
        for _ in range(max_messages):
            try:
                data, _addr = self.sock.recvfrom(65535)
            except BlockingIOError:
                break
            except ConnectionResetError:
                continue  # Windows reports an earlier ICMP "port unreachable" here; harmless
            except OSError:
                break
            t = self._clock()
            self.received += 1
            try:
                out.append((t, p.parse(data)))
            except p.ProtocolError as e:
                self.invalid += 1
                self.last_error = str(e)
        return out

    def close(self) -> None:
        self.sock.close()
