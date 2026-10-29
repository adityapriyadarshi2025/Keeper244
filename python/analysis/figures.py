"""Every figure and number for the video, from recorded sessions (brief §7, handout key points).

    python -m analysis.figures ../data/sessions/<folder> [<folder> ...] [--out ../figures]

One folder of PNGs (16:9, slide-ready) per session, plus `summary.json` / `summary.md` with the
numbers to quote; with several sessions also a `combined/` folder (participants side by side).
Each figure is independent: if a session lacks the data for one (e.g. no game blocks), that figure
is skipped and the reason is written to the summary.

    01 protocol overview          raw EMG of the whole session with the condition blocks   (setup, acquisition)
    02 raw vs filtered            rest / biceps / triceps, raw and band-pass + notch       (noise identification)
    03 spectra (FFT / PSD)        rest vs contraction, mains peaks                         (noise identification)
    04 noise table                RMS, 50 Hz share, low-frequency share, SNR               (noise identification)
    05 filter responses           magnitude and group delay of each profile's filters      (SSRP concepts)
    06 filter effect              PSD before/after; causal vs zero-phase                   (SSRP concepts)
    07 envelope trade-off         delay vs smoothness for RMS windows and low-pass cut-offs (E3)
    08 random process             amplitude distributions, autocorrelation, stationarity    (SSRP concepts)
    09 crosstalk                  the other muscle's activation during single-muscle holds (E5)
    10 game by filter profile     saves, reaction time, wrong moves, jitter per profile    (E4 live, handout pt 5)
    11 replay by filter profile   the same recording through every profile                 (E4 offline)
    12 confusion matrix           target vs keeper slot                                    (E6)
    13 reaction times             distribution per profile                                 (E6)
    14 fatigue                    median frequency across the contraction blocks           (E7)
    15 latency                    onset -> slot, round trip, budget                         (E8)
"""

from __future__ import annotations

import argparse
import json
import sys
import traceback
from pathlib import Path
from typing import Any, Callable

import numpy as np
import pandas as pd
from scipy import signal, stats

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from analysis import style  # noqa: E402
from analysis.session import Session, load  # noqa: E402
from emg_bridge.dsp.features import median_frequency, psd_welch  # noqa: E402
from emg_bridge.dsp.filters import design_chain, frequency_response  # noqa: E402
from emg_bridge.dsp.pipeline import profile_names, profile_settings  # noqa: E402
from emg_bridge.offline import process  # noqa: E402

import matplotlib.pyplot as plt  # noqa: E402

UV = 1e-6
MUSCLES = ("biceps", "triceps")


def ordered(profiles) -> list[str]:
    """Profiles in one fixed order in every figure (config order: full, bandpass, raw, smooth, fast)."""
    present = list(dict.fromkeys(profiles))
    canon = [p for p in style.PROFILE if p in present]
    return canon + [p for p in present if p not in canon]


class Skip(Exception):
    """The session has no data for this figure."""


def cfg_of(s: Session) -> dict:
    return s.meta["config"]


def filtered(s: Session, x: np.ndarray, profile: str = "full") -> np.ndarray:
    sos = design_chain(s.fs, profile_settings(cfg_of(s), profile)["filters"])
    x = np.nan_to_num(np.asarray(x, dtype=float))
    return signal.sosfilt(sos, x - x[:1], axis=0) if len(x) else x


def need_block(s: Session, name: str) -> tuple[float, float]:
    b = s.block(name)
    if b is None:
        raise Skip(f"no '{name}' block in this session (run the protocol: python -m emg_bridge protocol)")
    return b


def steps_with(s: Session, kind: str, muscle: str | None = None, level: str | None = None) -> pd.DataFrame:
    st = s.steps[(s.steps["type"] == "protocol") & (s.steps["kind"] == kind)]
    if muscle is not None:
        st = st[st["muscle"] == muscle]
    if level is not None:
        st = st[st["level"] == level]
    return st


def hold_segments(s: Session, muscle: str, level: str | None = None, trim: float = 1.0) -> list[np.ndarray]:
    """Raw samples of each flex step of a muscle, without the first/last `trim` seconds (ramps)."""
    segs = []
    for _, r in steps_with(s, "flex", muscle, level).iterrows():
        t0, t1 = r["t0"] + trim, r["t1"] - min(trim, 0.5)
        if t1 - t0 > 0.5:
            segs.append(s.raw_between(t0, t1))
    return segs


def decimate_minmax(t: np.ndarray, y: np.ndarray, bins: int = 4000) -> tuple[np.ndarray, np.ndarray]:
    """Keep the visual envelope of a long trace (min and max per bin)."""
    if len(y) <= 2 * bins:
        return t, y
    n = len(y) // bins * bins
    yb = y[:n].reshape(bins, -1)
    tb = t[:n].reshape(bins, -1)[:, 0]
    return np.repeat(tb, 2), np.column_stack([np.nanmin(yb, 1), np.nanmax(yb, 1)]).ravel()


# --- 01 ---------------------------------------------------------------------------------------
def fig_overview(s: Session, out: Path) -> dict:
    fig, axes = plt.subplots(2, 1, figsize=style.WIDE, sharex=True)
    t = (s.raw_t - s.raw_t[0]) / 60.0
    for i, (ax, m) in enumerate(zip(axes, MUSCLES)):
        tt, yy = decimate_minmax(t, s.x[:, i] / UV)
        ax.plot(tt, yy, color=style.MUSCLE[m], lw=0.6)
        ax.set_ylabel(f"{m} (µV)")
        lim = np.nanpercentile(np.abs(s.x[:, i] / UV), 99.9) * 1.2
        ax.set_ylim(-lim, lim)
    spans = []
    cal = s.steps[s.steps["type"] == "calibration"]
    if not cal.empty:
        spans.append(("calibration", float(cal["t0"].min()), float(cal["t1"].max())))
    for b in s.blocks():
        if b.startswith("cal_"):
            continue
        t0, t1 = s.block(b)
        spans.append((b, t0, t1))
    for b, t0, t1 in spans:
        x0, x1 = (t0 - s.raw_t[0]) / 60, (t1 - s.raw_t[0]) / 60
        for ax in axes:
            ax.axvspan(x0, x1, color=style.GRID, alpha=0.6, lw=0)
        axes[0].text((x0 + x1) / 2, axes[0].get_ylim()[1] * 0.97, b.replace("_", " "), ha="center", va="top",
                     fontsize=9, color=style.INK_2)
    axes[-1].set_xlabel("time in session (min)")
    dur = (s.raw_t[-1] - s.raw_t[0]) / 60
    blocks = {b: round((t1 - t0) / 60, 2) for b, t0, t1 in spans if not b.startswith("cal_")}
    finish(fig, out / "01_protocol_overview.png", f"{s.participant}: the whole session ({dur:.0f} min of raw EMG)",
           "shaded = calibration, then the protocol blocks: rest, biceps and triceps contractions, "
           "and one game block per filter profile", s)
    return {"duration_min": round(dur, 1), "block_minutes": blocks,
            "fs_nominal_hz": s.fs, "fs_measured_hz": s.meta.get("fs_measured_hz")}


# --- 02 ---------------------------------------------------------------------------------------
def fig_raw_vs_filtered(s: Session, out: Path) -> dict:
    rest = need_block(s, "rest")
    windows = [("rest", (rest[0] + rest[1]) / 2 - 1.5, None)]
    for m in MUSCLES:
        st = steps_with(s, "flex", m, "strong")
        if st.empty:
            raise Skip("no strong flex steps")
        r = st.iloc[len(st) // 2]
        windows.append((f"{m} strong flex", r["t0"] - 1.0, m))
    fig, axes = plt.subplots(2, 3, figsize=style.WIDE, sharey="row")
    for col, (label, t0, _m) in enumerate(windows):
        pre = 2.0  # filter from a little earlier so the window starts settled
        seg = s.raw_between(t0 - pre, t0 + 3.0)
        y = filtered(s, seg)
        n0 = int(pre * s.fs)
        tt = np.arange(len(seg) - n0) / s.fs
        for row, m in enumerate(MUSCLES):
            ax = axes[row, col]
            ax.plot(tt, seg[n0:, row] / UV - np.nanmean(seg[n0:, row] / UV), color=style.INK_3, lw=0.6, label="raw")
            ax.plot(tt, y[n0:, row] / UV, color=style.MUSCLE[m], lw=0.7, label="band-pass + notch")
            if row == 0:
                ax.set_title(label)
            if col == 0:
                ax.set_ylabel(f"{m} channel (µV)")
            if row == 1:
                ax.set_xlabel("time (s)")
    axes[0, 0].legend(loc="upper right")
    finish(fig, out / "02_raw_vs_filtered.png", "Raw vs filtered EMG", "raw shown with its mean removed; "
           "filters = the 'full' profile (20–450 Hz band-pass + 50 Hz notch), causal as in real time", s)
    return {}


# --- 03 / 04 ----------------------------------------------------------------------------------
def spectra(s: Session) -> dict[str, Any]:
    rest = need_block(s, "rest")
    xr = s.raw_between(*rest)
    xr = xr[np.isfinite(xr).all(1)]
    res: dict[str, Any] = {"rest": psd_welch(xr - xr.mean(0), s.fs)}
    for m in MUSCLES:
        for lv in ("light", "strong"):
            segs = [g[np.isfinite(g).all(1)] for g in hold_segments(s, m, lv)]
            segs = [g - g.mean(0) for g in segs if len(g) > s.fs]
            if segs:
                res[f"{m}_{lv}"] = psd_welch(np.vstack(segs), s.fs)
    return res


def fig_spectra(s: Session, out: Path) -> dict:
    sp = spectra(s)
    fig, axes = plt.subplots(1, 2, figsize=style.WIDE, sharey=True)
    f, p_rest = sp["rest"]
    for i, (ax, m) in enumerate(zip(axes, MUSCLES)):
        ax.axvspan(20, 450, color=style.GRID, alpha=0.5, lw=0, label="pass band 20–450 Hz")
        ax.semilogy(f, p_rest[:, i] / UV**2, color=style.REST, label="rest")
        if f"{m}_strong" in sp:
            f2, p2 = sp[f"{m}_strong"]
            ax.semilogy(f2, p2[:, i] / UV**2, color=style.MUSCLE[m], label=f"{m} strong contraction")
        for h in (50, 100, 150):
            ax.axvline(h, color=style.INK_3, lw=0.6, ls=":")
        ax.set_xlim(0, min(600, s.fs / 2))
        ax.set_title(f"{m} channel")
        ax.set_xlabel("frequency (Hz)")
        if i == 0:
            ax.set_ylabel("power spectral density (µV²/Hz)")
        ax.legend(loc="center right")
    finish(fig, out / "03_spectra.png", "Where the noise is: power spectra (Welch FFT, 1 s Hann windows)",
           "dotted lines: 50 Hz mains and harmonics; contraction power spreads across 20–450 Hz", s)
    return {}


def noise_table(s: Session) -> pd.DataFrame:
    sp = spectra(s)
    f, p_rest = sp["rest"]
    rest = s.raw_between(*need_block(s, "rest"))
    rest = rest[np.isfinite(rest).all(1)]
    y_rest = filtered(s, rest)[int(s.fs):]
    rows = []
    band = (f >= 20) & (f <= 450)
    for i, m in enumerate(MUSCLES):
        tot = np.trapezoid(p_rest[:, i], f)
        row = {"channel": m,
               "rest RMS raw (µV)": float(np.std(rest[:, i]) / UV),
               "rest RMS filtered (µV)": float(np.sqrt(np.mean(y_rest[:, i] ** 2)) / UV),
               "50 Hz share of rest power (%)": float(100 * p_rest[(f >= 49) & (f <= 51), i].sum() * (f[1] - f[0]) / tot),
               "< 20 Hz share of rest power (%)": float(100 * np.trapezoid(p_rest[f < 20, i], f[f < 20]) / tot),
               "rest power in 20-450 Hz (%)": float(100 * np.trapezoid(p_rest[band, i], f[band]) / tot)}
        for lv in ("light", "strong"):
            segs = [g[np.isfinite(g).all(1)] for g in hold_segments(s, m, lv)]
            if segs:
                yy = np.concatenate([filtered(s, g)[int(0.3 * s.fs):, i] for g in segs if len(g) > s.fs])
                row[f"SNR {lv} (dB)"] = float(20 * np.log10(np.sqrt(np.mean(yy ** 2)) / max(row["rest RMS filtered (µV)"] * UV, 1e-12)))
        rows.append(row)
    return pd.DataFrame(rows)


def fig_noise_table(s: Session, out: Path) -> dict:
    df = noise_table(s)
    df.to_csv(out / "04_noise_table.csv", index=False)
    fig, ax = plt.subplots(figsize=(12, 2.8))
    ax.axis("off")
    cols = list(df.columns)
    cells = [[r["channel"]] + [f"{r[c]:.1f}" if pd.notna(r[c]) else "–" for c in cols[1:]] for _, r in df.iterrows()]
    tb = ax.table(cellText=cells, colLabels=[c.replace(" (", "\n(") for c in cols], loc="center", cellLoc="center")
    tb.auto_set_font_size(False)
    tb.set_fontsize(9)
    tb.scale(1, 2.2)
    for (r, c), cell in tb.get_celld().items():
        cell.set_edgecolor(style.GRID)
        if r == 0:
            cell.set_text_props(color=style.INK_2, weight="bold")
    finish(fig, out / "04_noise_table.png", "Noise identification: numbers", "SNR = 20·log10(RMS during holds / RMS at rest), "
           "both after the band-pass + notch", s)
    return {"noise": df.round(2).to_dict(orient="records")}


# --- 05 / 06 ----------------------------------------------------------------------------------
def fig_filter_responses(s: Session, out: Path) -> dict:
    cfg = cfg_of(s)
    f = np.unique(np.concatenate([np.logspace(0, np.log10(s.fs / 2 * 0.98), 800), np.linspace(45, 55, 201)]))
    fig, (a1, a2) = plt.subplots(1, 2, figsize=style.WIDE)
    shown = {}
    for name in profile_names(cfg):
        st = profile_settings(cfg, name)
        sos = design_chain(s.fs, st["filters"])
        key = json.dumps(st["filters"], sort_keys=True)
        if key in shown:
            continue  # smooth / fast share the full profile's filters
        shown[key] = name
        mag, gd = frequency_response(sos, s.fs, f)
        z, ls = (3, "-") if name == "full" else (2, "--")
        a1.semilogx(f, mag, color=style.profile_color(name), label=name, zorder=z, ls=ls)
        a2.semilogx(f, gd * 1000, color=style.profile_color(name), label=name, zorder=z, ls=ls)
    a1.set_ylim(-60, 5)
    a1.set_xlabel("frequency (Hz)")
    a1.set_ylabel("gain (dB)")
    a1.set_title("Magnitude")
    a2.set_ylim(-5, 40)
    a2.set_xlabel("frequency (Hz)")
    a2.set_ylabel("group delay (ms)")
    a2.set_title("Group delay (what the filters cost in time)")
    a1.legend(loc="lower center")
    finish(fig, out / "05_filter_responses.png", "The filters: 4th-order Butterworth band-pass 20–450 Hz, IIR notch 50 Hz (Q 30)",
           "smooth and fast use the full profile's filters with a different envelope window (figure 07)", s)
    _, gd100 = frequency_response(design_chain(s.fs, profile_settings(cfg, "full")["filters"]), s.fs, np.array([99.0, 100.0]))
    return {"full_group_delay_ms_at_100Hz": round(float(gd100[-1] * 1000), 2)}


def fig_filter_effect(s: Session, out: Path) -> dict:
    rest = s.raw_between(*need_block(s, "rest"))
    rest = rest[np.isfinite(rest).all(1)]
    y = filtered(s, rest)[int(s.fs):]
    f, p_raw = psd_welch(rest[int(s.fs):] - rest.mean(0), s.fs)
    _, p_f = psd_welch(y, s.fs)
    st = steps_with(s, "flex", "biceps", "strong")
    if st.empty:
        raise Skip("no biceps flex steps")
    t_on = float(st.iloc[len(st) // 2]["t0"])
    seg = s.raw_between(t_on - 2.0, t_on + 2.0)[:, 0]
    sos = design_chain(s.fs, profile_settings(cfg_of(s), "full")["filters"])
    w = int(cfg_of(s)["envelope"]["rms_window_ms"] / 1000 * s.fs)
    causal = np.sqrt(np.convolve(signal.sosfilt(sos, seg - seg[0]) ** 2, np.ones(w) / w)[: len(seg)])  # trailing window
    zero_y = signal.sosfiltfilt(sos, seg - seg[0]) ** 2
    zero = np.sqrt(np.convolve(zero_y, np.ones(w) / w, mode="same"))                                    # centred window
    tt = np.arange(len(seg)) / s.fs - 2.0
    fig, (a1, a2) = plt.subplots(1, 2, figsize=style.WIDE)
    a1.semilogy(f, p_raw[:, 0] / UV**2, color=style.INK_3, label="raw")
    a1.semilogy(f, p_f[:, 0] / UV**2, color=style.MUSCLE["biceps"], label="filtered")
    a1.set_xlim(0, min(600, s.fs / 2))
    a1.set_ylim(np.median(p_raw[:, 0] / UV**2) * 1e-3, None)
    a1.set_xlabel("frequency (Hz)")
    a1.set_ylabel("PSD at rest (µV²/Hz)")
    a1.set_title("Biceps channel at rest: before vs after")
    a1.legend()
    sel = (tt > -0.3) & (tt < 1.5)
    a2.plot(tt[sel] * 1000, zero[sel] / UV, color=style.INK_3, label="offline: zero-phase filters, centred RMS")
    a2.plot(tt[sel] * 1000, causal[sel] / UV, color=style.MUSCLE["biceps"], label="real time: causal filters, trailing RMS")
    a2.set_xlabel("time after the flex cue (ms)")
    a2.set_ylabel("envelope (µV)")
    a2.set_title("The price of real time: the live envelope lags")
    a2.legend(loc="lower right")
    finish(fig, out / "06_filter_effect.png", "What filtering does", "left: the 50 Hz mains peak and the drift below 20 Hz "
           "are removed; right: one biceps contraction, envelope computed offline (no delay) and as the bridge does it live", s)
    return {}


# --- 07 ---------------------------------------------------------------------------------------
def fig_envelope_tradeoff(s: Session, out: Path) -> dict:
    b = need_block(s, "biceps")
    i0, i1 = s.raw_index(*b)
    x = s.x[i0:i1]
    cfg = cfg_of(s)
    holds = steps_with(s, "flex", "biceps", "strong")
    variants = [("rms", w) for w in (50, 100, 150, 250, 350)] + [("lowpass", fc) for fc in (2, 4, 6, 8)]
    rows = []
    y = filtered(s, x)
    for method, val in variants:
        c = json.loads(json.dumps(cfg))
        c["envelope"]["method"] = method
        if method == "rms":
            c["envelope"]["rms_window_ms"] = val
        else:
            c["envelope"]["lowpass_fc_hz"] = val
        res = process(x, s.fs, c, "full", n0=i0)
        e = res["e"][:, 0]
        t = s.raw_t[np.clip(res["k"] - s.n[0], 0, len(s.raw_t) - 1)]
        # zero-phase reference: centred moving RMS (50 ms) of the filtered signal on the same grid
        k_rel = res["k"] - i0
        w = int(0.05 * s.fs)
        cs = np.concatenate([[0], np.cumsum(y[:, 0] ** 2)])
        lo, hi = np.clip(k_rel - w // 2, 0, len(y)), np.clip(k_rel + w // 2, 0, len(y))
        ref = np.sqrt((cs[hi] - cs[lo]) / np.maximum(hi - lo, 1))
        a, r_ = e - e.mean(), ref - ref.mean()
        lags = np.arange(0, 60)  # frames of 10 ms
        cc = [np.dot(a[L:], r_[:len(r_) - L]) for L in lags]
        delay_ms = float(lags[int(np.argmax(cc))] * 10)
        cvs = []
        for _, h in holds.iterrows():
            sel = (t >= h["t0"] + 1.5) & (t <= h["t1"] - 0.5)
            if sel.sum() > 10:
                cvs.append(np.std(e[sel]) / np.mean(e[sel]))
        rows.append({"method": method, "setting": val, "delay_ms": delay_ms,
                     "ripple_cv": float(np.mean(cvs)) if cvs else np.nan})
    df = pd.DataFrame(rows)
    df.to_csv(out / "07_envelope_tradeoff.csv", index=False)
    fig, ax = plt.subplots(figsize=style.WIDE)
    for method, col, lab in (("rms", style.SERIES[0], "moving RMS, window (ms)"), ("lowpass", style.SERIES[1], "rectify + low-pass, cut-off (Hz)")):
        d = df[df["method"] == method]
        ax.plot(d["delay_ms"], d["ripple_cv"] * 100, "o-", color=col, label=lab, ms=7)
        for _, r in d.iterrows():
            ax.annotate(f"{r['setting']:g}", (r["delay_ms"], r["ripple_cv"] * 100), textcoords="offset points",
                        xytext=(6, 4), fontsize=9, color=style.INK_2)
    chosen = df[(df["method"] == cfg["envelope"]["method"]) & (df["setting"] == cfg["envelope"]["rms_window_ms"])]
    if not chosen.empty:
        ax.scatter(chosen["delay_ms"], chosen["ripple_cv"] * 100, s=180, facecolors="none", edgecolors=style.INK, lw=1.5,
                   label="our choice", zorder=5)
    ax.set_xlabel("delay vs a zero-phase reference (ms)   ← faster")
    ax.set_ylabel("ripple during steady holds (CV, %)   ← smoother")
    ax.legend()
    finish(fig, out / "07_envelope_tradeoff.png", "Envelope: delay vs smoothness (E3)",
           "biceps block, strong holds; each point is one envelope setting run over the same recording", s)
    return {"envelope_tradeoff": df.round(3).to_dict(orient="records")}


# --- 08 ---------------------------------------------------------------------------------------
def fig_random_process(s: Session, out: Path) -> dict:
    rest = s.raw_between(*need_block(s, "rest"))
    rest = rest[np.isfinite(rest).all(1)]
    y_rest = filtered(s, rest)[int(s.fs):, 0]
    segs = [g[np.isfinite(g).all(1)] for g in hold_segments(s, "biceps", "strong")]
    if not segs:
        raise Skip("no biceps strong holds")
    y_flex = np.concatenate([filtered(s, g)[int(0.3 * s.fs):, 0] for g in segs if len(g) > s.fs])
    fig, axes = plt.subplots(1, 3, figsize=style.WIDE)
    ax = axes[0]
    z = np.linspace(-5, 5, 400)
    out_stats = {}
    for lab, v, col in (("rest", y_rest, style.REST), ("strong contraction", y_flex, style.MUSCLE["biceps"])):
        vn = v / np.std(v)
        k = float(stats.kurtosis(vn))
        out_stats[f"kurtosis_{lab.split()[0]}"] = round(k, 2)
        ax.hist(vn, bins=120, range=(-5, 5), density=True, histtype="step", color=col, lw=1.4,
                label=f"{lab} (excess kurtosis {k:.1f})")
    ax.plot(z, stats.norm.pdf(z), color=style.INK, lw=1, ls="--", label="Gaussian")
    ax.set_yscale("log")
    ax.set_ylim(1e-4, 1)
    ax.set_xlabel("amplitude / standard deviation")
    ax.set_ylabel("probability density")
    ax.set_title("Amplitude distribution")
    ax.legend(fontsize=8, loc="upper right")
    ax = axes[1]
    lags = int(0.06 * s.fs)
    for lab, v, col in (("raw at rest", rest[:, 0] - rest[:, 0].mean(), style.INK_3), ("filtered at rest", y_rest, style.SERIES[0])):
        v = v[: int(30 * s.fs)]
        ac = signal.correlate(v, v, mode="full", method="fft")[len(v) - 1: len(v) - 1 + lags]
        ax.plot(np.arange(lags) / s.fs * 1000, ac / ac[0], color=col, label=lab)
    ax.set_xlabel("lag (ms)")
    ax.set_ylabel("autocorrelation")
    ax.set_title("Autocorrelation")
    ax.legend(fontsize=8)
    ax = axes[2]
    w = int(s.fs)
    n = len(y_rest) // w
    rms_1s = np.sqrt(np.mean(y_rest[: n * w].reshape(n, w) ** 2, axis=1)) / UV
    ax.plot(np.arange(n) / 60, rms_1s, color=style.SERIES[0], label="1 s RMS")
    half = n // 2
    if half > 2:
        f_stat = np.var(y_rest[: half * w]) / np.var(y_rest[half * w: 2 * half * w])
        out_stats["rest_variance_ratio_first_second_half"] = round(float(f_stat), 3)
        ax.axhline(np.mean(rms_1s[:half]), xmax=0.5, color=style.INK_2, lw=1, ls="--")
        ax.axhline(np.mean(rms_1s[half:]), xmin=0.5, color=style.INK_2, lw=1, ls="--", label="half means")
    ax.set_xlabel("time in rest block (min)")
    ax.set_ylabel("µV")
    ax.set_title("Stationarity of the rest block")
    ax.legend(fontsize=8)
    out_stats["rest_rms_cv_percent"] = round(float(np.std(rms_1s) / np.mean(rms_1s) * 100), 2)
    finish(fig, out / "08_random_process.png", "EMG as a random process", "biceps channel. Left: dashed = Gaussian, "
           "excess kurtosis 0 = Gaussian, > 0 = heavier tails. Middle: a periodic autocorrelation (20 ms period) means "
           "50 Hz mains in the signal. Right: a stationary rest block keeps the same RMS level throughout", s)
    return out_stats


# --- 09 ---------------------------------------------------------------------------------------
def activation_steps(s: Session, muscle: str) -> pd.DataFrame:
    """Mean live activations of both muscles over the steady part of each flex step of `muscle`."""
    rows = []
    for _, r in steps_with(s, "flex", muscle).iterrows():
        e = s.env_between(r["t0"] + 1.5, r["t1"] - 0.3)
        if len(e) > 10 and e["a_b"].notna().any():
            rows.append({"t": r["t0"], "level": r["level"], "a_b": e["a_b"].mean(), "a_t": e["a_t"].mean(),
                         "profile": e["profile"].iloc[0] if "profile" in e else ""})
    return pd.DataFrame(rows)


def fig_crosstalk(s: Session, out: Path) -> dict:
    k = float(cfg_of(s)["decision"]["dominance_k"])
    fig, ax = plt.subplots(figsize=style.WIDE)
    res = {}
    for i, m in enumerate(MUSCLES):
        d = activation_steps(s, m)
        if d.empty:
            raise Skip("no flex steps with activations")
        own, other = (d["a_b"], d["a_t"]) if m == "biceps" else (d["a_t"], d["a_b"])
        ratio = other / own.clip(lower=1e-6)
        res[f"{m}_hold_other_over_own_median"] = round(float(ratio.median()), 3)
        for lv, mk in (("light", "o"), ("strong", "s")):
            sel = d["level"] == lv
            ax.scatter(own[sel], other[sel], color=style.MUSCLE[m], marker=mk, s=45, alpha=0.85, label=f"{m} holds, {lv}")
    lim = 1.0
    xx = np.linspace(0, lim, 50)
    ax.plot(xx, xx / k, color=style.INK, lw=1, ls="--", label=f"dominance limit (own ≥ {k:g} × other)")
    ax.set_xlim(0, lim)
    ax.set_ylim(0, lim / 2)
    ax.set_xlabel("activation of the muscle being flexed (fraction of MVC)")
    ax.set_ylabel("activation of the other muscle")
    ax.legend(loc="upper left")
    finish(fig, out / "09_crosstalk.png", "Crosstalk and co-activation (E5)",
           "each point = one 5 s hold (steady part); points below the dashed line give a clean left/right decision", s)
    return res


# --- 10 / 12 / 13 -----------------------------------------------------------------------------
def game_table(s: Session) -> pd.DataFrame:
    sh = s.shots
    if sh.empty or "profile" not in sh:
        raise Skip("no shots (play the game blocks)")
    sh = sh[sh["voided"] == 0].copy()
    if sh.empty:
        raise Skip("no completed shots")
    rows = []
    for prof in ordered(sh["profile"]):
        g = sh[sh["profile"] == prof]
        saved = g["unity_saved"] if g["unity_saved"].notna().all() else g["bridge_saved"]
        rt = pd.to_numeric(g["reaction_time"], errors="coerce")
        blk = s.block(f"game_{prof}")
        jitter = np.nan
        if blk is not None:
            e = s.env_between(*blk)
            if len(e) > 1:
                jitter = float((e["slot"].diff().fillna(0) != 0).sum() / ((e["t"].iloc[-1] - e["t"].iloc[0]) / 60))
        rows.append({"profile": prof, "shots": len(g), "save_%": 100 * float(saved.mean()),
                     "reaction_time_median_ms": 1000 * float(rt.median()) if rt.notna().any() else np.nan,
                     "wrong_direction_%": 100 * float(pd.to_numeric(g["wrong_direction"], errors="coerce").mean()),
                     "overshoot_%": 100 * float(pd.to_numeric(g["overshoot"], errors="coerce").mean()),
                     "false_activations_per_shot": float(pd.to_numeric(g["false_activations_between_shots"],
                                                                       errors="coerce").mean()),
                     "slot_changes_per_min": jitter})
    return pd.DataFrame(rows)


def fig_game_by_profile(s: Session, out: Path) -> dict:
    df = game_table(s)
    df.to_csv(out / "10_game_by_profile.csv", index=False)
    metrics = [("save_%", "saves (%)", "higher is better"), ("reaction_time_median_ms", "reaction time, median (ms)", "lower is better"),
               ("wrong_direction_%", "first move wrong way (%)", "lower is better"),
               ("slot_changes_per_min", "keeper moves per minute", "same shots: fewer = steadier")]
    fig, axes = plt.subplots(1, len(metrics), figsize=style.WIDE)
    for ax, (col, lab, hint) in zip(axes, metrics):
        cols = [style.profile_color(p) for p in df["profile"]]
        ax.bar(df["profile"], df[col], color=cols, width=0.6, edgecolor=style.SURFACE, lw=2)
        for xi, v in enumerate(df[col]):
            if pd.notna(v):
                ax.text(xi, v, f"{v:.0f}", ha="center", va="bottom", fontsize=9, color=style.INK)
        ax.set_title(lab, fontsize=10)
        ax.set_xlabel(hint, color=style.INK_3, fontsize=8)
        ax.grid(axis="x", visible=False)
    finish(fig, out / "10_game_by_profile.png", "Does filtering change the game? Same player, same level, one block per filter profile",
           f"{int(df['shots'].sum())} shots; profiles: " + ", ".join(df["profile"]), s)
    return {"game_by_profile": df.round(2).to_dict(orient="records")}


def fig_confusion(s: Session, out: Path) -> dict:
    sh = s.shots[(s.shots["voided"] == 0) & (s.shots["zones"] == 5)] if not s.shots.empty else s.shots
    if sh.empty:
        raise Skip("no 5-zone shots")
    profs = ordered(sh["profile"])
    fig, axes = plt.subplots(1, len(profs), figsize=style.WIDE, squeeze=False)
    out_d = {}
    for ax, prof in zip(axes[0], profs):
        g = sh[sh["profile"] == prof]
        m = np.zeros((5, 5), dtype=int)
        for _, r in g.iterrows():
            m[int(r["target_slot"]) + 2, int(r["bridge_slot_at_arrival"]) + 2] += 1
        acc = np.trace(m) / max(m.sum(), 1)
        out_d[f"accuracy_{prof}"] = round(float(acc), 3)
        ax.imshow(m, cmap=matplotlib_cmap(), vmin=0, vmax=max(m.max(), 1))
        for i in range(5):
            for j in range(5):
                if m[i, j]:
                    ax.text(j, i, str(m[i, j]), ha="center", va="center", fontsize=9,
                            color=style.SURFACE if m[i, j] > m.max() * 0.55 else style.INK)
        labels = ["L2", "L1", "C", "R1", "R2"]
        ax.set_xticks(range(5), labels)
        ax.set_yticks(range(5), labels)
        ax.set_xlabel("keeper at arrival")
        ax.set_ylabel("ball target")
        ax.set_title(f"{prof}: {acc * 100:.0f} % correct")
        ax.grid(False)
    finish(fig, out / "12_confusion.png", "Where the keeper ended up vs where the ball went (5-zone shots)",
           "L = triceps side, R = biceps side; off-diagonal = misses (near/far confusions sit next to the diagonal)", s)
    return out_d


def matplotlib_cmap():
    from matplotlib.colors import LinearSegmentedColormap
    return LinearSegmentedColormap.from_list("seq", [style.SURFACE] + style.SEQ)


def fig_reaction_times(s: Session, out: Path) -> dict:
    sh = s.shots[s.shots["voided"] == 0] if not s.shots.empty else s.shots
    if sh.empty:
        raise Skip("no shots")
    profs = ordered(sh["profile"])
    data = [pd.to_numeric(sh[sh["profile"] == p]["reaction_time"], errors="coerce").dropna().to_numpy() * 1000 for p in profs]
    if not any(len(d) for d in data):
        raise Skip("no reaction times")
    fig, ax = plt.subplots(figsize=style.WIDE)
    bp = ax.boxplot(data, tick_labels=profs, widths=0.5, patch_artist=True, showfliers=False)
    for patch, p in zip(bp["boxes"], profs):
        patch.set_facecolor(style.profile_color(p))
        patch.set_alpha(0.35)
        patch.set_edgecolor(style.profile_color(p))
    for med in bp["medians"]:
        med.set_color(style.INK)
    rng = np.random.default_rng(0)
    for i, (d, p) in enumerate(zip(data, profs)):
        ax.scatter(i + 1 + rng.uniform(-0.12, 0.12, len(d)), d, s=14, color=style.profile_color(p), alpha=0.8)
    ax.set_ylabel("reaction time: cue → keeper on the correct side (ms)")
    finish(fig, out / "13_reaction_times.png", "Reaction times per filter profile (E6)",
           "centre shots excluded (no movement needed)", s)
    return {"rt_median_ms": {p: round(float(np.median(d)), 1) for p, d in zip(profs, data) if len(d)}}


# --- 11 ---------------------------------------------------------------------------------------
def fig_replay_by_profile(s: Session, out: Path) -> dict:
    if s.cal is None:
        raise Skip("no calibration")
    b0, b1 = need_block(s, "biceps"), need_block(s, "triceps")
    i0, i1 = s.raw_index(b0[0], b1[1])
    x = s.x[i0:i1]
    cfg = cfg_of(s)
    profs = [p for p in profile_names(cfg) if p in s.cal.names]
    flex = s.steps[(s.steps["type"] == "protocol") & s.steps["kind"].isin(["flex", "relax"]) & (s.steps["t0"] >= b0[0])]
    rows, traces = [], {}
    for prof in profs:
        res = process(x, s.fs, cfg, prof, s.cal, n0=int(s.n[i0]))
        t = s.raw_t[np.clip(res["k"] - s.n[0], 0, len(s.raw_t) - 1)]
        slot = res["slot"]
        traces[prof] = (t, slot)
        lat, acc, chat, false = [], [], 0.0, []
        hold_time = 0.0
        for _, r in flex.iterrows():
            sel = (t >= r["t0"]) & (t < r["t1"])
            if sel.sum() < 5:
                continue
            ss, ts = slot[sel], t[sel]
            if r["kind"] == "flex":
                want = 1 if r["muscle"] == "biceps" else -1
                hit = np.flatnonzero(np.sign(ss) == want)
                if len(hit):
                    lat.append(ts[hit[0]] - r["t0"])
                steady = ts >= r["t0"] + 1.0
                acc.append(np.mean(np.sign(ss[steady]) == want) if steady.any() else np.nan)
                chat += np.count_nonzero(np.diff(ss[steady]))
                hold_time += (r["t1"] - r["t0"] - 1.0)
            else:
                late = ts >= r["t0"] + 1.0
                false.append(np.mean(ss[late] != 0) if late.any() else np.nan)
        rows.append({"profile": prof, "onset_to_slot_ms": 1000 * float(np.median(lat)) if lat else np.nan,
                     "correct_side_during_holds_%": 100 * float(np.nanmean(acc)) if acc else np.nan,
                     "changes_per_min_during_holds": chat / max(hold_time / 60, 1e-9),
                     "off_centre_while_relaxed_%": 100 * float(np.nanmean(false)) if false else np.nan})
    df = pd.DataFrame(rows)
    df.to_csv(out / "11_replay_by_profile.csv", index=False)
    fig = plt.figure(figsize=style.TALL)
    gs = fig.add_gridspec(len(profs) + 1, 4, height_ratios=[1] * len(profs) + [3.0])
    st = steps_with(s, "flex", "biceps")
    t_ex = float(st.iloc[min(4, len(st) - 1)]["t0"]) - 2 if not st.empty else b0[0]
    for i, prof in enumerate(profs):
        ax = fig.add_subplot(gs[i, :])
        t, slot = traces[prof]
        sel = (t >= t_ex) & (t < t_ex + 25)
        for _, r in flex[(flex["kind"] == "flex") & (flex["t1"] > t_ex) & (flex["t0"] < t_ex + 25)].iterrows():
            ax.axvspan(r["t0"] - t_ex, r["t1"] - t_ex, color=style.GRID, alpha=0.7, lw=0)
        ax.step(t[sel] - t_ex, slot[sel], where="post", color=style.profile_color(prof), lw=1.4)
        ax.set_ylim(-2.4, 2.4)
        ax.set_yticks([-2, 0, 2], ["L2", "C", "R2"])
        ax.set_ylabel(prof, rotation=0, ha="right", va="center", color=style.INK)
        if i < len(profs) - 1:
            ax.tick_params(labelbottom=False)
    ax.set_xlabel("seconds (shaded = flex cue)")
    metrics = [("onset_to_slot_ms", "cue → slot (ms)"), ("correct_side_during_holds_%", "correct side in holds (%)"),
               ("changes_per_min_during_holds", "changes / min in holds"), ("off_centre_while_relaxed_%", "off centre while relaxed (%)")]
    for j, (col, lab) in enumerate(metrics):
        ax = fig.add_subplot(gs[-1, j])
        ax.bar(df["profile"], df[col], color=[style.profile_color(p) for p in df["profile"]], width=0.6,
               edgecolor=style.SURFACE, lw=2)
        ax.set_title(lab, fontsize=9)
        ax.tick_params(axis="x", labelsize=8, rotation=30)
        ax.grid(axis="x", visible=False)
    finish(fig, out / "11_replay_by_profile.png", "The same recording through every filter profile (E4, offline)",
           "biceps and triceps blocks replayed with each profile's own calibration; differences come from the processing only", s)
    return {"replay_by_profile": df.round(2).to_dict(orient="records")}


# --- 14 ---------------------------------------------------------------------------------------
def fig_fatigue(s: Session, out: Path) -> dict:
    fig, ax = plt.subplots(figsize=style.WIDE)
    res = {}
    for i, m in enumerate(MUSCLES):
        pts = []
        blk = s.block(m)
        if blk is None:
            continue
        for _, r in steps_with(s, "flex", m, "strong").iterrows():
            g = s.raw_between(r["t0"] + 1.0, r["t1"] - 0.3)
            g = g[np.isfinite(g).all(1)]
            if len(g) < s.fs:
                continue
            y = filtered(s, g)[int(0.2 * s.fs):, i]
            f, p = psd_welch(y, s.fs, segment_s=0.5)
            pts.append(((r["t0"] - blk[0]) / 60, float(median_frequency(f, p))))
        if len(pts) < 3:
            continue
        tt, mdf = np.array(pts).T
        slope, icpt, rv, pv, _ = stats.linregress(tt, mdf)
        res[f"{m}_mdf_slope_hz_per_min"] = round(float(slope), 2)
        res[f"{m}_mdf_p"] = round(float(pv), 4)
        ax.plot(tt, mdf, "o", color=style.MUSCLE[m], ms=6, label=f"{m}: {slope:+.1f} Hz/min (p = {pv:.2f})")
        ax.plot(tt, icpt + slope * tt, color=style.MUSCLE[m], lw=1.2)
    if not res:
        raise Skip("needs at least 3 strong holds per muscle (the 5-minute contraction blocks have 15)")
    ax.set_xlabel("time in the contraction block (min)")
    ax.set_ylabel("median frequency of each strong hold (Hz)")
    ax.legend()
    finish(fig, out / "14_fatigue.png", "Fatigue: median frequency over each 5-minute contraction block (E7)",
           "a falling median frequency is the classic EMG sign of fatigue", s)
    return res


# --- 15 ---------------------------------------------------------------------------------------
def fig_latency(s: Session, out: Path) -> dict:
    cfg = cfg_of(s)
    rest = s.raw_between(*need_block(s, "rest"))
    rest = rest[np.isfinite(rest).all(1)]
    y_rest = filtered(s, rest)[int(s.fs):]
    w = int(0.025 * s.fs)
    onset_lat = []
    for i, m in enumerate(MUSCLES):
        mu_sd = []
        for v in (y_rest[:, i],):
            e = np.sqrt(np.convolve(v ** 2, np.ones(w) / w, mode="same"))
            mu_sd = (e.mean(), e.std())
        want = 1 if m == "biceps" else -1
        for _, r in steps_with(s, "flex", m).iterrows():
            g = s.raw_between(r["t0"] - 1.0, r["t0"] + 2.0)
            if len(g) < 2 * s.fs or not np.isfinite(g).all():
                continue
            y = signal.sosfiltfilt(design_chain(s.fs, profile_settings(cfg, "full")["filters"]), g[:, i] - g[0, i])
            e = np.sqrt(np.convolve(y ** 2, np.ones(w) / w, mode="same"))
            above = e > mu_sd[0] + 3 * mu_sd[1]
            run = int(0.025 * s.fs)
            idx = next((j for j in range(int(0.5 * s.fs), len(above) - run) if above[j:j + run].all()), None)
            if idx is None:
                continue
            t_on = r["t0"] - 1.0 + idx / s.fs
            ev = s.env_between(t_on, t_on + 1.5)
            hit = ev[np.sign(ev["slot"]) == want]
            if not hit.empty:
                onset_lat.append((float(hit["t"].iloc[0]) - t_on) * 1000)
    rtt = s.latency["rtt_ms"].to_numpy(dtype=float) if not s.latency.empty else np.array([])
    if not onset_lat and not len(rtt):
        raise Skip("no flex steps and no round trips")
    fig = plt.figure(figsize=style.WIDE)
    gs = fig.add_gridspec(2, 2)
    both = np.concatenate([np.asarray(onset_lat, dtype=float), rtt])
    edges = np.histogram_bin_edges(both, bins=50, range=(0, max(50.0, np.percentile(both, 99) * 1.1)))
    a_on = fig.add_subplot(gs[0, 0])
    a_rt = fig.add_subplot(gs[1, 0], sharex=a_on)
    for ax, data, col, lab in ((a_on, onset_lat, style.SERIES[0], "muscle onset → slot decided (bridge)"),
                               (a_rt, rtt, style.SERIES[1], "slot sent → keeper moved (Unity round trip)")):
        if len(data):
            ax.hist(data, bins=edges, color=col, edgecolor=style.SURFACE, lw=1)
            ax.axvline(np.median(data), color=style.INK, lw=1, ls="--")
        ax.set_title(f"{lab}: median {np.median(data):.0f} ms" if len(data) else f"{lab}: no data", fontsize=10)
        ax.set_ylabel("count")
    a_rt.set_xlabel("ms")
    a2 = fig.add_subplot(gs[:, 1])
    _, gd = frequency_response(design_chain(s.fs, profile_settings(cfg, "full")["filters"]), s.fs, np.array([99.0, 100.0]))
    parts = [("filters (at 100 Hz)", float(gd[-1] * 1000)), ("RMS window / 2", cfg["envelope"]["rms_window_ms"] / 2),
             ("hop / 2", cfg["envelope"]["hop_ms"] / 2), ("dwell", cfg["decision"]["dwell_s"] * 1000)]
    if len(rtt):
        parts.append(("Unity round trip (median)", float(np.median(rtt))))
    left = 0.0
    for j, (lab, v) in enumerate(parts):
        a2.barh([0], [v], left=left, color=style.SERIES[j % len(style.SERIES)], edgecolor=style.SURFACE, lw=2, label=f"{lab}: {v:.0f} ms")
        left += v
    if onset_lat:
        a2.axvline(np.median(onset_lat), color=style.INK, ls="--", lw=1.2, label=f"measured onset → slot (median {np.median(onset_lat):.0f} ms)")
    a2.set_yticks([])
    a2.set_xlabel("ms")
    a2.set_title(f"Budget: about {left:.0f} ms in total (target < 250 ms)")
    a2.legend(loc="upper center", bbox_to_anchor=(0.5, -0.15), ncol=2, fontsize=8)
    a2.grid(axis="y", visible=False)
    finish(fig, out / "15_latency.png", "Latency: from the muscle to the keeper (E8)",
           "onset = zero-phase envelope above rest mean + 3 SD for 25 ms; slot = the bridge's live decision", s)
    return {"onset_to_slot_ms_median": round(float(np.median(onset_lat)), 1) if onset_lat else None,
            "round_trip_ms_median": round(float(np.median(rtt)), 1) if len(rtt) else None,
            "budget_ms": {lab: round(v, 1) for lab, v in parts}}


# --- driver -----------------------------------------------------------------------------------
FIGURES: list[tuple[str, Callable[[Session, Path], dict]]] = [
    ("01 overview", fig_overview), ("02 raw vs filtered", fig_raw_vs_filtered), ("03 spectra", fig_spectra),
    ("04 noise table", fig_noise_table), ("05 filter responses", fig_filter_responses), ("06 filter effect", fig_filter_effect),
    ("07 envelope trade-off", fig_envelope_tradeoff), ("08 random process", fig_random_process), ("09 crosstalk", fig_crosstalk),
    ("10 game by profile", fig_game_by_profile), ("11 replay by profile", fig_replay_by_profile), ("12 confusion", fig_confusion),
    ("13 reaction times", fig_reaction_times), ("14 fatigue", fig_fatigue), ("15 latency", fig_latency),
]


def finish(fig, path: Path, title: str, note: str, s: Session) -> str:
    return style.finish(fig, path, f"{title}", note, s.simulated)


def analyse(session_dir: str | Path, out_root: Path, only: list[str] | None = None, log=print) -> dict:
    style.apply()
    s = load(session_dir)
    out = out_root / s.name
    out.mkdir(parents=True, exist_ok=True)
    summary: dict[str, Any] = {"session": s.name, "participant": s.participant, "simulated": s.simulated,
                               "figures": {}, "skipped": {}, "errors": {}}
    for name, fn in FIGURES:
        if only and not any(name.startswith(o) for o in only):
            continue
        try:
            summary["figures"][name] = fn(s, out)
            log(f"  ok    {name}")
        except Skip as e:
            summary["skipped"][name] = str(e)
            log(f"  skip  {name}: {e}")
        except Exception as e:  # noqa: BLE001 - one bad figure must not stop the rest
            summary["errors"][name] = f"{type(e).__name__}: {e}"
            log(f"  ERROR {name}: {type(e).__name__}: {e}")
            traceback.print_exc()
    (out / "summary.json").write_text(json.dumps(summary, indent=2, default=_jd) + "\n", encoding="utf-8")
    (out / "summary.md").write_text(summary_md(summary), encoding="utf-8")
    return summary


def _jd(o):
    if isinstance(o, (np.integer,)):
        return int(o)
    if isinstance(o, (np.floating,)):
        return None if np.isnan(o) else float(o)
    return str(o)


def md_table(df: pd.DataFrame) -> str:
    def cell(v):
        return f"{v:.2f}" if isinstance(v, float) else str(v)
    head = "| " + " | ".join(map(str, df.columns)) + " |"
    rule = "|" + "|".join("---" for _ in df.columns) + "|"
    body = ["| " + " | ".join(cell(v) for v in row) + " |" for row in df.itertuples(index=False)]
    return "\n".join([head, rule, *body])


def summary_md(sm: dict) -> str:
    lines = [f"# {sm['participant']} — {sm['session']}", ""]
    if sm["simulated"]:
        lines += ["**SIMULATED DATA — for checking the analysis only.**", ""]
    for name, d in sm["figures"].items():
        lines.append(f"## {name}")
        for k, v in (d or {}).items():
            if isinstance(v, list) and v and isinstance(v[0], dict):
                lines.append(md_table(pd.DataFrame(v)))
            else:
                lines.append(f"- **{k}**: {v}")
        lines.append("")
    if sm["skipped"]:
        lines += ["## Skipped", *[f"- {k}: {v}" for k, v in sm["skipped"].items()], ""]
    if sm["errors"]:
        lines += ["## Errors", *[f"- {k}: {v}" for k, v in sm["errors"].items()], ""]
    return "\n".join(lines) + "\n"


def combined(summaries: list[dict], out: Path) -> None:
    """Participants side by side: the game-by-profile numbers."""
    rows = []
    for sm in summaries:
        for r in (sm["figures"].get("10 game by profile") or {}).get("game_by_profile", []):
            rows.append({"participant": sm["participant"], **r})
    if not rows:
        return
    df = pd.DataFrame(rows)
    out.mkdir(parents=True, exist_ok=True)
    df.to_csv(out / "game_by_profile_all.csv", index=False)
    style.apply()
    metrics = [("save_%", "saves (%)"), ("reaction_time_median_ms", "reaction time (ms)"),
               ("wrong_direction_%", "wrong way first (%)"), ("slot_changes_per_min", "keeper moves / min")]
    fig, axes = plt.subplots(1, len(metrics), figsize=style.WIDE)
    profs = ordered(df["profile"])
    for ax, (col, lab) in zip(axes, metrics):
        g = df.groupby("profile", sort=False)[col]
        mean, sd = g.mean().reindex(profs), g.std().reindex(profs)
        ax.bar(profs, mean, yerr=sd.fillna(0), color=[style.profile_color(p) for p in profs], width=0.6,
               edgecolor=style.SURFACE, lw=2, capsize=4, ecolor=style.INK_2)
        for p_i, p in enumerate(profs):
            pts = df[df["profile"] == p][col].to_numpy()
            ax.scatter(np.full(len(pts), p_i), pts, color=style.INK, s=12, zorder=5)
        ax.set_title(lab, fontsize=10)
        ax.grid(axis="x", visible=False)
    sim = any(sm["simulated"] for sm in summaries)
    style.finish(fig, out / "game_by_profile_all.png", f"All participants (n = {df['participant'].nunique()}): filter profile vs game",
                 "bars = mean ± SD across participants; dots = participants", sim)


def main(argv=None) -> int:
    a = argparse.ArgumentParser(description="Figures and numbers for the video, from session folders")
    a.add_argument("sessions", nargs="+")
    a.add_argument("--out", default=str(Path(__file__).resolve().parents[2] / "figures"))
    a.add_argument("--only", help="comma-separated figure numbers, e.g. 03,10")
    args = a.parse_args(argv)
    out = Path(args.out)
    only = [x.strip() for x in args.only.split(",")] if args.only else None
    sums = []
    for d in args.sessions:
        print(d)
        sums.append(analyse(d, out, only))
    if len(sums) > 1:
        combined(sums, out / "combined")
    print(f"figures in {out}")
    return 0 if not any(sm["errors"] for sm in sums) else 1


if __name__ == "__main__":
    sys.exit(main())
