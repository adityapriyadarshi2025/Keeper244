"""Session logging (brief §5.4). One folder per session; raw data is ALWAYS kept.

    data/sessions/<YYYYMMDD_HHMMSS>_<participant>/
      meta.json          participant, arm, sites, source + device info, fs nominal + measured,
                         config snapshot, git hash, run statistics (rewritten at the end)
      calibration.json   thresholds per channel, for every filter profile (when calibrated or loaded)
      raw.csv            n, t, biceps_uv, triceps_uv          every sample; NaN = missing
      chunks.csv         t_arrival, n0, n, dropped             one row per delivery from the source
      envelope.csv       k, t, e_b_uv, e_t_uv, a_b, a_t, slot, rest, status, seq, profile   100 Hz
      events.csv         t, type, name, json                   game events, marks, gaps, calibration/protocol steps
      latency.csv        t, seq, rtt_ms                        emg packet -> Unity's slot_applied, per keeper move
      shots.csv          one row per shot (io/shots.py)
      device_config.xml  BioRadio only: the device's own configuration

`t` is the bridge clock in seconds from the session start. Raw samples are timed by their index
(t = t_first + n / fs); chunks.csv keeps the PC arrival times, so the real fs and any clock drift
can be measured afterwards (`fs_measured` in meta.json is the least-squares slope of n vs arrival).
"""

from __future__ import annotations

import csv
import json
import platform
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

import numpy as np

from .shots import FIELDS as SHOT_FIELDS

ENVELOPE_FIELDS = ["k", "t", "e_b_uv", "e_t_uv", "a_b", "a_t", "slot", "rest", "status", "seq", "profile"]


def git_hash(repo: Path) -> str | None:
    try:
        out = subprocess.run(["git", "rev-parse", "--short", "HEAD"], cwd=repo, capture_output=True, text=True, timeout=3)
        return out.stdout.strip() or None
    except Exception:
        return None


def measured_fs(t_arrival: np.ndarray, n_end: np.ndarray) -> float | None:
    """Least-squares samples per second of the PC clock (needs ≥ 2 s of data)."""
    t_arrival, n_end = np.asarray(t_arrival, dtype=float), np.asarray(n_end, dtype=float)
    if len(t_arrival) != len(n_end) or len(t_arrival) < 10 or t_arrival[-1] - t_arrival[0] < 2.0:
        return None
    slope, _ = np.polyfit(t_arrival - t_arrival[0], n_end, 1)
    return float(slope)


class RunningFit:
    """The same least-squares slope, kept up to date in O(1) per point.

    `fs` may be read from another thread (the monitor) while `add` runs in the bridge thread:
    the sums live in one tuple that is replaced in a single assignment, so a reader always sees a
    consistent set. Times and counts are taken relative to the first point to keep the sums small.
    """

    def __init__(self):
        self._origin: tuple[float, float] | None = None
        self._sums = (0, 0.0, 0.0, 0.0, 0.0, 0.0)  # n, Σt, Σy, Σt², Σty, last t

    def add(self, t: float, y: float) -> None:
        if self._origin is None:
            self._origin = (t, y)
        t0, y0 = self._origin
        dt, dy = t - t0, y - y0
        n, st, sy, stt, sty, _ = self._sums
        self._sums = (n + 1, st + dt, sy + dy, stt + dt * dt, sty + dt * dy, dt)

    @property
    def fs(self) -> float | None:
        n, st, sy, stt, sty, last = self._sums
        if n < 10 or last < 2.0:
            return None
        den = n * stt - st * st
        return (n * sty - st * sy) / den if den > 0 else None


def safe_name(s: str) -> str:
    return "".join(c if c.isalnum() or c in "-_" else "_" for c in s.strip()) or "anon"


class Recorder:
    def __init__(self, root: str | Path, participant: str, meta: dict[str, Any], channels: list[str] | None = None,
                 folder: str | Path | None = None):
        self.root = Path(root)
        stamp = time.strftime("%Y%m%d_%H%M%S")
        self.dir = Path(folder) if folder else self.root / f"{stamp}_{safe_name(participant)}"
        self.dir.mkdir(parents=True, exist_ok=False)
        self.meta = {"participant": participant, "started_local": time.strftime("%Y-%m-%dT%H:%M:%S"),
                     "python": sys.version.split()[0], "platform": platform.platform(), **meta}
        chans = channels or ["biceps", "triceps"]
        self._raw = (self.dir / "raw.csv").open("w", encoding="utf-8", newline="")
        self._raw.write("n,t," + ",".join(f"{c}_uv" for c in chans) + "\n")
        self._chunks = (self.dir / "chunks.csv").open("w", encoding="utf-8", newline="")
        self._chunks.write("t_arrival,n0,n,dropped\n")
        self._env_f = (self.dir / "envelope.csv").open("w", encoding="utf-8", newline="")
        self._env = csv.writer(self._env_f)
        self._env.writerow(ENVELOPE_FIELDS)
        self._ev_f = (self.dir / "events.csv").open("w", encoding="utf-8", newline="")
        self._ev = csv.writer(self._ev_f)
        self._ev.writerow(["t", "type", "name", "json"])
        self._lat_f = (self.dir / "latency.csv").open("w", encoding="utf-8", newline="")
        self._lat_f.write("t,seq,rtt_ms\n")
        self._shots_f = (self.dir / "shots.csv").open("w", encoding="utf-8", newline="")
        self._shots = csv.DictWriter(self._shots_f, fieldnames=SHOT_FIELDS)
        self._shots.writeheader()
        self._fit = RunningFit()
        self._last_flush = time.perf_counter()
        self.samples = 0
        self.closed = False
        self.write_meta()

    # --- rows -------------------------------------------------------------------------------
    def raw(self, n0: int, t: np.ndarray, x_volts: np.ndarray) -> None:
        if len(x_volts) == 0:
            return
        n = np.arange(n0, n0 + len(x_volts))
        rows = np.column_stack([n, t, x_volts / 1e-6])
        np.savetxt(self._raw, rows, delimiter=",", fmt=["%d", "%.5f"] + ["%.3f"] * x_volts.shape[1])
        self.samples += len(x_volts)

    def chunk(self, t_arrival: float, n0: int, n: int, dropped: int) -> None:
        self._chunks.write(f"{t_arrival:.5f},{n0},{n},{dropped}\n")
        self._fit.add(t_arrival, float(n0 + n))

    def envelope(self, k: int, t: float, e: np.ndarray, a: np.ndarray, slot: int, rest: bool, status: str,
                 seq: int | None, profile: str = "") -> None:
        a_b = "" if not np.isfinite(a[0]) else f"{a[0]:.4f}"
        a_t = "" if not np.isfinite(a[1]) else f"{a[1]:.4f}"
        self._env.writerow([k, f"{t:.5f}", f"{e[0] / 1e-6:.3f}", f"{e[1] / 1e-6:.3f}", a_b, a_t, slot, int(rest),
                            status, "" if seq is None else seq, profile])

    def event(self, t: float, kind: str, name: str, payload: dict[str, Any] | None = None) -> None:
        self._ev.writerow([f"{t:.5f}", kind, name, json.dumps(payload or {}, separators=(",", ":"))])

    def latency(self, t: float, seq: int, rtt_s: float) -> None:
        """One round trip: emg packet `seq` sent -> Unity's slot_applied for it (E8)."""
        self._lat_f.write(f"{t:.5f},{seq},{rtt_s * 1000:.2f}\n")

    def shot(self, row: dict[str, Any]) -> None:
        self._shots.writerow(row)
        self._shots_f.flush()

    def save_calibration(self, cal) -> Path:
        return cal.save(self.dir / "calibration.json")

    def maybe_flush(self, every_s: float = 1.0) -> None:
        now = time.perf_counter()
        if now - self._last_flush >= every_s:
            for f in (self._raw, self._chunks, self._env_f, self._ev_f, self._lat_f):
                f.flush()
            self._last_flush = now

    # --- meta -------------------------------------------------------------------------------
    @property
    def fs_measured(self) -> float | None:
        return self._fit.fs

    def write_meta(self, **extra: Any) -> None:
        self.meta.update(extra)
        (self.dir / "meta.json").write_text(json.dumps(self.meta, indent=2, default=_json_default) + "\n", encoding="utf-8")

    def close(self, **stats: Any) -> None:
        if self.closed:
            return
        self.closed = True
        fs_meas = self.fs_measured
        self.write_meta(ended_local=time.strftime("%Y-%m-%dT%H:%M:%S"), samples=self.samples,
                        fs_measured_hz=None if fs_meas is None else round(fs_meas, 3), stats=stats)
        for f in (self._raw, self._chunks, self._env_f, self._ev_f, self._shots_f, self._lat_f):
            f.close()


def _json_default(o: Any) -> Any:
    if isinstance(o, (np.integer,)):
        return int(o)
    if isinstance(o, (np.floating,)):
        return float(o)
    if isinstance(o, np.ndarray):
        return o.tolist()
    if isinstance(o, Path):
        return str(o)
    if isinstance(o, tuple):
        return list(o)
    return str(o)
