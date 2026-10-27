"""tools/fake_sender.py: the fake keeper logic, and a short end-to-end run against a mock game."""

import json
import socket
import threading
import time

import pytest

from emg_bridge.net import protocol as p
from tools import fake_sender as fs


def test_activations_follow_the_slot_like_an_envelope():
    env = fs.Envelope(noise=0.0)
    for _ in range(100):
        ab, at, rest = env.step(2, 0.01)
    assert ab == pytest.approx(fs.STRONG_LEVEL, abs=1e-3)
    assert at == pytest.approx(fs.REST_LEVEL, abs=1e-3)
    assert not rest
    for _ in range(100):
        ab, at, rest = env.step(0, 0.01)
    assert rest
    assert fs.target_activations(-1) == (fs.REST_LEVEL, fs.LIGHT_LEVEL)


def test_auto_keeper_moves_after_the_reaction_time_and_relaxes_after_arrival():
    k = fs.AutoKeeper(reaction=0.3, relax_after=0.2)
    k.on_event(p.event("shot_cue", shot=1, level="L2", zones=5, target=-2), now=10.0)
    assert k.slot(10.29) == 0
    assert k.slot(10.31) == -2
    k.on_event(p.event("arrival", shot=1, target=-2, keeper_slot=-2, saved=True), now=12.0)
    assert k.slot(12.1) == -2
    assert k.slot(12.21) == 0


def test_auto_keeper_relaxes_at_once_on_a_voided_shot():
    k = fs.AutoKeeper(reaction=0.0)
    k.on_event(p.event("shot_cue", shot=1, level="L1", zones=3, target=1), now=0.0)
    assert k.slot(0.01) == 1
    k.on_event(p.event("shot_void", shot=1, reason="no_signal"), now=0.5)
    assert k.slot(0.5) == 0


def test_auto_keeper_misses_on_purpose():
    k = fs.AutoKeeper(reaction=0.0, miss=1.0)
    for target in (-1, 0, 1):
        k.on_event(p.event("shot_cue", shot=1, level="L1", zones=3, target=target), now=0.0)
        slot = k.slot(0.0)
        assert slot != target and -1 <= slot <= 1


def free_port():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


def test_end_to_end_against_a_mock_game():
    """fake_sender (auto) <-> a mock 'Unity': packets at ~100 Hz, the keeper answers a shot_cue,
    slot_applied echoes give a round trip, a stale packet is flagged by its seq."""
    game = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    game.bind(("127.0.0.1", 0))
    game.settimeout(0.05)
    events_port = free_port()
    args = fs.parse_args(["--mode", "auto", "--out-port", str(game.getsockname()[1]), "--in-port", str(events_port),
                          "--duration", "1.2", "--reaction", "0.2", "--shuffle-every", "50", "--quiet"])
    result = {}
    worker = threading.Thread(target=lambda: result.update(summary=fs.run(args, out=lambda *_: None)))
    worker.start()

    packets, seqs, cue_sent, slots_after_cue = [], [], None, []
    last_slot = 0
    t0 = time.perf_counter()
    try:
        while worker.is_alive():
            try:
                data, _ = game.recvfrom(65535)
            except socket.timeout:
                continue
            msg = p.parse(data)
            if msg["type"] != "emg":
                continue
            packets.append(msg)
            seqs.append(msg["seq"])
            now = time.perf_counter() - t0
            if cue_sent is None and now > 0.2:
                game.sendto(p.encode(p.event("level_start", level="L1", zones=3, shots=3)), ("127.0.0.1", events_port))
                game.sendto(p.encode(p.event("shot_cue", shot=1, level="L1", zones=3, target=1)), ("127.0.0.1", events_port))
                cue_sent = now
            if msg["slot"] != last_slot:  # what Unity does when the keeper moves
                game.sendto(p.encode(p.event("slot_applied", seq=msg["seq"])), ("127.0.0.1", events_port))
                last_slot = msg["slot"]
            if cue_sent is not None:
                slots_after_cue.append((now - cue_sent, msg["slot"]))
    finally:
        worker.join(5)
        game.close()

    s = result["summary"]
    assert 80 <= len([m for m in packets]) - s.stale_packets <= 140, "about 100 packets in ~1.2 s"
    assert s.stale_packets >= 1 and any(b <= a for a, b in zip(seqs, seqs[1:])), "a stale seq was sent"
    assert any(slot == 1 for dt, slot in slots_after_cue if dt > 0.3), "keeper went to the target"
    assert all(slot == 0 for dt, slot in slots_after_cue if dt < 0.1), "not before the reaction time"
    assert s.events["shot_cue"] == 1 and s.events["slot_applied"] >= 1
    assert s.invalid_events == 0
    assert s.round_trip and s.round_trip["n"] >= 1 and s.round_trip["median_ms"] < 200
