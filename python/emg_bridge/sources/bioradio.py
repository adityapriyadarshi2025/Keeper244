"""`BioRadioSource`: the real device, through the vendor's .NET SDK (brief §5.1, §5.2).

Route (from the course kit, BioRadio_Student_Unity_Kit): Windows + Bluetooth + BioRadioSDK.dll
(Great Lakes NeuroTechnologies, .NET Framework 4.5, AnyCPU) loaded into Python with `pythonnet`.
Configure the channels and sample rate in BioCapture first, then CLOSE BioCapture: only one
program can hold the Bluetooth link.

SDK calls used (names from the SDK's own XML docs, BioRadioSDK.xml):
    BioRadioDeviceManager().DiscoverBluetoothDevices() -> [.DeviceId, .MacId]
    manager.GetBluetoothDevice(int64 mac)             -> BioRadioDevice (already connected)
    device.BioPotentialSignals  (.Count, .SamplesPerSecond, [i] -> signal)
    signal.GetScaledValueArray() (drains the PC-side buffer), .Name, .Units, .MinValue, .MaxValue
    BioRadioSignal.ScaledPadValue  (the value that marks a missing sample)
    device.DroppedPacketCount, .TotalAcquisitionTime, .GetBatteryInfo(), .SaveConfiguration(path)
    device.EventMarkerButtonPushed (the BioRadio's marker button) -> a mark in events.csv
    device.StartAcquisition() / StopAcquisition() / Disconnect()

Everything that touches .NET goes through `BioRadioSdk`, so the logic here is tested on any OS
with `FakeBioRadioSdk` (sources/fake_bioradio.py).
"""

from __future__ import annotations

import queue
import sys
import time
from pathlib import Path
from typing import Any, Callable

import numpy as np

from .base import Chunk, EMGSource, SourceInfo

UNIT_SCALE = {"v": 1.0, "volt": 1.0, "volts": 1.0, "mv": 1e-3, "millivolts": 1e-3, "uv": 1e-6, "µv": 1e-6,
              "μv": 1e-6, "microvolts": 1e-6}


class BioRadioError(RuntimeError):
    """Something the operator must fix (no Windows, no DLL, no device, wrong channels)."""


def normalise_mac(mac: Any) -> str:
    return str(mac).replace(":", "").replace("-", "").replace(" ", "").upper()


def to_numpy(arr: Any) -> np.ndarray:
    """A .NET double[] (or any sequence) -> float64 numpy array."""
    if arr is None:
        return np.zeros(0)
    count = getattr(arr, "Length", None)
    if count is None:
        count = len(arr)
    count = int(count)
    if count == 0:
        return np.zeros(0)
    try:
        return np.fromiter(arr, dtype=float, count=count)
    except TypeError:
        return np.array([float(v) for v in arr], dtype=float)


def unit_scale(units: str) -> tuple[float, bool]:
    """(factor to volts, known?) for the SDK's Units string. Unknown units are assumed to be volts."""
    key = str(units).strip().lower()
    if key in UNIT_SCALE:
        return UNIT_SCALE[key], True
    return 1.0, False


class BioRadioSdk:
    """The .NET entry points, loaded through pythonnet (Windows only)."""

    kind = "dotnet"

    def __init__(self, device_manager_cls: Any, pad_value: float | None):
        self.DeviceManager = device_manager_cls
        self.pad_value = pad_value

    @classmethod
    def load(cls, dll_path: str | Path) -> "BioRadioSdk":
        if sys.platform != "win32":
            raise BioRadioError("the BioRadio SDK is a Windows .NET library: run this on the Windows laptop "
                                "(docs/SETUP.md, BioRadio section). On a Mac use --source sim or --fake-sdk.")
        dll = Path(dll_path).resolve()
        if not dll.is_file():
            raise BioRadioError(f"BioRadioSDK.dll not found at {dll}. Copy it from the course kit "
                                "(BioRadio_Student_Unity_Kit/Python_Bridge/BioRadioSDK.dll) into python/vendor/. "
                                "It is not in git: keep the SDK within IITGN.")
        try:
            from pythonnet import load as load_runtime
            try:
                load_runtime("netfx")  # .NET Framework: the SDK targets 4.5
            except RuntimeError:
                pass  # a runtime is already loaded
            import clr  # noqa: F401  (pythonnet)
        except ImportError as e:
            raise BioRadioError("pythonnet is missing: pip install pythonnet (Windows only)") from e
        import clr
        if str(dll.parent) not in sys.path:
            sys.path.append(str(dll.parent))
        try:
            clr.AddReference(str(dll))
        except Exception:
            try:
                clr.AddReference(dll.stem)
            except Exception as e:
                raise BioRadioError(f"could not load {dll.name} ({e}). If Windows blocked the file: right-click it -> "
                                    "Properties -> Unblock. The SDK also needs .NET Framework 4.5.1 or newer.") from e
        from GLNeuroTech.Devices.BioRadio import BioRadioDeviceManager  # type: ignore
        pad = None
        try:
            from GLNeuroTech.Devices.BioRadio import BioRadioSignal  # type: ignore
            pad = float(BioRadioSignal.ScaledPadValue)
        except Exception:
            pass
        return cls(BioRadioDeviceManager, pad)


class BioRadioSource(EMGSource):
    """Two biopotential channels (biceps, triceps) from a BioRadio, in volts."""

    def __init__(self, cfg: dict[str, Any], channels: list[str] | None = None, sdk: Any = None,
                 clock: Callable[[], float] = time.perf_counter, mac: str | None = None,
                 python_root: Path | None = None, log: Callable[[str], None] = print):
        self.cfg = cfg
        self.names = list(channels or ["biceps", "triceps"])
        self.clock = clock
        self.log = log
        root = python_root or Path(__file__).resolve().parents[2]
        dll = Path(cfg.get("sdk_dll", "vendor/BioRadioSDK.dll"))
        self.dll_path = dll if dll.is_absolute() else root / dll
        self._sdk = sdk
        self.mac = normalise_mac(mac or cfg.get("mac") or "") or None
        self.manager = self.device = None
        self.signals: list[Any] = []
        self.scale = 1.0
        self.pad_value: float | None = None
        self._pending: list[np.ndarray] = []
        self._marks: "queue.Queue[tuple[float, str]]" = queue.Queue()
        self.n = 0
        self.pads = 0
        self.acquiring = False
        self.info = SourceInfo("bioradio", 0.0, self.names)

    # --- connection --------------------------------------------------------------------------
    @property
    def sdk(self):
        if self._sdk is None:
            self._sdk = BioRadioSdk.load(self.dll_path)
        return self._sdk

    def discover(self) -> list[tuple[str, str]]:
        """[(device id, MAC)] of the BioRadios in range (takes a few seconds)."""
        if self.manager is None:
            self.manager = self.sdk.DeviceManager()
        found = self.manager.DiscoverBluetoothDevices()
        n = int(getattr(found, "Length", len(found)))
        out = []
        for i in range(n):
            d = found[i]
            out.append((str(d.DeviceId), normalise_mac(d.MacId)))
        return out

    def connect(self) -> SourceInfo:
        if self.device is not None:
            return self.info
        sdk = self.sdk
        if self.manager is None:
            self.manager = sdk.DeviceManager()
        mac = self.mac
        if not mac:
            self.log("bioradio: scanning for devices (no mac in config.yaml)...")
            found = self.discover()
            if not found:
                raise BioRadioError("no BioRadio found: power it on, check Bluetooth, keep it close, close BioCapture, try again")
            name, mac = found[0]
            self.log(f"bioradio: found {', '.join(f'{d} ({m})' for d, m in found)}; using {name} ({mac}). "
                     "Put its mac in config.yaml bioradio.mac to skip the scan.")
        try:
            self.device = self.manager.GetBluetoothDevice(int(mac, 16))
        except Exception as e:
            raise BioRadioError(f"could not connect to BioRadio {mac}: {e}. Is BioCapture still open? Is it paired in Windows?") from e
        self.mac = mac
        group = self.device.BioPotentialSignals
        count = int(group.Count)
        all_signals = [group[i] for i in range(count)]
        all_names = [str(s.Name) for s in all_signals]
        if count < len(self.names):
            raise BioRadioError(f"the BioRadio has {count} enabled biopotential channel(s) ({all_names}); "
                                f"we need {len(self.names)}. Enable them in BioCapture, then close it.")
        fs = float(group.SamplesPerSecond)
        if fs <= 0:
            raise BioRadioError(f"invalid sample rate from the SDK: {fs}")
        picks = [self._resolve_channel(self.cfg.get("channels", {}).get(m, i + 1), all_names, m)
                 for i, m in enumerate(self.names)]
        if len(set(picks)) != len(picks):
            raise BioRadioError(f"biceps and triceps map to the same channel ({picks}); fix config.yaml bioradio.channels")
        self.signals = [all_signals[i] for i in picks]
        units = str(self.signals[0].Units)
        self.scale, known = unit_scale(units)
        if not known:
            self.log(f"bioradio: unknown units '{units}', assuming volts. Check against BioCapture.")
        self.pad_value = self.sdk.pad_value
        lo = min(float(s.MinValue) for s in self.signals) * self.scale
        hi = max(float(s.MaxValue) for s in self.signals) * self.scale
        details = {"device": self._safe(lambda: str(self.device.Name)), "mac": mac,
                   "firmware": self._safe(lambda: str(self.device.FirmwareVersion)),
                   "hardware": self._safe(lambda: str(self.device.HardwareVersion)),
                   "enabled_channels": all_names, "picked": {m: all_names[i] for m, i in zip(self.names, picks)},
                   "units_reported": units, "units_known": known, "pad_value": self.pad_value,
                   "battery": self.battery(), "sdk": getattr(self.sdk, "kind", "?")}
        self.info = SourceInfo("bioradio", fs, self.names, "V", (lo, hi) if hi > lo else None, details)
        self._pending = [np.zeros(0) for _ in self.signals]
        try:
            self.device.EventMarkerButtonPushed += self._on_button
        except Exception:
            pass
        return self.info

    @staticmethod
    def _resolve_channel(spec: Any, names: list[str], muscle: str) -> int:
        if isinstance(spec, int) or (isinstance(spec, str) and spec.isdigit()):
            i = int(spec) - 1
            if not 0 <= i < len(names):
                raise BioRadioError(f"{muscle}: channel {spec} does not exist; enabled channels are {names}")
            return i
        for i, n in enumerate(names):
            if n.strip().lower() == str(spec).strip().lower():
                return i
        raise BioRadioError(f"{muscle}: no enabled channel named '{spec}'; enabled channels are {names}")

    @staticmethod
    def _safe(fn: Callable[[], Any]) -> Any:
        try:
            return fn()
        except Exception:
            return None

    def battery(self) -> dict[str, Any] | None:
        if self.device is None:
            return None
        try:
            b = self.device.GetBatteryInfo()
            return {"voltage": float(b.Voltage), "current": float(b.Current), "charge_state": float(b.ChargeState)}
        except Exception:
            return None

    def _on_button(self, *_args) -> None:
        self._marks.put((self.clock(), "bioradio_button"))

    def save_device_config(self, path: str | Path) -> bool:
        """The device's own configuration as XML (into the session folder, for reproducibility)."""
        try:
            self.device.SaveConfiguration(str(path))
            return True
        except Exception as e:
            self.log(f"bioradio: could not save the device configuration ({e})")
            return False

    # --- EMGSource ---------------------------------------------------------------------------
    def start(self) -> None:
        self.connect()
        self.device.StartAcquisition()
        self.acquiring = True

    def stop(self) -> None:
        if self.device is None:
            return
        if self.acquiring:
            try:
                self.device.StopAcquisition()
            except Exception as e:
                self.log(f"bioradio: stop: {e}")
        try:
            self.device.Disconnect()
        except Exception as e:
            self.log(f"bioradio: disconnect: {e}")
        self.acquiring = False
        self.device = None

    def read(self) -> Chunk:
        now = self.clock()
        for i, sig in enumerate(self.signals):
            vals = to_numpy(sig.GetScaledValueArray())
            if len(vals):
                if self.pad_value is not None:
                    pad = vals == self.pad_value if np.isfinite(self.pad_value) else ~np.isfinite(vals)
                    if pad.any():
                        vals = vals.copy()
                        vals[pad] = np.nan
                        if i == 0:
                            self.pads += int(pad.sum())
                self._pending[i] = np.concatenate([self._pending[i], vals * self.scale])
        m = min(len(p) for p in self._pending) if self._pending else 0
        if m == 0:
            return Chunk(self.n, np.zeros((0, len(self.signals))), now, self.dropped)
        x = np.column_stack([p[:m] for p in self._pending])
        self._pending = [p[m:] for p in self._pending]
        n0 = self.n
        self.n += m
        return Chunk(n0, x, now, self.dropped)

    @property
    def dropped(self) -> int:
        try:
            return int(self.device.DroppedPacketCount) if self.device is not None else 0
        except Exception:
            return 0

    @property
    def device_time(self) -> float | None:
        """TotalAcquisitionTime: derived from the data received, not the PC clock (SDK docs)."""
        try:
            t = self.device.TotalAcquisitionTime
            return float(t.TotalSeconds) if hasattr(t, "TotalSeconds") else float(t)
        except Exception:
            return None

    def poll_marks(self) -> list[tuple[float, str]]:
        out = []
        while True:
            try:
                out.append(self._marks.get_nowait())
            except queue.Empty:
                return out
