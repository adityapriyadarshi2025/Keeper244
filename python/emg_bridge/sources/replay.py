"""`ReplaySource`: streams a recorded session's raw.csv through the pipeline again (brief §5.2).

For regression tests and offline tuning on real data (E3, E4): same samples, different DSP
settings. `speed=1` paces it in real time (e.g. to drive the game); `speed=0` delivers it as fast
as the bridge can take it (offline analysis).
"""

from __future__ import annotations

import json
import time
from pathlib import Path
from typing import Callable

import numpy as np

from .base import Chunk, EMGSource, SourceInfo

UV = 1e-6


def load_raw(session_dir: str | Path) -> tuple[np.ndarray, np.ndarray, dict]:
    """(n, x volts (N, 2), meta) from a session folder written by the recorder."""
    session_dir = Path(session_dir)
    raw = session_dir / "raw.csv" if session_dir.is_dir() else session_dir
    meta_path = raw.parent / "meta.json"
    meta = json.loads(meta_path.read_text(encoding="utf-8")) if meta_path.exists() else {}
    data = np.genfromtxt(raw, delimiter=",", names=True, dtype=float)
    names = data.dtype.names
    chans = [c for c in names if c.endswith("_uv")]
    x = np.column_stack([data[c] for c in chans]) * UV
    n = data["n"].astype(np.int64) if "n" in names else np.arange(len(x))
    return n, x, meta


class ReplaySource(EMGSource):
    def __init__(self, session_dir: str | Path, speed: float = 1.0, fs: float | None = None,
                 clock: Callable[[], float] = time.perf_counter, chunk_s: float = 0.02, loop: bool = False):
        self.n_idx, self.x, meta = load_raw(session_dir)
        src = meta.get("source", {})
        fs = fs or src.get("fs") or meta.get("fs_nominal_hz")
        if not fs:
            raise ValueError("cannot tell the sample rate: pass fs= or keep meta.json next to raw.csv")
        self.info = SourceInfo("replay", float(fs), list(src.get("channels", ["biceps", "triceps"])), "V",
                               tuple(src["range_v"]) if src.get("range_v") else None,
                               {"from": str(session_dir), "speed": speed, "samples": int(len(self.x))})
        self.speed = float(speed)
        self.clock = clock
        self.chunk = max(1, int(chunk_s * self.fs))
        self.loop = loop
        self.pos = 0
        self._t_start = 0.0
        self._offset = int(self.n_idx[0]) if len(self.n_idx) else 0

    @property
    def finished(self) -> bool:
        return self.pos >= len(self.x) and not self.loop

    def start(self) -> None:
        self._t_start = self.clock()
        self.pos = 0

    def stop(self) -> None:
        pass

    def read(self) -> Chunk:
        now = self.clock()
        if self.pos >= len(self.x):
            if not self.loop or not len(self.x):
                return Chunk(int(self.n_idx[-1]) + 1 - self._offset if len(self.x) else 0, np.zeros((0, 2)), now)
            self.pos, self._t_start = 0, now
        if self.speed > 0:
            due = int((now - self._t_start) * self.fs * self.speed) - self.pos
            due = (due // self.chunk) * self.chunk
        else:
            due = self.chunk
        due = min(due, len(self.x) - self.pos)
        if due <= 0:
            return Chunk(int(self.n_idx[self.pos]) - self._offset, np.zeros((0, 2)), now)
        jumps = np.flatnonzero(np.diff(self.n_idx[self.pos:self.pos + due]) != 1)
        if len(jumps):  # a gap in the recording: deliver up to it, the bridge sees the jump next read
            due = int(jumps[0]) + 1
        sl = slice(self.pos, self.pos + due)
        n0 = int(self.n_idx[self.pos]) - self._offset
        self.pos += due
        return Chunk(n0, self.x[sl].copy(), now)
