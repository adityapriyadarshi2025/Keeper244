"""Calibration maths (brief §4.4 worked example) and the guided procedure."""

import numpy as np
import pytest

from emg_bridge.config import load_config
from emg_bridge.control.calibration import (Calibration, CalibrationError, Procedure, build_steps, compute,
                                            moving_average_max)

CFG = load_config()["calibration"]
RATE = 100.0  # envelope frames per second
UV = 1e-6


def worked_example_segments(rng):
    """mu_rest 8 µV, sd 2 µV, e_MVC 400 µV, light 0.20, strong 0.50 (brief §4.4)."""
    rest = 8 * UV + rng.normal(0, 2 * UV, (1000, 2))
    mvc = [np.full((500, 2), 8 * UV) for _ in range(3)]
    for m in mvc:
        m[100:400, :] = 400 * UV
    span = 392 * UV

    def hold(a):
        h = np.full((300, 2), 8 * UV + a * span)
        h[:40] = 8 * UV  # ramp in: trimmed away
        return h

    seg = {"rest": [rest]}
    for mus in ("biceps", "triceps"):
        seg[f"mvc_{mus}"] = mvc
        seg[f"light_{mus}"] = [hold(0.20)] * 3
        seg[f"strong_{mus}"] = [hold(0.50)] * 3
    return seg


def test_worked_example():
    cal = compute(worked_example_segments(np.random.default_rng(1)), RATE, CFG)
    b = cal.biceps
    assert b.mu_rest == pytest.approx(8 * UV, abs=0.3 * UV)
    assert b.e_mvc == pytest.approx(400 * UV)
    assert b.th_on == pytest.approx(0.10) and b.th_off == pytest.approx(0.07)
    assert b.a_light == pytest.approx(0.20, abs=0.01) and b.a_strong == pytest.approx(0.50, abs=0.01)
    assert b.th_far == pytest.approx(0.35, abs=0.01)
    assert cal.checks_passed
    a = cal.normalise(np.array([[120 * UV, 8 * UV]]))
    assert a[0, 0] == pytest.approx((120 - b.mu_rest / UV) / (400 - b.mu_rest / UV), abs=1e-6)
    assert a[0, 1] == pytest.approx(0.0, abs=0.01)


def test_noisy_rest_raises_theta_on():
    seg = worked_example_segments(np.random.default_rng(2))
    seg["rest"] = [8 * UV + np.random.default_rng(3).normal(0, 20 * UV, (1000, 2))]  # sigma_n ~ 0.05
    cal = compute(seg, RATE, CFG)
    assert cal.biceps.th_on == pytest.approx(4 * cal.biceps.sigma_n)
    assert cal.biceps.th_on > 0.15


def test_light_and_strong_too_close_fail_validation():
    seg = worked_example_segments(np.random.default_rng(1))
    seg["strong_biceps"] = seg["light_biceps"]
    cal = compute(seg, RATE, CFG)
    assert cal.biceps.gap_ok is False and not cal.checks_passed
    assert cal.notes


def test_mvc_not_above_rest_is_an_error():
    seg = worked_example_segments(np.random.default_rng(1))
    seg["mvc_triceps"] = [np.full((500, 2), 8 * UV)]
    with pytest.raises(CalibrationError, match="triceps"):
        compute(seg, RATE, CFG)


def test_no_comfort_holds_uses_default_far():
    seg = worked_example_segments(np.random.default_rng(1))
    for k in list(seg):
        if k.startswith(("light", "strong")):
            del seg[k]
    cal = compute(seg, RATE, CFG)
    assert cal.biceps.th_far == CFG["default_theta_far"] and cal.biceps.light_ok is None


def test_save_load_round_trip(tmp_path):
    cal = compute(worked_example_segments(np.random.default_rng(1)), RATE, CFG)
    back = Calibration.load(cal.save(tmp_path / "calibration.json"))
    assert back.thresholds == cal.thresholds
    assert back.biceps.e_mvc == cal.biceps.e_mvc


def test_moving_average_max():
    x = np.r_[np.zeros(10), np.ones(5), np.zeros(10)]
    assert moving_average_max(x, 5) == 1.0
    assert moving_average_max(x, 10) == 0.5


def test_steps_follow_the_protocol():
    steps = build_steps(CFG)
    kinds = [s.kind for s in steps]
    assert kinds[0] == "settle" and kinds.count("mvc") == 6
    assert kinds.count("light") == 6 and kinds.count("strong") == 6
    assert all(s.segment for s in steps if s.kind in ("rest", "mvc", "light", "strong"))
    quick = build_steps(CFG, quick=True)
    assert sum(s.seconds for s in quick) < 60


def test_procedure_runs_and_collects_frames():
    steps = build_steps(CFG, quick=True)
    started = []
    proc = Procedure(steps, CFG, RATE, on_step=lambda s, i: started.append(s.kind))
    span, now = 392 * UV, 0.0
    level = {"rest": 0.0, "mvc": 1.0, "light": 0.2, "strong": 0.5}
    rng = np.random.default_rng(0)
    while not proc.done:
        proc.tick(now)
        s = proc.step
        e = np.full((1, 2), 8 * UV) + rng.normal(0, 1 * UV, (1, 2))
        if s is not None and s.muscle is not None and s.kind in level:
            e[0, 0 if s.muscle == "biceps" else 1] += level[s.kind] * span
        proc.on_frames(e)
        now += 1 / RATE
    assert proc.error is None, proc.error
    assert started[0] == "ready" and "mvc" in started
    cal = proc.result
    assert cal.biceps.a_light == pytest.approx(0.2, abs=0.02)
    assert cal.triceps.a_strong == pytest.approx(0.5, abs=0.02)
    assert cal.checks_passed


def test_operator_can_skip_waits():
    proc = Procedure(build_steps(CFG), CFG, RATE)
    proc.tick(0.0)
    assert proc.step.kind == "settle"
    proc.skip(1.0)
    assert proc.step.kind == "ready"
    proc.skip(1.5)  # not skippable
    assert proc.step.kind == "ready"
