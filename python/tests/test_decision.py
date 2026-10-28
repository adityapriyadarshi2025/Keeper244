"""Brief §5.6 decision tests (and the §4.5 hysteresis/debounce simulation)."""

import numpy as np
import pytest

from emg_bridge.control.decision import DecisionMachine, Thresholds

TICK = 0.010
TH = Thresholds(th_on=(0.10, 0.10), th_off=(0.07, 0.07), th_far=(0.35, 0.35))


def machine(**kw):
    return DecisionMachine(TH, dominance_k=1.5, dwell_s=kw.pop("dwell_s", 0.080), far_hysteresis_h=0.05, **kw)


def run(ab, at, m=None):
    m = m or machine()
    out = []
    for i, (b, t) in enumerate(zip(ab, at)):
        out.append(m.step(b, t, i * TICK))
    return np.array(out), m


def step_signal(level, n_before=50, n_after=100):
    return np.r_[np.zeros(n_before), np.full(n_after, level)]


def test_step_to_light_gives_plus_one_after_80_ms():
    ab = step_signal(0.25)
    slots, _ = run(ab, np.zeros_like(ab))
    first = np.argmax(slots != 0)
    assert slots[first] == 1
    assert abs((first - 50) * TICK - 0.080) <= TICK + 1e-9
    assert set(slots[first:]) == {1}


def test_step_to_strong_goes_straight_to_plus_two():
    ab = step_signal(0.6)
    slots, _ = run(ab, np.zeros_like(ab))
    changes = [s for i, s in enumerate(slots) if i == 0 or s != slots[i - 1]]
    assert changes == [0, 2]


def test_triceps_mirrors_biceps():
    at = step_signal(0.6)
    slots, _ = run(np.zeros_like(at), at)
    assert slots[-1] == -2
    at = step_signal(0.2)
    slots, _ = run(np.zeros_like(at), at)
    assert slots[-1] == -1


def test_co_contraction_stays_centre():
    a = step_signal(0.3)
    slots, _ = run(a, a)
    assert np.all(slots == 0)


def test_dominant_muscle_wins_under_co_contraction():
    ab, at = step_signal(0.5), step_signal(0.2)  # 0.5 >= 1.5 * 0.2
    slots, _ = run(ab, at)
    assert slots[-1] == 2


def test_50_ms_spike_is_ignored():
    ab = np.zeros(200)
    ab[100:105] = 0.6  # 5 ticks = 50 ms
    slots, _ = run(ab, np.zeros_like(ab))
    assert np.all(slots == 0)


def test_ramp_passes_through_near_to_far():
    ab = np.r_[np.zeros(20), np.linspace(0, 0.6, 60), np.full(50, 0.6)]
    slots, _ = run(ab, np.zeros_like(ab))
    seq = [s for i, s in enumerate(slots) if i == 0 or s != slots[i - 1]]
    assert seq == [0, 1, 2]


def chatter(hyst, dwell, seed):
    rng = np.random.default_rng(seed)
    n = int(30 / TICK)
    ab = 0.10 + rng.normal(0, 0.015, n)
    m = machine(dwell_s=dwell, hysteresis=hyst)
    run(ab, np.zeros(n), m)
    return m.changes


def test_hysteresis_and_debounce_stop_chatter():
    both = [chatter(True, 0.080, s) for s in range(20)]
    neither = [chatter(False, 0.0, s) for s in range(5)]
    assert max(both) <= 5 and np.mean(both) < 3
    assert np.mean(neither) > 1000  # brief §4.5: ~1500 without either


def test_rest_flag_and_slot3():
    m = machine()
    assert m.rest(0.05, 0.06) and not m.rest(0.08, 0.0)
    for i in range(30):
        m.step(0.6, 0.0, i * TICK)
    assert m.slot == 2 and m.slot3 == 1


@pytest.mark.parametrize("dwell", [0.0, 0.08])
def test_relax_returns_to_centre(dwell):
    ab = np.r_[np.full(50, 0.3), np.zeros(50)]
    slots, _ = run(ab, np.zeros_like(ab), machine(dwell_s=dwell))
    assert slots[49] == 1 and slots[-1] == 0
