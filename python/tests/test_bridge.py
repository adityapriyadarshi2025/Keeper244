"""The bridge loop: statuses, gaps, calibration -> play, logging; and end to end over UDP against a
stand-in for the Unity game (brief §5.6 "End to end")."""

from __future__ import annotations

import csv
import json
import socket
import threading
import time

import numpy as np
import pytest

from emg_bridge.bridge import Bridge
from emg_bridge.config import load_config
from emg_bridge.control.calibration import Calibration, ChannelCalibration, Procedure, build_steps
from emg_bridge.io.recorder import Recorder
from emg_bridge.net import protocol as p
from emg_bridge.net.udp_in import EventReceiver
from emg_bridge.net.udp_out import EmgSender
from emg_bridge.sources.bioradio import BioRadioSource
from emg_bridge.sources.fake_bioradio import FakeBioRadioSdk
from emg_bridge.sources.simulated import AutoPlayer, KeysPlayer, SimulatedSource

CFG = load_config()
FS = 2000.0


class FakeClock:
    def __init__(self):
        self.t = 100.0

    def __call__(self):
        return self.t


def sim_calibration() -> Calibration:
    """What calibrating the default simulator gives (rest ≈ 4 µV, MVC ≈ 400 µV)."""
    def ch(m):
        return ChannelCalibration(m, 4e-6, 0.3e-6, 400e-6, 0.3 / 396, 0.10, 0.07, 0.365, 0.2, 0.52, True, True)
    return Calibration(ch("biceps"), ch("triceps"))


def offline_bridge(sim_cfg=None, mode="play", cal=None, tmp_path=None, **kw):
    clock = FakeClock()
    src = SimulatedSource(FS, {**CFG["simulator"], **(sim_cfg or {})}, clock=clock, seed=3)
    rec = Recorder(tmp_path, "test", {"source": {"fs": FS}}) if tmp_path is not None else None
    b = Bridge(CFG, src, mode=mode, calibration=cal if cal is not None else (sim_calibration() if mode == "play" else None),
               recorder=rec, clock=clock, log=lambda *_: None, **kw)
    b.start()
    return b, src, clock


def advance(b, clock, seconds, dt=0.005):
    frames = []
    for _ in range(int(round(seconds / dt))):
        clock.t += dt
        frames += b.step()
    return frames


def test_frames_at_100_hz_and_slots_follow_the_drive():
    b, src, clock = offline_bridge()
    frames = advance(b, clock, 1.0)
    assert 85 <= len(frames) <= 101 and all(f.status == "ok" for f in frames)
    assert frames[-1].slot == 0 and frames[-1].rest
    src.set_drive(0.55, 0.0)
    frames = advance(b, clock, 0.6)
    assert frames[-1].slot == 2 and not frames[-1].rest
    src.set_drive(0.0, 0.22)
    frames = advance(b, clock, 0.6)
    assert frames[-1].slot == -1


def test_saturation_status():
    b, src, clock = offline_bridge({"range_uv": 300})
    src.set_drive(2.0, 0.0)
    frames = advance(b, clock, 0.6)
    assert frames[-1].status == "saturated"


def test_flatline_status_when_leads_are_off():
    b, src, clock = offline_bridge({"noise_uv": 0.0, "mains_uv": 0.0, "mvc_uv": 0.0})
    frames = advance(b, clock, 1.5)
    assert frames[10].status == "ok" and frames[-1].status == "flatline"


def test_gap_is_logged_and_filters_restart(tmp_path):
    b, src, clock = offline_bridge(tmp_path=tmp_path)
    advance(b, clock, 0.5)
    src.dropout(0.4)
    advance(b, clock, 1.0)
    assert b.gaps == 1 and 700 <= b.lost_samples <= 900
    assert b.source_silent is False  # back again
    b.close()
    ev = list(csv.DictReader(open(b.recorder.dir / "events.csv")))
    names = [(e["type"], e["name"]) for e in ev]
    assert ("status", "source_silent") in names and ("gap", "samples") in names and ("status", "signal_back") in names


def test_missing_samples_give_no_data_and_hold_the_slot():
    clock = FakeClock()
    sdk = FakeBioRadioSdk(clock=clock, pad_every=4, auto_pattern=False)
    src = BioRadioSource(CFG["bioradio"], sdk=sdk, clock=clock, log=lambda *_: None)
    b = Bridge(CFG, src, mode="play", calibration=sim_calibration(), clock=clock, log=lambda *_: None)
    b.start()
    sdk.device.sim.set_drive(0.55, 0.0)
    frames = advance(b, clock, 1.5)
    statuses = {f.status for f in frames}
    assert "no_data" in statuses and "ok" in statuses
    assert all(np.isfinite(f.e).all() for f in frames)  # NaNs never reach the filters
    late = [f for f in frames[-50:]]
    assert all(f.slot == 2 for f in late)               # held through the missing windows


def test_calibrate_mode_with_the_auto_player_then_play(tmp_path):
    clock = FakeClock()
    src = SimulatedSource(FS, CFG["simulator"], clock=clock, seed=9)
    player = AutoPlayer(src, CFG["simulator"], clock=clock)
    proc = Procedure(build_steps(CFG["calibration"], quick=True), CFG["calibration"], 100.0,
                     on_step=lambda s, i: player.on_calibration_step(s))
    rec = Recorder(tmp_path, "cal", {"source": {"fs": FS}})
    b = Bridge(CFG, src, mode="calibrate", procedure=proc, player=player, recorder=rec, clock=clock, log=lambda *_: None)
    b.start()
    for _ in range(60 * 200):
        clock.t += 0.005
        b.step()
        if b.calibration_done:
            break
    assert b.calibration is not None, proc.error
    c = b.calibration
    assert c.biceps.e_mvc == pytest.approx(400e-6, rel=0.1) and c.checks_passed
    assert (rec.dir / "calibration.json").exists()
    b.enter_play()
    src.set_drive(0.22, 0.0)
    frames = advance(b, clock, 0.6)
    assert frames[-1].slot == 1
    stats = b.close()
    meta = json.loads((rec.dir / "meta.json").read_text())
    assert meta["fs_measured_hz"] == pytest.approx(2000, rel=0.01) and stats["frames"] > 0
    raw = np.genfromtxt(rec.dir / "raw.csv", delimiter=",", names=True)
    assert np.all(np.diff(raw["n"]) == 1) and len(raw) == rec.samples
    env = list(csv.DictReader(open(rec.dir / "envelope.csv")))
    assert env[-1]["slot"] == "1" and env[0]["a_b"] == ""   # no activations before calibration


def test_record_mode_trials_become_marks(tmp_path):
    b, src, clock = offline_bridge(mode="record", tmp_path=tmp_path, trials=["rest_30s", "mvc_biceps"])
    b.on_key("\n")
    advance(b, clock, 0.2)
    b.on_key("\n")
    b.on_key("k")
    advance(b, clock, 0.1)
    b.close()
    marks = [e["name"] for e in csv.DictReader(open(b.recorder.dir / "events.csv")) if e["type"] == "mark"]
    assert marks == ["start:rest_30s", "stop:rest_30s", "operator_mark"]


# --- end to end over real UDP ------------------------------------------------------------

class FakeGame(threading.Thread):
    """Plays Unity's part: takes emg packets, plays a 5-zone level, echoes slot_applied."""

    def __init__(self, bridge_events_port: int, targets=(2, -1, 0, 1, -2), t_cue=0.9, t_flight=0.7):
        super().__init__(daemon=True)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(("127.0.0.1", 0))
        self.sock.settimeout(0.002)
        self.port = self.sock.getsockname()[1]
        self.events_addr = ("127.0.0.1", bridge_events_port)
        self.targets, self.t_cue, self.t_flight = list(targets), t_cue, t_flight
        self.slot, self.rest, self.last_seq, self.last_rx = 0, False, 0, None
        self.results, self.max_gap, self.calibs, self.packets, self.done = [], 0.0, 0, 0, False
        self.slot_times: list[tuple[float, int]] = []

    def send(self, name, **f):
        self.sock.sendto(p.encode(p.event(name, **f)), self.events_addr)

    def pump(self, until):
        while time.perf_counter() < until:
            try:
                data, _ = self.sock.recvfrom(65535)
            except (socket.timeout, BlockingIOError):
                continue
            msg = p.parse(data)
            now = time.perf_counter()
            if msg["type"] == "calib":
                self.calibs += 1
                continue
            if msg["seq"] <= self.last_seq:
                continue
            if self.last_rx is not None:
                self.max_gap = max(self.max_gap, now - self.last_rx)
            self.last_rx, self.last_seq, self.packets = now, msg["seq"], self.packets + 1
            if msg["slot"] != self.slot:
                self.slot_times.append((now, msg["slot"]))
                self.send("slot_applied", seq=msg["seq"])
            self.slot, self.rest = msg["slot"], msg["rest"]

    def run(self):
        t = time.perf_counter()
        self.pump(t + 0.5)
        self.send("level_start", level="L2", zones=5, shots=len(self.targets))
        for i, target in enumerate(self.targets, 1):
            rest_since = None
            deadline = time.perf_counter() + 5
            while time.perf_counter() < deadline:  # rest gate: rest for 0.5 s
                self.pump(time.perf_counter() + 0.01)
                if self.rest:
                    rest_since = rest_since or time.perf_counter()
                    if time.perf_counter() - rest_since >= 0.5:
                        break
                else:
                    rest_since = None
            self.send("shot_cue", shot=i, level="L2", zones=5, target=target)
            self.pump(time.perf_counter() + self.t_cue)
            self.send("kick", shot=i)
            self.pump(time.perf_counter() + self.t_flight)
            saved = self.slot == target
            self.results.append((target, self.slot, saved))
            self.send("arrival", shot=i, target=target, keeper_slot=self.slot, saved=saved)
            self.pump(time.perf_counter() + 0.3)
        self.send("level_end", level="L2", shots=len(self.targets), saves=sum(r[2] for r in self.results))
        self.pump(time.perf_counter() + 0.3)
        self.done = True


def live_bridge(tmp_path, player_cls, game_port=None):
    src = SimulatedSource(FS, CFG["simulator"], seed=11)
    player = player_cls(src, CFG["simulator"])
    events = EventReceiver("127.0.0.1", 0)
    game = FakeGame(events.port)
    sender = EmgSender("127.0.0.1", game.port)
    rec = Recorder(tmp_path, "e2e", {"source": {"fs": FS}})
    b = Bridge(CFG, src, mode="play", calibration=sim_calibration(), sender=sender, events=events, recorder=rec,
               player=player, log=lambda *_: None)
    return b, game, src, sender, events


def test_end_to_end_auto_player_saves_every_shot(tmp_path):
    b, game, src, sender, events = live_bridge(tmp_path, AutoPlayer)
    b.start()
    game.start()
    b.run(until=lambda br: game.done, duration=30)
    stats = b.close()
    sender.close()
    events.close()
    assert [r[2] for r in game.results] == [True] * 5, game.results
    assert game.calibs >= 5 and game.max_gap < 0.2
    assert stats["round_trip_ms"]["n"] >= 5 and stats["round_trip_ms"]["median_ms"] < 100
    shots = list(csv.DictReader(open(b.recorder.dir / "shots.csv")))
    assert len(shots) == 5 and all(s["bridge_saved"] == "1" and s["unity_saved"] == "1" for s in shots)
    assert all(float(s["reaction_time"]) > 0.3 for s in shots if s["target_slot"] != "0")
    ev = [e["name"] for e in csv.DictReader(open(b.recorder.dir / "events.csv"))]
    assert ev.count("shot_cue") == 5 and "slot_applied" not in ev


def test_end_to_end_key_2_moves_the_keeper_to_r2_and_dropout_gives_no_signal(tmp_path):
    b, game, src, sender, events = live_bridge(tmp_path, KeysPlayer)
    game.targets = []  # just listen
    b.start()
    t0 = time.perf_counter()
    state = {"pressed": None, "dropped": None}

    def script(br):
        now = time.perf_counter() - t0
        game.pump(time.perf_counter() + 0.001)
        if state["pressed"] is None and now > 0.5:
            br.on_key("2")
            state["pressed"] = time.perf_counter()
        if state["dropped"] is None and now > 2.0:
            src.dropout(0.6)
            state["dropped"] = time.perf_counter()

    b.run(duration=3.5, on_step=script)
    b.close()
    sender.close()
    events.close()
    t_r2 = next(t for t, s in game.slot_times if s == 2)
    assert 0.1 < t_r2 - state["pressed"] < 0.6   # muscle ramp + window + dwell (~0.25 s; slack for CI)
    assert game.max_gap > 0.25                    # Unity would show NO SIGNAL
    assert game.slot == 2                         # and the keeper comes back where the muscle is
