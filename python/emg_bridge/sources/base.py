"""The `EMGSource` abstraction (brief §5.2): everything above it is the same for the simulator,
a replayed recording and the real BioRadio.
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from typing import Any

import numpy as np


@dataclass
class Chunk:
    """Samples delivered by one `read()`.

    n0         absolute index of the first sample (0 = first sample of the acquisition). A jump
               (n0 > previous n0 + previous N) means samples were lost; the bridge logs a gap.
    x          (N, C) float array in **volts**. NaN = a sample the device reported missing (pad).
    t_arrival  bridge clock (s) when the chunk was read: the PC-side timestamp used to measure fs.
    dropped    cumulative dropped packets reported by the device (0 if it does not report them).
    """

    n0: int
    x: np.ndarray
    t_arrival: float
    dropped: int = 0

    def __len__(self) -> int:
        return len(self.x)

    @property
    def n_end(self) -> int:
        """Index one past the last sample."""
        return self.n0 + len(self.x)


@dataclass
class SourceInfo:
    """What the source knows about itself; written to the session's meta.json."""

    kind: str
    fs: float
    channels: list[str]
    units: str = "V"
    range_v: tuple[float, float] | None = None   # ADC range of the scaled values, for saturation
    details: dict[str, Any] = field(default_factory=dict)


class EMGSource(ABC):
    """Non-blocking sample source. Call `start()`, then `read()` as often as you like, then `stop()`."""

    info: SourceInfo

    @property
    def fs(self) -> float:
        return self.info.fs

    @property
    def channels(self) -> list[str]:
        return self.info.channels

    @abstractmethod
    def start(self) -> None: ...

    @abstractmethod
    def read(self) -> Chunk:
        """All samples that arrived since the last call (N may be 0). Never blocks for long."""

    @abstractmethod
    def stop(self) -> None: ...

    def poll_marks(self) -> list[tuple[float, str]]:
        """Device-side markers since the last call, as (bridge time, label). Default: none."""
        return []

    def __enter__(self) -> "EMGSource":
        self.start()
        return self

    def __exit__(self, *exc) -> None:
        self.stop()


def empty_chunk(n0: int, n_channels: int, t: float, dropped: int = 0) -> Chunk:
    return Chunk(n0, np.zeros((0, n_channels)), t, dropped)
