"""The UDP contract (docs/PROTOCOL.md) checked against the shared examples file.

Unity's EditMode ProtocolTests parse the same file, so both sides agree on every field.
"""

import json
from pathlib import Path

import pytest

from emg_bridge.net import protocol as p

EXAMPLES = Path(__file__).resolve().parents[2] / "protocol" / "udp_examples.json"


def load_examples():
    data = json.loads(EXAMPLES.read_text(encoding="utf-8"))
    return data["python_to_unity"] + data["unity_to_python"]


@pytest.mark.parametrize("msg", load_examples(), ids=lambda m: f"{m['type']}:{m.get('name', '')}")
def test_every_example_is_valid(msg):
    assert p.validate(msg) == msg
    assert p.parse(p.encode(msg)) == msg


def test_every_schema_entry_has_an_example():
    seen = {(m["type"], None if m["type"] in ("emg", "calib") else m["name"]) for m in load_examples()}
    assert seen == set(p.SCHEMA)


def test_builders_match_examples():
    data = json.loads(EXAMPLES.read_text(encoding="utf-8"))
    assert p.emg(10234, 512.34, 0.286, 0.041, 1, False, "ok") == data["python_to_unity"][0]
    assert p.calib((0.10, 0.12), (0.07, 0.084), (0.35, 0.33)) == data["python_to_unity"][3]


def test_ports_match_config():
    from emg_bridge.config import load_config

    net = load_config()["network"]
    assert (net["out_port"], net["in_port"]) == (p.PYTHON_TO_UNITY_PORT, p.UNITY_TO_PYTHON_PORT)
    assert net["signal_timeout_s"] == p.SIGNAL_TIMEOUT_S


@pytest.mark.parametrize(
    "bad",
    [
        {"type": "emg", "seq": 1, "t": 0.0, "ab": 0.1, "at": 0.1, "slot": 3, "rest": False, "status": "ok"},
        {"type": "emg", "seq": 1, "t": 0.0, "ab": 0.1, "at": 0.1, "slot": 0, "rest": 1, "status": "ok"},
        {"type": "emg", "seq": 1, "t": 0.0, "ab": 0.1, "at": 0.1, "slot": 0, "rest": True, "status": "weird"},
        {"type": "emg", "seq": True, "t": 0.0, "ab": 0.1, "at": 0.1, "slot": 0, "rest": True, "status": "ok"},
        {"type": "emg", "seq": 1, "t": 0.0, "ab": 0.1, "slot": 0, "rest": True, "status": "ok"},
        {"type": "calib", "th_on": [0.1], "th_off": [0.07, 0.08], "th_far": [0.3, 0.3]},
        {"type": "event", "name": "shot_cue", "shot": 1, "level": "L1", "zones": 4, "target": 0},
        {"type": "event", "name": "teleport"},
        {"name": "kick", "shot": 1},
    ],
)
def test_invalid_messages_are_rejected(bad):
    with pytest.raises(p.ProtocolError):
        p.validate(bad)


def test_parse_rejects_non_json():
    with pytest.raises(p.ProtocolError):
        p.parse(b"not json")


def test_optional_rtt_ms_is_checked_only_when_present():
    base = p.emg(1, 0.0, 0.1, 0.0, 1, False)
    assert "rtt_ms" not in base
    assert p.validate(p.emg(1, 0.0, 0.1, 0.0, 1, False, rtt_ms=21.234))["rtt_ms"] == 21.23
    with pytest.raises(p.ProtocolError, match="rtt_ms"):
        p.validate({**base, "rtt_ms": "fast"})


def test_event_builder_validates():
    assert p.event("shot_void", shot=3, reason="no_signal") == {"type": "event", "name": "shot_void", "shot": 3, "reason": "no_signal"}
    with pytest.raises(p.ProtocolError):
        p.event("shot_void", shot=3)
