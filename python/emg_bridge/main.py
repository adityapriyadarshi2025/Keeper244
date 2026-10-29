"""Command line for the Keeper244 bridge. Run from the python/ folder:

    python -m emg_bridge run                      # simulator + keys: calibrate (quick), then play
    python -m emg_bridge run --player auto        # hands-free: the simulated player reacts to each shot
    python -m emg_bridge run --calibration last   # reuse the newest calibration, go straight to play
    python -m emg_bridge run --source bioradio --participant P01 --full-calibration   (Windows, real device)
    python -m emg_bridge calibrate --source bioradio --participant P01
    python -m emg_bridge protocol --source bioradio --participant P01 # calibration + timed 5-min blocks + games
    python -m emg_bridge record --source bioradio --participant P01   # free-form labelled trials, no game
    python -m emg_bridge replay ../data/sessions/<folder> --speed 0    # re-run the DSP on a recording

Every run writes a session folder under data/sessions/ (io/recorder.py). The live monitor opens
unless --no-monitor; with --no-monitor, keys are read from this terminal.
"""

from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path
from typing import Any

from .config import DEFAULT_CONFIG_PATH, load_config

PYTHON_ROOT = Path(__file__).resolve().parents[1]

RECORD_TRIALS = ["rest_30s", "mvc_biceps", "mvc_triceps", "graded_10", "graded_20", "graded_30", "graded_50",
                 "comfort_light_biceps", "comfort_strong_biceps", "comfort_light_triceps", "comfort_strong_triceps",
                 "artifact_cable_tug", "artifact_free_movement", "artifact_tapping", "artifact_mains", "fatigue_hold"]


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    a = argparse.ArgumentParser(prog="python -m emg_bridge", description="Keeper244 EMG bridge",
                                formatter_class=argparse.RawDescriptionHelpFormatter, epilog=__doc__)
    sub = a.add_subparsers(dest="command", required=True)

    def common(sp: argparse.ArgumentParser) -> None:
        sp.add_argument("--source", choices=["sim", "bioradio"], default="sim")
        sp.add_argument("--fake-sdk", action="store_true", help="bioradio: use the built-in fake SDK (any OS)")
        sp.add_argument("--mac", help="bioradio: device MAC (overrides config.yaml)")
        sp.add_argument("--participant", default="sim", help="participant id for the session folder")
        sp.add_argument("--arm", default="right")
        sp.add_argument("--note", default="", help="free text saved in meta.json (electrode sites, posture...)")
        sp.add_argument("--no-monitor", action="store_true", help="no window; keys and status in this terminal")
        sp.add_argument("--duration", type=float, help="stop after this many seconds")
        sp.add_argument("--config", default=str(DEFAULT_CONFIG_PATH))
        sp.add_argument("--player", choices=["keys", "auto"], default="keys", help="sim: who flexes the simulated arm")
        sp.add_argument("--seed", type=int, default=244)
        sp.add_argument("--profile", help="filter profile to start with (config.yaml profiles; f switches live)")

    for name, help_ in (("run", "calibrate (or load a calibration), then stream to Unity"),
                        ("protocol", "calibrate, then the timed 5-minute blocks (rest, biceps, triceps, games)"),
                        ("calibrate", "calibration only"), ("record", "record labelled trials, no game")):
        sp = sub.add_parser(name, help=help_)
        common(sp)
        if name in ("run", "calibrate", "protocol"):
            sp.add_argument("--quick", action="store_true", help="~30 s calibration (simulator development)")
            sp.add_argument("--full-calibration", action="store_true", help="the full brief §4.4 calibration")
            sp.add_argument("--no-comfort", action="store_true", help="skip the light/strong holds (3-zone only)")
        if name in ("run", "protocol"):
            sp.add_argument("--calibration", help="calibration.json to use, or 'last' = this participant's newest")
        if name == "protocol":
            sp.add_argument("--game-profiles", help="order of the game blocks, e.g. raw,full,bandpass "
                                                    "(rotate between participants; default from config.yaml)")
            sp.add_argument("--blocks", help="only these conditions, e.g. rest,biceps (default: rest,biceps,triceps,game)")
            sp.add_argument("--short", action="store_true", help="30 s blocks instead of 5 min (rehearsal only)")
        if name == "record":
            sp.add_argument("--calibration", help="optional: also log activations and slots")

    rp = sub.add_parser("replay", help="stream a recorded session through the pipeline")
    rp.add_argument("session", help="session folder (with raw.csv and meta.json)")
    rp.add_argument("--speed", type=float, default=1.0, help="1 = real time, 0 = as fast as possible")
    rp.add_argument("--calibration", help="calibration.json (default: the session's own)")
    rp.add_argument("--profile", help="filter profile to process the recording with (default: config.yaml default)")
    rp.add_argument("--to-unity", action="store_true", help="send packets to the game (needs --speed 1)")
    rp.add_argument("--config", default=str(DEFAULT_CONFIG_PATH))
    rp.add_argument("--no-monitor", action="store_true")
    rp.add_argument("--duration", type=float, help="stop after this many seconds")
    rp.add_argument("--participant", default="replay")
    return a.parse_args(argv)


def find_last_calibration(root: Path, participant: str) -> Path | None:
    """The newest calibration.json of this participant (session folders end in _<participant>)."""
    from .io.recorder import safe_name
    cands = sorted(root.glob(f"*_{safe_name(participant)}/calibration.json"), key=lambda p: p.stat().st_mtime)
    return cands[-1] if cands else None


def make_source(args: argparse.Namespace, cfg: dict[str, Any], log):
    fs = float(cfg["acquisition"]["fs_nominal_hz"])
    chans = list(cfg["acquisition"]["channels"])
    if getattr(args, "command", "") == "replay":
        from .sources.replay import ReplaySource
        return ReplaySource(args.session, speed=args.speed), None
    if args.source == "sim":
        from .sources.simulated import AutoPlayer, KeysPlayer, SimulatedSource
        src = SimulatedSource(fs, cfg["simulator"], chans, seed=args.seed)
        player = (AutoPlayer if args.player == "auto" else KeysPlayer)(src, cfg["simulator"])
        return src, player
    from .sources.bioradio import BioRadioSource
    sdk = None
    if args.fake_sdk:
        from .sources.fake_bioradio import FakeBioRadioSdk
        sdk = FakeBioRadioSdk(fs=fs)
    return BioRadioSource(cfg["bioradio"], chans, sdk=sdk, mac=args.mac, python_root=PYTHON_ROOT, log=log), None


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    cfg = load_config(args.config)
    from .bridge import Bridge
    from .control.calibration import CalibrationSet, Procedure, build_steps
    from .dsp.pipeline import profile_names
    from .io.recorder import Recorder, git_hash
    from .net.udp_in import EventReceiver
    from .net.udp_out import EmgSender

    log = print
    root = (PYTHON_ROOT / cfg["recording"]["root"]).resolve()
    try:
        source, player = make_source(args, cfg, log)
    except Exception as e:
        print(f"bridge: {e}", file=sys.stderr)
        return 2

    calibration = None
    cal_arg = getattr(args, "calibration", None)
    if args.command == "replay" and not cal_arg:
        own = Path(args.session) / "calibration.json"
        cal_arg = str(own) if own.exists() else None
    if cal_arg:
        path = find_last_calibration(root, args.participant) if cal_arg == "last" else Path(cal_arg)
        if path is None or not path.exists():
            who = f" for participant '{args.participant}'" if cal_arg == "last" else ""
            print(f"bridge: no calibration found{who} ({cal_arg}). Run without --calibration to calibrate first.",
                  file=sys.stderr)
            return 2
        calibration = CalibrationSet.load(path)
        log(f"calibration: {path}")

    if args.command == "replay":
        mode = "play" if calibration is not None else "record"
    elif args.command == "record":
        mode = "record"
    elif args.command in ("run", "protocol") and calibration is not None:
        mode = "play"
    else:
        mode = "calibrate"

    procedure = None
    if mode == "calibrate":
        quick = args.quick or (args.source == "sim" and not args.full_calibration)
        steps = build_steps(cfg["calibration"], comfort=not args.no_comfort, quick=quick)

        def on_cal_step(step, _i):
            if player is not None:
                player.on_calibration_step(step)

        procedure = Procedure(steps, cfg["calibration"], frame_rate_hz=100.0, envelope_method=cfg["envelope"]["method"],
                              on_step=on_cal_step)
        log(f"calibration: {len(steps)} steps, ~{sum(s.seconds for s in steps):.0f} s"
            + (" (quick)" if quick else "") + ". Enter skips a wait.")

    protocol = None
    if args.command == "protocol":
        from .control.protocol import ProtocolRunner, build_protocol
        pcfg = dict(cfg["protocol"])
        if args.short:
            pcfg.update(rest_s=30, contraction_block_s=30, game_block_s=30, break_s=5)
        order = [x.strip() for x in args.game_profiles.split(",")] if args.game_profiles else None
        for prof in order or []:
            if prof not in profile_names(cfg):
                print(f"bridge: unknown filter profile '{prof}' in --game-profiles", file=sys.stderr)
                return 2
        blocks = [x.strip() for x in args.blocks.split(",")] if args.blocks else None
        protocol = ProtocolRunner(build_protocol(pcfg, order, blocks))
        log(f"protocol: {len(protocol.steps)} steps, {protocol.total_s / 60:.0f} min after calibration. "
            "Enter skips a break.")

    net = cfg["network"]
    to_unity = args.command in ("run", "protocol") or (args.command == "replay" and args.to_unity)
    sender = EmgSender(net["host"], net["out_port"]) if to_unity else None
    events = None
    if to_unity:
        try:
            events = EventReceiver("127.0.0.1", net["in_port"])
        except OSError as e:
            print(f"bridge: {e}", file=sys.stderr)
            return 2

    try:
        # BioRadio: connect first, so the device facts go into meta.json
        if hasattr(source, "connect"):
            source.connect()
    except Exception as e:
        print(f"bridge: {e}", file=sys.stderr)
        return 2

    info = source.info
    meta = {"command": args.command, "mode": mode, "arm": getattr(args, "arm", None), "note": getattr(args, "note", ""),
            "source": {"kind": info.kind, "fs": info.fs, "channels": info.channels, "units": info.units,
                       "range_v": info.range_v, "details": info.details},
            "fs_nominal_hz": float(cfg["acquisition"]["fs_nominal_hz"]), "config": cfg,
            "git": git_hash(PYTHON_ROOT), "calibration_from": cal_arg}
    recorder = Recorder(root, args.participant, meta, info.channels)
    if hasattr(source, "save_device_config"):
        source.save_device_config(recorder.dir / "device_config.xml")
    if calibration is not None:
        recorder.save_calibration(calibration)
    log(f"session: {recorder.dir}")

    profile = getattr(args, "profile", None)
    if profile is not None and profile not in profile_names(cfg):
        print(f"bridge: unknown filter profile '{profile}'; config.yaml has {profile_names(cfg)}", file=sys.stderr)
        return 2
    if profile is not None and calibration is not None and profile not in calibration.names:
        print(f"bridge: the calibration has no thresholds for profile '{profile}' (it has {calibration.names}); "
              "calibrate again", file=sys.stderr)
        return 2
    bridge = Bridge(cfg, source, mode=mode, calibration=calibration, procedure=procedure, sender=sender, events=events,
                    profile=profile, protocol=protocol,
                    recorder=recorder, player=player, trials=RECORD_TRIALS if mode == "record" else None, log=log)

    def on_step(b: Bridge) -> None:
        if b.mode == "calibrate" and b.calibration_done:
            if b.calibration is None or args.command == "calibrate":
                b.stop()
            elif args.command in ("run", "protocol"):
                b.enter_play()
        if b.protocol is not None and b.protocol.done:
            b.note("protocol complete")
            b.stop()
        src = b.source
        if getattr(src, "finished", False):
            b.stop()

    help_text = _help(args, mode, player)
    try:
        if args.no_monitor:
            _run_terminal(bridge, args, on_step, help_text)
        else:
            try:
                from .monitor import run_monitor
            except ImportError as e:
                print(f"bridge: the monitor needs pyqtgraph + PySide6 ({e}); use --no-monitor", file=sys.stderr)
                return 2
            run_monitor(bridge, f"Keeper244 bridge · {args.command} · {args.participant}", help_text,
                        run_kwargs={"duration": getattr(args, "duration", None), "on_step": on_step})
    except KeyboardInterrupt:
        pass
    finally:
        stats = bridge.close()
        if sender is not None:
            sender.close()
        if events is not None:
            events.close()
    log(f"\nsaved {recorder.dir}")
    log(f"frames {stats['frames']}, packets {stats['packets']}, gaps {stats['gaps']}"
        + (f", round trip median {stats['round_trip_ms']['median_ms']:.1f} ms" if stats["round_trip_ms"] else ""))
    if recorder.fs_measured:
        log(f"fs measured {recorder.fs_measured:.2f} Hz (nominal {info.fs:g})")
    if mode == "calibrate" and bridge.calibration is None:
        return 1
    return 0


def _help(args, mode: str, player) -> str:
    keys = []
    if player is not None and hasattr(player, "KEYS"):
        from .sources.simulated import KEY_HELP
        keys.append("SIM: " + KEY_HELP)
    if mode == "calibrate" or args.command == "protocol":
        keys.append("Enter = skip a wait or break")
    if mode == "record":
        keys.append("[ ] = choose trial   Enter = start / stop it")
    keys.append("f = next filter profile   k = mark   x / Esc = quit")
    return "   ·   ".join(keys)


def _terminal_keys():
    """Non-blocking single-key reads (Windows: msvcrt; macOS/Linux: termios)."""
    import os
    if os.name == "nt":
        import msvcrt

        class R:
            def __enter__(self):
                return self

            def read(self):
                out = []
                while msvcrt.kbhit():
                    out.append(msvcrt.getwch())
                return out

            def __exit__(self, *e):
                pass
        return R()
    import select
    import termios
    import tty

    class R:
        def __enter__(self):
            self.fd = sys.stdin.fileno()
            self.old = termios.tcgetattr(self.fd)
            tty.setcbreak(self.fd)
            return self

        def read(self):
            out = []
            while select.select([sys.stdin], [], [], 0)[0]:
                ch = sys.stdin.read(1)
                if not ch:
                    break
                out.append(ch)
            return out

        def __exit__(self, *e):
            termios.tcsetattr(self.fd, termios.TCSADRAIN, self.old)
    return R()


def _run_terminal(bridge, args, on_step, help_text: str) -> None:
    interactive = sys.stdin.isatty()
    print(help_text)
    last = {"t": 0.0, "prompt": ""}

    def tick(b):
        on_step(b)
        now = time.perf_counter()
        prompt = b.procedure.prompt(b.now()) if b.procedure is not None and not b.procedure.done else ""
        if not prompt and b.protocol is not None and not b.protocol.done and b.mode == "play":
            prompt = b.protocol.prompt(b.now())
        head = prompt.split("(")[0]
        if head and head != last["prompt"]:
            print(f"\n>>> {prompt}")
            last["prompt"] = head
        if now - last["t"] >= 1.0:
            print("  " + b.status_line(), flush=True)
            last["t"] = now

    if not interactive:
        bridge.run(duration=args.duration, on_step=tick)
        return
    with _terminal_keys() as reader:
        def tick_keys(b):
            for ch in reader.read():
                if ch in ("x", "\x03", "\x1b"):
                    b.stop()
                    return
                b.on_key(ch)
            tick(b)
        bridge.run(duration=args.duration, on_step=tick_keys)


if __name__ == "__main__":
    sys.exit(main())
