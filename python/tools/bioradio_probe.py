#!/usr/bin/env python3
"""BioRadio smoke test for the Windows laptop: does the SDK load, does the device stream, and how
good is the link? Run it BEFORE the first real session (docs/SETUP.md, BioRadio section).

    python tools/bioradio_probe.py                 # scan, connect to the first BioRadio, record 30 s
    python tools/bioradio_probe.py --scan-only     # just list the BioRadios in range
    python tools/bioradio_probe.py --mac ECFE7E1836AF --seconds 60
    python tools/bioradio_probe.py --fake-sdk      # rehearse on any OS with the built-in fake device

While it records, flex the biceps, then the triceps: the live RMS numbers should jump on the
right channel. At the end it prints a report and saves everything to data/probe/<time>/:
    info.json (device, channels, units, fs, battery), raw.csv, chunks.csv, device_config.xml, report.txt

Before running: configure the channels in BioCapture (2 biopotential channels, 2000 Hz), then
CLOSE BioCapture; copy BioRadioSDK.dll into python/vendor/; pip install pythonnet.
"""

from __future__ import annotations

import argparse
import json
import platform
import sys
import time
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))  # so "python tools/bioradio_probe.py" finds emg_bridge

from emg_bridge.config import load_config  # noqa: E402
from emg_bridge.dsp.features import psd_welch  # noqa: E402
from emg_bridge.dsp.filters import StreamingSOS, design_chain  # noqa: E402
from emg_bridge.io.recorder import measured_fs  # noqa: E402
from emg_bridge.sources.bioradio import BioRadioError, BioRadioSource  # noqa: E402

PYTHON_ROOT = Path(__file__).resolve().parents[1]


def parse_args(argv=None) -> argparse.Namespace:
    a = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    a.add_argument("--seconds", type=float, default=30.0)
    a.add_argument("--mac", help="device MAC, e.g. ECFE7E1836AF (default: config.yaml, else the first one found)")
    a.add_argument("--scan-only", action="store_true")
    a.add_argument("--fake-sdk", action="store_true", help="use the built-in fake BioRadio (any OS)")
    a.add_argument("--out", help="output folder (default: data/probe/<time>)")
    return a.parse_args(argv)


def report(x: np.ndarray, arrivals: np.ndarray, n_ends: np.ndarray, sizes: np.ndarray, info, src, cfg, wall_s: float,
           out=print) -> tuple[list[str], dict]:
    fs = info.fs
    lines, checks = [], {}
    n = len(x)
    fs_meas = measured_fs(arrivals, n_ends)
    lines.append(f"samples per channel: {n}  in {wall_s:.1f} s on the PC clock")
    if fs_meas:
        err = (fs_meas - fs) / fs * 100
        lines.append(f"sample rate: nominal {fs:g} Hz, measured {fs_meas:.2f} Hz ({err:+.3f} %)")
        checks["fs within 0.5 % of nominal"] = abs(err) < 0.5
    dev_t = src.device_time
    if dev_t:
        lines.append(f"device clock (TotalAcquisitionTime): {dev_t:.2f} s vs PC {wall_s:.2f} s")
    dropped, pads = src.dropped, src.pads
    lines.append(f"dropped packets (SDK): {dropped}   missing samples (pads): {pads} ({pads / max(n, 1) * 100:.2f} %)")
    checks["no dropped packets"] = dropped == 0 and pads == 0
    if len(arrivals) > 2:
        gaps = np.diff(arrivals) * 1000
        nonempty = sizes > 0
        lines.append(f"deliveries: {int(nonempty.sum())} non-empty reads; samples per delivery median "
                     f"{np.median(sizes[nonempty]):.0f}")
        lines.append(f"time between deliveries: median {np.median(gaps):.1f} ms, p95 {np.percentile(gaps, 95):.1f} ms, "
                     f"max {gaps.max():.1f} ms   (Bluetooth burstiness: part of the latency budget, brief §4.6)")
        checks["p95 delivery gap < 100 ms"] = np.percentile(gaps, 95) < 100
    good = np.isfinite(x).all(axis=1)
    xg = x[good]
    if len(xg) > fs:
        y = StreamingSOS(design_chain(fs, cfg["filters"]), x.shape[1]).process(xg)[int(fs * 0.5):]
        for i, name in enumerate(info.channels):
            raw_rms = np.sqrt(np.mean((xg[:, i] - xg[:, i].mean()) ** 2)) * 1e6
            filt_rms = np.sqrt(np.mean(y[:, i] ** 2)) * 1e6
            f, pxx = psd_welch(xg[:, i] - xg[:, i].mean(), fs)
            band = (f >= 20) & (f <= 450)
            mains = (f >= 49) & (f <= 51)
            mains_pct = pxx[mains].sum() / max(pxx[band].sum(), 1e-30) * 100
            peak = np.abs(xg[:, i]).max()
            lines.append(f"{name:<8} DC {xg[:, i].mean() * 1e6:9.1f} µV   raw RMS {raw_rms:8.1f} µV   "
                         f"filtered RMS {filt_rms:7.1f} µV   50 Hz share {mains_pct:5.1f} %   peak {peak * 1e6:8.1f} µV")
            checks[f"{name}: signal present (filtered RMS > 1 µV)"] = filt_rms > 1.0
            if info.range_v:
                checks[f"{name}: not saturated"] = peak < 0.99 * max(abs(info.range_v[0]), abs(info.range_v[1]))
    lines.append("")
    for name, ok in checks.items():
        lines.append(f"  [{'ok' if ok else '!!'}] {name}")
    return lines, checks


def main(argv=None) -> int:
    args = parse_args(argv)
    cfg = load_config()
    out_dir = Path(args.out) if args.out else PYTHON_ROOT.parent / "data" / "probe" / time.strftime("%Y%m%d_%H%M%S")
    print(f"Python {platform.python_version()} on {platform.platform()}")
    try:
        import pythonnet  # noqa: F401
        from importlib.metadata import version
        print(f"pythonnet {version('pythonnet')}")
    except Exception:
        if not args.fake_sdk:
            print("pythonnet: not installed (pip install pythonnet)")
    sdk = None
    if args.fake_sdk:
        from emg_bridge.sources.fake_bioradio import FakeBioRadioSdk
        sdk = FakeBioRadioSdk(fs=float(cfg["acquisition"]["fs_nominal_hz"]))
        print("using the FAKE BioRadio SDK (rehearsal)")
    src = BioRadioSource(cfg["bioradio"], list(cfg["acquisition"]["channels"]), sdk=sdk, mac=args.mac,
                         python_root=PYTHON_ROOT)
    try:
        sdk_obj = src.sdk
        print(f"SDK loaded ({getattr(sdk_obj, 'kind', '?')}); pad value: {sdk_obj.pad_value}")
        if args.scan_only:
            found = src.discover()
            print("\n".join(f"  {d}  {m}" for d, m in found) or "  none found")
            return 0 if found else 1
        info = src.connect()
    except BioRadioError as e:
        print(f"\nPROBE FAILED: {e}")
        return 2
    d = info.details
    print(f"\ndevice {d.get('device')}  mac {d.get('mac')}  firmware {d.get('firmware')}  hardware {d.get('hardware')}")
    print(f"battery: {d.get('battery')}")
    print(f"enabled biopotential channels: {d.get('enabled_channels')}  ->  using {d.get('picked')}")
    print(f"sample rate {info.fs:g} Hz   units reported '{d.get('units_reported')}'"
          + ("" if d.get("units_known") else "  (UNKNOWN: assumed volts; check against BioCapture)")
          + f"   range {tuple(round(v * 1e3, 3) for v in info.range_v) if info.range_v else '?'} mV")
    out_dir.mkdir(parents=True, exist_ok=True)
    src.save_device_config(out_dir / "device_config.xml")

    print(f"\nrecording {args.seconds:g} s: flex the BICEPS, relax, flex the TRICEPS, relax...\n")
    src.start()
    t0 = time.perf_counter()
    xs, arrivals, n_ends, sizes = [], [], [], []
    last_print = t0
    try:
        while time.perf_counter() - t0 < args.seconds:
            c = src.read()
            arrivals.append(c.t_arrival - t0)
            n_ends.append(c.n_end)
            sizes.append(len(c))
            if len(c):
                xs.append(c.x)
            now = time.perf_counter()
            if now - last_print >= 1.0 and xs:
                recent = np.vstack(xs[-50:])
                recent = recent[np.isfinite(recent).all(axis=1)]
                rms = np.sqrt(np.mean((recent - recent.mean(0)) ** 2, axis=0)) * 1e6 if len(recent) else [np.nan] * 2
                print(f"  t {now - t0:5.1f} s   samples {c.n_end:7d}   dropped {src.dropped:3d}   "
                      f"RMS biceps {rms[0]:8.1f} µV   triceps {rms[1]:8.1f} µV")
                last_print = now
            time.sleep(0.005)
    except KeyboardInterrupt:
        print("  (stopped early)")
    wall = time.perf_counter() - t0
    for t, label in src.poll_marks():
        print(f"  marker button pressed at {t - t0:.2f} s")
    src.stop()

    x = np.vstack(xs) if xs else np.zeros((0, 2))
    arrivals, n_ends, sizes = np.array(arrivals), np.array(n_ends), np.array(sizes)
    np.savetxt(out_dir / "raw.csv", np.column_stack([np.arange(len(x)), x / 1e-6]), delimiter=",",
               header="n,biceps_uv,triceps_uv", comments="", fmt=["%d", "%.3f", "%.3f"])
    np.savetxt(out_dir / "chunks.csv", np.column_stack([arrivals, n_ends, sizes]), delimiter=",",
               header="t_arrival,n_end,n", comments="", fmt=["%.5f", "%d", "%d"])
    lines, checks = report(x, arrivals[sizes > 0], n_ends[sizes > 0], sizes, info, src, cfg, wall)
    text = "\n".join(lines)
    print("\n" + text)
    (out_dir / "report.txt").write_text(text + "\n", encoding="utf-8")
    (out_dir / "info.json").write_text(json.dumps({"fs": info.fs, "channels": info.channels, "range_v": info.range_v,
                                                   "details": info.details, "checks": checks}, indent=2, default=str) + "\n",
                                       encoding="utf-8")
    print(f"\nsaved {out_dir}")
    return 0 if checks and all(checks.values()) else 1


if __name__ == "__main__":
    sys.exit(main())
