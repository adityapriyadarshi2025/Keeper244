"""Load a session folder written by the bridge (docs/SESSION_LOGS.md) into convenient tables.

    s = load("data/sessions/20261102_141500_P01")
    s.x, s.raw_t            raw EMG (volts) and sample times
    s.env, s.events, s.shots, s.latency   pandas tables
    s.steps                 every calibration/protocol step with start/end time and its labels
    s.block("biceps")       (t0, t1) of a protocol block; s.raw_between(t0, t1) -> samples
"""

from __future__ import annotations

import json
import sys
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np
import pandas as pd

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))  # emg_bridge, when run from anywhere

from emg_bridge.control.calibration import CalibrationSet  # noqa: E402

UV = 1e-6


@dataclass
class Session:
    dir: Path
    meta: dict
    fs: float
    cal: CalibrationSet | None
    n: np.ndarray
    raw_t: np.ndarray
    x: np.ndarray
    env: pd.DataFrame
    events: pd.DataFrame
    shots: pd.DataFrame
    latency: pd.DataFrame
    chunks: pd.DataFrame
    steps: pd.DataFrame = field(default_factory=pd.DataFrame)

    @property
    def name(self) -> str:
        return self.dir.name

    @property
    def participant(self) -> str:
        return str(self.meta.get("participant", self.dir.name))

    @property
    def simulated(self) -> bool:
        return bool(self.meta.get("simulated")) or self.meta.get("source", {}).get("kind") == "simulated"

    def block(self, name: str) -> tuple[float, float] | None:
        rows = self.steps[self.steps["block"] == name]
        if rows.empty:
            return None
        return float(rows["t0"].min()), float(rows["t1"].max())

    def blocks(self) -> list[str]:
        b = self.steps["block"].dropna() if "block" in self.steps else pd.Series(dtype=str)
        return list(dict.fromkeys(b))

    def raw_between(self, t0: float, t1: float) -> np.ndarray:
        i0, i1 = np.searchsorted(self.raw_t, [t0, t1])
        return self.x[i0:i1]

    def raw_index(self, t0: float, t1: float) -> tuple[int, int]:
        i0, i1 = np.searchsorted(self.raw_t, [t0, t1])
        return int(i0), int(i1)

    def env_between(self, t0: float, t1: float) -> pd.DataFrame:
        return self.env[(self.env["t"] >= t0) & (self.env["t"] < t1)]

    def steps_of(self, kind: str | None = None, block: str | None = None) -> pd.DataFrame:
        s = self.steps
        if kind is not None:
            s = s[s["kind"] == kind]
        if block is not None:
            s = s[s["block"] == block]
        return s


def _steps(events: pd.DataFrame, t_end: float) -> pd.DataFrame:
    """Calibration and protocol steps as intervals (each lasts until the next one starts)."""
    rows = []
    sel = events[events["type"].isin(["calibration", "protocol"])]
    for _, e in sel.iterrows():
        if e["type"] == "calibration" and e["name"] in ("done", "failed"):
            continue
        j = e["json"]
        rows.append({"t0": e["t"], "type": e["type"], "kind": e["name"], "block": j.get("block"),
                     "muscle": j.get("muscle"), "level": j.get("level"), "profile": j.get("profile"),
                     "seconds": j.get("seconds")})
    df = pd.DataFrame(rows, columns=["t0", "type", "kind", "block", "muscle", "level", "profile", "seconds"])
    if df.empty:
        df["t1"] = []
        return df
    t1 = list(df["t0"].iloc[1:]) + [t_end]
    df["t1"] = t1
    # a step never lasts longer than its own duration (the next step may start much later, e.g. after play)
    dur = pd.to_numeric(df["seconds"], errors="coerce")
    df["t1"] = np.where(dur.notna(), np.minimum(df["t1"], df["t0"] + dur), df["t1"])
    if "calibration" in set(df["type"]):  # calibration steps carry the muscle and kind as their label
        cal = df["type"] == "calibration"
        df.loc[cal & df["kind"].isin(["mvc", "light", "strong", "rest"]), "block"] = (
            "cal_" + df.loc[cal, "kind"] + df.loc[cal, "muscle"].fillna("").map(lambda m: f"_{m}" if m else ""))
    return df


def load(path: str | Path) -> Session:
    d = Path(path)
    meta = json.loads((d / "meta.json").read_text(encoding="utf-8"))
    fs = float(meta.get("source", {}).get("fs") or meta.get("fs_nominal_hz"))
    cal = CalibrationSet.load(d / "calibration.json") if (d / "calibration.json").exists() else None
    raw = pd.read_csv(d / "raw.csv")
    chans = [c for c in raw.columns if c.endswith("_uv")]
    x = raw[chans].to_numpy(dtype=float) * UV
    env = pd.read_csv(d / "envelope.csv")
    ev = pd.read_csv(d / "events.csv", keep_default_na=False)
    ev["t"] = ev["t"].astype(float)
    ev["json"] = ev["json"].map(lambda s: json.loads(s) if s else {})
    shots = pd.read_csv(d / "shots.csv") if (d / "shots.csv").exists() else pd.DataFrame()
    lat = pd.read_csv(d / "latency.csv") if (d / "latency.csv").exists() else pd.DataFrame(columns=["t", "seq", "rtt_ms"])
    chunks = pd.read_csv(d / "chunks.csv")
    t_end = float(raw["t"].iloc[-1]) if len(raw) else 0.0
    s = Session(d, meta, fs, cal, raw["n"].to_numpy(dtype=np.int64), raw["t"].to_numpy(dtype=float), x, env, ev,
                shots, lat, chunks)
    s.steps = _steps(ev, t_end)
    # which filter profile was live for each shot (from envelope.csv at the cue)
    if not shots.empty and "profile" in env:
        idx = np.searchsorted(env["t"].to_numpy(), shots["cue_time"].to_numpy())
        idx = np.clip(idx, 0, len(env) - 1)
        s.shots["profile"] = env["profile"].to_numpy()[idx]
    return s
