"""Per-shot metrics on the bridge clock (io/shots.py), against GAME_LOGS.md definitions."""

import pytest

from emg_bridge.io.shots import ShotTracker


def play(tracker, timeline, target, zones=5, keeper=None, void=False):
    """timeline: [(t, slot)] frames; cue at t=0, arrival at the last frame's time."""
    tracker.on_event(0.0, {"name": "shot_cue", "shot": 1, "level": "L2", "zones": zones, "target": target})
    for t, slot in timeline:
        tracker.on_frame(t, slot, 0.3 if slot > 0 else 0.0, 0.3 if slot < 0 else 0.0)
    t_end = timeline[-1][0]
    if void:
        return tracker.on_event(t_end, {"name": "shot_void", "shot": 1, "reason": "no_signal"})
    k = timeline[-1][1] if keeper is None else keeper
    return tracker.on_event(t_end, {"name": "arrival", "shot": 1, "target": target, "keeper_slot": k, "saved": k == target})


def frames(*segments):
    """segments: (slot, seconds) at 100 Hz."""
    out, t = [], 0.0
    for slot, dur in segments:
        for _ in range(int(round(dur * 100))):
            t += 0.01
            out.append((round(t, 4), slot))
    return out


def test_clean_save():
    row = play(ShotTracker(), frames((0, 0.5), (1, 0.3), (2, 1.0)), target=2)
    assert row["bridge_saved"] == 1 and row["unity_saved"] == 1
    assert row["reaction_time"] == pytest.approx(0.51)
    assert row["wrong_direction"] == 0 and row["overshoot"] == 0 and row["undershoot"] == 0
    assert row["time_in_target_before_arrival"] == pytest.approx(0.99, abs=0.011)
    assert row["mean_a_b"] == 0.3 and row["voided"] == 0


def test_wrong_way_then_overshoot():
    row = play(ShotTracker(), frames((0, 0.3), (-1, 0.2), (0, 0.1), (2, 0.4), (1, 0.6)), target=1)
    assert row["wrong_direction"] == 1 and row["overshoot"] == 1 and row["bridge_saved"] == 1


def test_undershoot_and_centre_shot():
    row = play(ShotTracker(), frames((0, 0.5), (-1, 1.0)), target=-2)
    assert row["undershoot"] == 1 and row["bridge_saved"] == 0
    row = play(ShotTracker(), frames((0, 0.4), (1, 0.1), (0, 1.0)), target=0)
    assert row["wrong_direction"] == 1 and row["reaction_time"] == "" and row["bridge_saved"] == 1


def test_three_zone_compares_by_sign():
    row = play(ShotTracker(), frames((0, 0.5), (2, 1.0)), target=1, zones=3, keeper=1)
    assert row["bridge_slot_at_arrival"] == 1 and row["bridge_saved"] == 1 and row["overshoot"] == 0


def test_void_and_false_activations_between_shots():
    tr = ShotTracker()
    row = play(tr, frames((0, 0.5), (1, 0.5)), target=1, void=True)
    assert row["voided"] == 1 and row["unity_saved"] == ""
    for t, s in frames((0, 0.3), (1, 0.2), (0, 0.3), (-1, 0.2), (0, 0.5)):
        tr.on_frame(t + 10, s, 0.0, 0.0)
    row = play(tr, frames((0, 0.4), (1, 1.0)), target=1)
    assert row["false_activations_between_shots"] == 2
