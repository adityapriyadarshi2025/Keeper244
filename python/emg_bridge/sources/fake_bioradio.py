"""A stand-in for BioRadioSDK.dll with the same object model, for tests and Mac rehearsals.

`BioRadioSource(cfg, sdk=FakeBioRadioSdk())` and `tools/bioradio_probe.py --fake-sdk` run the
real BioRadio code path (connect, channel mapping, units, pads, dropped packets, marker button)
on any OS. The fake device streams simulated EMG in real time: biceps and triceps take turns
flexing (1 s light, 1 s rest) unless a test drives `device.sim` directly.
"""

from __future__ import annotations

import time
from typing import Any, Callable

import numpy as np

from .simulated import SimulatedSource


class _NetArray(list):
    """Behaves like a .NET array for the bits we use (.Length, indexing, iteration)."""

    @property
    def Length(self) -> int:  # noqa: N802 (.NET name)
        return len(self)


class _Event:
    """A .NET event: handlers attach with += and detach with -=."""

    def __init__(self):
        self.handlers: list[Callable] = []

    def __iadd__(self, handler):
        self.handlers.append(handler)
        return self

    def __isub__(self, handler):
        self.handlers.remove(handler)
        return self

    def fire(self, *args):
        for h in list(self.handlers):
            h(*args)


class _DeviceInfo:
    def __init__(self, device_id: str, mac: str):
        self.DeviceId, self.MacId = device_id, mac


class _Battery:
    Voltage, Current, ChargeState = 3.95, -0.12, 87.0


class _Signal:
    def __init__(self, device: "FakeBioRadioDevice", index: int, name: str):
        self._device, self._index = device, index
        self.Name, self.Units = name, device.units
        self.MinValue, self.MaxValue = -0.0075 / device.unit_factor, 0.0075 / device.unit_factor
        self.buffer: list[float] = []

    def GetScaledValueArray(self):  # noqa: N802
        self._device._pump()
        out, self.buffer = self.buffer, []
        return _NetArray(out)


class _Group:
    def __init__(self, signals: list[_Signal], fs: float):
        self._signals, self.SamplesPerSecond = signals, fs

    @property
    def Count(self) -> int:  # noqa: N802
        return len(self._signals)

    def __getitem__(self, i: int) -> _Signal:
        return self._signals[i]


class FakeBioRadioDevice:
    def __init__(self, sdk: "FakeBioRadioSdk", mac: int):
        self.Name = sdk.device_id
        self.FirmwareVersion, self.HardwareVersion = "fake-fw", "fake-hw"
        self.mac = mac
        self.units, self.unit_factor = sdk.units, {"V": 1.0, "mV": 1e-3, "uV": 1e-6}.get(sdk.units, 1.0)
        self.pad_value = sdk.pad_value
        self.pad_every = sdk.pad_every
        sim_cfg = {"mvc_uv": 400, "noise_uv": 6, "mains_uv": 20, "crosstalk": 0.08, "chunk_ms": sdk.chunk_ms,
                   "ramp_tau_s": 0.06, "range_uv": 7500}
        self.sim = SimulatedSource(sdk.fs, sim_cfg, clock=sdk.clock, seed=7)
        self.auto_pattern = sdk.auto_pattern
        names = sdk.channel_names
        self._signals = [_Signal(self, i, n) for i, n in enumerate(names)]
        self.BioPotentialSignals = _Group(self._signals, sdk.fs)
        self.EventMarkerButtonPushed = _Event()
        self.DroppedPacketCount = 0
        self.IsAcquiring = False
        self.connected = True
        self._clock = sdk.clock
        self._t0 = 0.0
        self._count = 0

    def StartAcquisition(self):  # noqa: N802
        self.IsAcquiring = True
        self._t0 = self._clock()
        self.sim.start()

    def StopAcquisition(self):  # noqa: N802
        self.IsAcquiring = False

    def Disconnect(self):  # noqa: N802
        self.connected = False

    def GetBatteryInfo(self):  # noqa: N802
        return _Battery()

    @property
    def TotalAcquisitionTime(self) -> float:  # noqa: N802
        return self._count / self.BioPotentialSignals.SamplesPerSecond

    def SaveConfiguration(self, path: str):  # noqa: N802
        with open(path, "w", encoding="utf-8") as f:
            f.write(f'<BioRadioConfiguration Name="fake" SampleRate="{self.BioPotentialSignals.SamplesPerSecond:g}" '
                    f'Termination="Differential"/>\n')

    def press_button(self):
        self.EventMarkerButtonPushed.fire(self, None)

    def _pump(self):
        if not self.IsAcquiring:
            return
        if self.auto_pattern:
            t = self._clock() - self._t0
            phase = int(t) % 4  # 0 biceps, 1 rest, 2 triceps, 3 rest
            self.sim.set_drive(0.25 if phase == 0 else 0.0, 0.25 if phase == 2 else 0.0)
        chunk = self.sim.read()
        if not len(chunk):
            return
        x = chunk.x / self.unit_factor  # the SDK reports in its own units
        n = len(x)
        if self.pad_every:
            idx = self._count + np.arange(n)
            lost = (idx // 20) % self.pad_every == self.pad_every - 1  # whole 20-sample "packets"
            if lost.any():
                x = x.copy()
                x[lost] = self.pad_value
                self.DroppedPacketCount += int(np.count_nonzero(lost[::20])) or 1
        self._count += n
        for i, s in enumerate(self._signals):
            col = x[:, i] if i < 2 else 0.02 * x[:, i % 2]  # channels 3+ carry a little crosstalk only
            s.buffer.extend(col.tolist())


class _Manager:
    def __init__(self, sdk: "FakeBioRadioSdk"):
        self._sdk = sdk

    def DiscoverBluetoothDevices(self):  # noqa: N802
        return _NetArray([_DeviceInfo(self._sdk.device_id, self._sdk.mac)] if self._sdk.present else [])

    def GetBluetoothDevice(self, mac: int):  # noqa: N802
        if not self._sdk.present or mac != int(self._sdk.mac.replace(":", ""), 16):
            raise RuntimeError("Connection failed (fake SDK: no such device)")
        self._sdk.device = FakeBioRadioDevice(self._sdk, mac)
        return self._sdk.device


class FakeBioRadioSdk:
    """Drop-in for `BioRadioSdk` (see sources/bioradio.py)."""

    kind = "fake"

    def __init__(self, fs: float = 2000.0, channel_names: tuple[str, ...] = ("Biceps", "Triceps", "Ch3", "Ch4"),
                 units: str = "V", device_id: str = "AOP", mac: str = "EC:FE:7E:18:36:AF", present: bool = True,
                 pad_value: float = -1.0e10, pad_every: int = 0, chunk_ms: float = 20,
                 clock: Callable[[], float] = time.perf_counter, auto_pattern: bool = True):
        self.fs, self.channel_names, self.units = fs, list(channel_names), units
        self.device_id, self.mac, self.present = device_id, mac, present
        self.pad_value, self.pad_every, self.chunk_ms = pad_value, pad_every, chunk_ms
        self.clock, self.auto_pattern = clock, auto_pattern
        self.device: FakeBioRadioDevice | None = None
        sdk = self

        class DeviceManager(_Manager):
            def __init__(self):
                super().__init__(sdk)

        self.DeviceManager = DeviceManager

    def info(self) -> dict[str, Any]:
        return {"kind": self.kind, "fs": self.fs, "channels": self.channel_names, "units": self.units}
