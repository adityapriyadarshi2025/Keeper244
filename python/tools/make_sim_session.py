#!/usr/bin/env python3
"""Make a complete simulated session in seconds (no Unity, no hardware, no waiting): calibration,
the timed protocol blocks, and the game blocks played by the simulated player against a stand-in
for the game. The folder looks exactly like a real one, so the analysis can be built and checked
before the lab sessions (python -m analysis.figures <folder>).

    python tools/make_sim_session.py                      # full length (~50 min of data, ~1 min to make)
    python tools/make_sim_session.py --short              # 30 s blocks (quick checks)
    python tools/make_sim_session.py --participant S02 --miss 0.1 --artifacts 4 --order raw,full,bandpass
"""

from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from emg_bridge.bridge import Bridge  # noqa: E402
from emg_bridge.config import load_config  # noqa: E402
from emg_bridge.control.calibration import Procedure, build_steps  # noqa: E402
from emg_bridge.control.protocol import ProtocolRunner, build_protocol  # noqa: E402
from emg_bridge.io.recorder import Recorder  # noqa: E402
from emg_bridge.simgame import NullSender, SimGame  # noqa: E402
from emg_bridge.sources.simulated import AutoPlayer, SimulatedSource  # noqa: E402

PYTHON_ROOT = Path(__file__).resolve().parents[1]


class FakeClock:
    def __init__(self, t: float = 1000.0):
        self.t = t

    def __call__(self) -> float:
        return self.t


def make_session(cfg: dict, root: Path, participant: str = "sim01", short: bool = False, seed: int = 1,
                 miss: float = 0.05, reaction: float = 0.35, artifacts_per_min: float = 2.0, order=None,
                 level: str = "L2", dt: float = 0.01, quiet: bool = False) -> Path:
    clock = FakeClock()
    sim_cfg = dict(cfg["simulator"])
    sim_cfg["player"] = dict(sim_cfg.get("player", {}), miss=miss, reaction_s=reaction)
    src = SimulatedSource(float(cfg["acquisition"]["fs_nominal_hz"]), sim_cfg, clock=clock, seed=seed)
    player = AutoPlayer(src, sim_cfg, clock=clock, seed=seed + 1)
    cal_steps = build_steps({**cfg["calibration"], "settle_s": 0}, quick=short)
    proc = Procedure(cal_steps, cfg["calibration"], 100.0, on_step=lambda s, i: player.on_calibration_step(s))
    pcfg = dict(cfg["protocol"])
    if short:
        pcfg.update(rest_s=30, contraction_block_s=30, game_block_s=40, break_s=5)
    pcfg["break_s"] = min(float(pcfg.get("break_s", 60)), 10.0)  # nobody needs to wait in a simulation
    runner = ProtocolRunner(build_protocol(pcfg, order))
    game = SimGame(level, seed=seed + 2)
    sender = NullSender(clock)
    meta = {"command": "make_sim_session", "mode": "protocol", "arm": "right", "note": "SIMULATED (tools/make_sim_session.py)",
            "source": {"kind": "simulated", "fs": src.fs, "channels": src.channels, "units": "V",
                       "range_v": src.info.range_v, "details": src.info.details},
            "fs_nominal_hz": src.fs, "config": cfg, "simulated": True,
            "sim": {"miss": miss, "reaction_s": reaction, "artifacts_per_min": artifacts_per_min, "level": level}}
    rec = Recorder(root, participant, meta, src.channels)
    log = (lambda *_: None) if quiet else print
    b = Bridge(cfg, src, mode="calibrate", procedure=proc, sender=sender, events=game, recorder=rec, player=player,
               protocol=runner, clock=clock, log=log)
    import numpy as np
    rng = np.random.default_rng(seed + 3)
    b.start()
    t_wall = time.perf_counter()
    in_game = False
    while True:
        clock.t += dt
        frames = b.step()
        game.update(clock(), frames)
        if b.mode == "calibrate" and b.calibration_done:
            if b.calibration is None:
                raise RuntimeError("simulated calibration failed")
            b.enter_play()
        if b.mode == "play":
            s = runner.step
            want = s is not None and s.kind == "game"
            if want and not in_game:
                game.start(clock())
            elif not want and in_game:
                game.stop()
            in_game = want
            if want and artifacts_per_min > 0 and rng.random() < artifacts_per_min * dt / 60.0:
                src.add_artifact()      # cable tugs and bumps during play: the raw profile suffers most
            if runner.done:
                break
    b.close()
    rec_summary = (f"{rec.dir}\n{rec.samples / src.fs / 60:.1f} min of data, {len(game.results)} shots, "
                   f"made in {time.perf_counter() - t_wall:.0f} s")
    if not quiet:
        print(rec_summary)
    make_session.last_summary = rec_summary
    return rec.dir


def main(argv=None) -> int:
    a = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    a.add_argument("--participant", default="sim01")
    a.add_argument("--short", action="store_true")
    a.add_argument("--seed", type=int, default=1)
    a.add_argument("--miss", type=float, default=0.05, help="probability the player flexes for a wrong slot")
    a.add_argument("--reaction", type=float, default=0.35)
    a.add_argument("--artifacts", type=float, default=2.0, help="motion artifacts per minute during the games")
    a.add_argument("--order", help="game profile order, e.g. raw,full,bandpass")
    a.add_argument("--level", default="L2", choices=["L1", "L2", "L3"])
    a.add_argument("--out", help="sessions folder (default: data/sessions)")
    args = a.parse_args(argv)
    cfg = load_config()
    root = Path(args.out) if args.out else (PYTHON_ROOT / cfg["recording"]["root"]).resolve()
    order = [x.strip() for x in args.order.split(",")] if args.order else None
    make_session(cfg, root, args.participant, args.short, args.seed, args.miss, args.reaction, args.artifacts, order,
                 args.level, quiet=True)
    print(make_session.last_summary)
    return 0


if __name__ == "__main__":
    sys.exit(main())
