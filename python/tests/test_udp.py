"""UDP plumbing over real localhost sockets (ephemeral ports, so tests never clash with a game)."""

import json
import socket
import time

import pytest

from emg_bridge.net import protocol as p
from emg_bridge.net.udp_in import EventReceiver
from emg_bridge.net.udp_out import EmgSender, RoundTrip


@pytest.fixture
def unity_socket():
    """Plays Unity's EMG port: a bound UDP socket that collects what the sender sends."""
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.bind(("127.0.0.1", 0))
    s.settimeout(1.0)
    yield s
    s.close()


def recv_json(sock):
    data, _ = sock.recvfrom(65535)
    return json.loads(data)


def test_emg_packets_are_numbered_valid_and_clipped(unity_socket):
    sender = EmgSender("127.0.0.1", unity_socket.getsockname()[1])
    try:
        assert sender.send_emg(0.01, 0.2, 0.0, 1, False) == 1
        assert sender.send_emg(0.02, 9.0, -1.0, 2, False, "saturated") == 2
        a, b = recv_json(unity_socket), recv_json(unity_socket)
    finally:
        sender.close()
    assert p.validate(a) and p.validate(b)
    assert (a["seq"], b["seq"]) == (1, 2)
    assert (b["ab"], b["at"]) == (1.5, 0.0)
    assert b["status"] == "saturated"
    assert "rtt_ms" not in a  # no echo yet


def test_a_forced_late_seq_does_not_advance_the_counter(unity_socket):
    sender = EmgSender("127.0.0.1", unity_socket.getsockname()[1])
    try:
        for _ in range(5):
            sender.send_emg(0.0, 0.0, 0.0, 0, True)
        assert sender.send_emg(0.0, 0.0, 0.0, 0, True, seq=3) == 3
        assert sender.send_emg(0.0, 0.0, 0.0, 0, True) == 6
    finally:
        sender.close()


def test_calib_message_is_valid(unity_socket):
    sender = EmgSender("127.0.0.1", unity_socket.getsockname()[1])
    try:
        sender.send_calib((0.1, 0.12), (0.07, 0.084), (0.35, 0.33))
        msg = recv_json(unity_socket)
    finally:
        sender.close()
    assert msg == {"type": "calib", "th_on": [0.1, 0.12], "th_off": [0.07, 0.084], "th_far": [0.35, 0.33]}


def test_rtt_rides_on_later_packets_once_unity_echoes(unity_socket):
    clock = iter([10.0, 10.1, 10.2]).__next__
    sender = EmgSender("127.0.0.1", unity_socket.getsockname()[1], clock=clock)
    try:
        seq = sender.send_emg(0.0, 0.2, 0.0, 1, False)        # sent at 10.0
        assert sender.round_trip.echoed(seq, t=10.025) == pytest.approx(0.025)
        sender.send_emg(0.01, 0.2, 0.0, 1, False)              # sent at 10.1
        recv_json(unity_socket)
        later = recv_json(unity_socket)
    finally:
        sender.close()
    assert later["rtt_ms"] == pytest.approx(25.0)


def test_round_trip_matching_and_summary():
    rt = RoundTrip(keep=3)
    for seq, t in [(1, 0.0), (2, 0.01), (3, 0.02), (4, 0.03)]:
        rt.sent(seq, t)
    assert rt.echoed(1, 0.5) is None, "evicted (keep=3)"
    assert rt.echoed(99, 0.5) is None, "never sent"
    assert rt.echoed(2, 0.03) == pytest.approx(0.02)
    assert rt.echoed(2, 0.04) is None, "each echo counts once"
    rt.echoed(4, 0.07)
    s = rt.summary()
    assert s["n"] == 2 and s["median_ms"] == pytest.approx(30.0) and s["max_ms"] == pytest.approx(40.0)
    assert RoundTrip().summary() == {}


def test_event_receiver_keeps_valid_events_and_counts_bad_ones():
    rx = EventReceiver("127.0.0.1", 0)
    tx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        good = p.event("shot_cue", shot=1, level="L1", zones=3, target=-1)
        tx.sendto(p.encode(good), ("127.0.0.1", rx.port))
        tx.sendto(b'{"type": "event", "name": "teleport"}', ("127.0.0.1", rx.port))
        tx.sendto(b"not json", ("127.0.0.1", rx.port))
        got = []
        deadline = time.time() + 1.0
        while time.time() < deadline and rx.received < 3:
            got += rx.poll()
            time.sleep(0.01)
    finally:
        tx.close()
        rx.close()
    assert [m for _, m in got] == [good]
    assert rx.invalid == 2 and rx.last_error


def test_event_receiver_reports_a_busy_port():
    first = EventReceiver("127.0.0.1", 0)
    try:
        with pytest.raises(OSError, match="cannot listen"):
            EventReceiver("127.0.0.1", first.port)
    finally:
        first.close()
