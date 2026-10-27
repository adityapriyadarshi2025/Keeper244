"""The frozen UDP contract between the Python bridge and the Unity game.

Spec: docs/PROTOCOL.md. Examples: protocol/udp_examples.json (tested on both sides).
One JSON object per UDP datagram. Receivers ignore unknown fields.
"""

from __future__ import annotations

import json
from numbers import Real
from typing import Any

PYTHON_TO_UNITY_PORT = 5005
UNITY_TO_PYTHON_PORT = 5006
SIGNAL_TIMEOUT_S = 0.250

STATUSES = ("ok", "no_data", "saturated", "flatline")
SLOT_RANGE = range(-2, 3)

# (type, name) -> {field: kind}. kind is "int", "num", "bool", "str" or "pair" (two numbers).
SCHEMA: dict[tuple[str, str | None], dict[str, str]] = {
    ("emg", None): {"seq": "int", "t": "num", "ab": "num", "at": "num", "slot": "int", "rest": "bool", "status": "str"},
    ("calib", None): {"th_on": "pair", "th_off": "pair", "th_far": "pair"},
    ("event", "level_start"): {"level": "str", "zones": "int", "shots": "int"},
    ("event", "shot_cue"): {"shot": "int", "level": "str", "zones": "int", "target": "int"},
    ("event", "kick"): {"shot": "int"},
    ("event", "arrival"): {"shot": "int", "target": "int", "keeper_slot": "int", "saved": "bool"},
    ("event", "level_end"): {"level": "str", "shots": "int", "saves": "int"},
    ("event", "shot_void"): {"shot": "int", "reason": "str"},
    ("event", "slot_applied"): {"seq": "int"},
    ("cmd", "mark"): {"label": "str"},
}


# Optional fields: checked only when present. rtt_ms = the bridge's latest round trip
# (emg sent -> slot_applied back), shown on Unity's F1 panel.
OPTIONAL: dict[tuple[str, str | None], dict[str, str]] = {
    ("emg", None): {"rtt_ms": "num"},
}


class ProtocolError(ValueError):
    """A message that does not follow docs/PROTOCOL.md."""


def _is_kind(value: Any, kind: str) -> bool:
    if kind == "bool":
        return isinstance(value, bool)
    if kind == "int":
        return isinstance(value, int) and not isinstance(value, bool)
    if kind == "num":
        return isinstance(value, Real) and not isinstance(value, bool)
    if kind == "str":
        return isinstance(value, str)
    if kind == "pair":
        return isinstance(value, list) and len(value) == 2 and all(_is_kind(v, "num") for v in value)
    raise AssertionError(kind)


def validate(msg: dict[str, Any]) -> dict[str, Any]:
    """Return msg if it matches the contract, else raise ProtocolError."""
    if not isinstance(msg, dict):
        raise ProtocolError("message must be a JSON object")
    mtype = msg.get("type")
    key = (mtype, None) if mtype in ("emg", "calib") else (mtype, msg.get("name"))
    if key not in SCHEMA:
        raise ProtocolError(f"unknown message type/name: {key}")
    for field, kind in SCHEMA[key].items():
        if field not in msg:
            raise ProtocolError(f"{key}: missing field '{field}'")
        if not _is_kind(msg[field], kind):
            raise ProtocolError(f"{key}: field '{field}' must be {kind}, got {msg[field]!r}")
    for field, kind in OPTIONAL.get(key, {}).items():
        if field in msg and not _is_kind(msg[field], kind):
            raise ProtocolError(f"{key}: optional field '{field}' must be {kind}, got {msg[field]!r}")
    if mtype == "emg":
        if msg["slot"] not in SLOT_RANGE:
            raise ProtocolError(f"emg: slot {msg['slot']} outside -2..2")
        if msg["status"] not in STATUSES:
            raise ProtocolError(f"emg: unknown status {msg['status']!r}")
    if key[1] in ("level_start", "shot_cue") and msg["zones"] not in (3, 5):
        raise ProtocolError(f"{key}: zones must be 3 or 5")
    return msg


def parse(datagram: bytes | str) -> dict[str, Any]:
    """Decode one datagram and validate it."""
    text = datagram.decode("utf-8") if isinstance(datagram, bytes) else datagram
    try:
        msg = json.loads(text)
    except json.JSONDecodeError as e:
        raise ProtocolError(f"not JSON: {e}") from e
    return validate(msg)


def encode(msg: dict[str, Any]) -> bytes:
    """Validate and encode one message as a compact UTF-8 datagram."""
    return json.dumps(validate(msg), separators=(",", ":")).encode("utf-8")


def emg(seq: int, t: float, ab: float, at: float, slot: int, rest: bool, status: str = "ok",
        rtt_ms: float | None = None) -> dict[str, Any]:
    msg = {"type": "emg", "seq": int(seq), "t": float(t), "ab": float(ab), "at": float(at),
           "slot": int(slot), "rest": bool(rest), "status": status}
    if rtt_ms is not None:
        msg["rtt_ms"] = round(float(rtt_ms), 2)
    return msg


def calib(th_on: tuple[float, float], th_off: tuple[float, float], th_far: tuple[float, float]) -> dict[str, Any]:
    """Thresholds as (biceps, triceps)."""
    return {"type": "calib", "th_on": [float(v) for v in th_on], "th_off": [float(v) for v in th_off],
            "th_far": [float(v) for v in th_far]}


def event(name: str, **fields: Any) -> dict[str, Any]:
    """A Unity -> Python event (used by tests and fake_sender to play Unity's part)."""
    return validate({"type": "event", "name": name, **fields})
