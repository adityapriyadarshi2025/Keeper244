"""One look for every figure (slides for the video: 16:9, light surface, thin marks, recessive grid).

Colours come from a validated categorical palette, assigned in a fixed order and kept per entity:
the muscles always use the monitor's colours (biceps orange, triceps blue), and each filter profile
keeps its own colour in every figure.
"""

from __future__ import annotations

import textwrap

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

SURFACE = "#fcfcfb"
INK = "#0b0b0b"
INK_2 = "#52514e"
INK_3 = "#8a8985"
GRID = "#e4e3df"
SERIES = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948"]
MUSCLE = {"biceps": SERIES[1], "triceps": SERIES[0]}
PROFILE = {"full": SERIES[0], "bandpass": SERIES[1], "raw": SERIES[2], "smooth": SERIES[3], "fast": SERIES[4]}
REST = INK_3
SEQ = ["#cde2fb", "#9ec5f4", "#6da7ec", "#3987e5", "#256abf", "#184f95", "#0d366b"]  # one-hue blue ramp

WIDE = (12.0, 6.75)   # 16:9 slide
TALL = (12.0, 8.0)


def apply() -> None:
    plt.rcParams.update({
        "figure.facecolor": SURFACE, "axes.facecolor": SURFACE, "savefig.facecolor": SURFACE,
        "axes.edgecolor": INK_3, "axes.labelcolor": INK_2, "axes.titlecolor": INK, "axes.titleweight": "bold",
        "axes.titlesize": 12, "axes.labelsize": 10, "xtick.color": INK_2, "ytick.color": INK_2,
        "xtick.labelsize": 9, "ytick.labelsize": 9, "axes.grid": True, "grid.color": GRID, "grid.linewidth": 0.8,
        "axes.spines.top": False, "axes.spines.right": False, "legend.frameon": False, "legend.fontsize": 9,
        "lines.linewidth": 1.6, "font.size": 10, "figure.titlesize": 14, "figure.titleweight": "bold",
        "text.color": INK,
    })


def profile_color(name: str) -> str:
    """Fixed colour per profile; profiles added later in config.yaml take the remaining slots."""
    return PROFILE.get(name, SERIES[5 + sum(map(ord, name)) % 3])


def finish(fig, path, title: str, note: str | None = None, simulated: bool = False) -> str:
    fig.suptitle(title, x=0.01, ha="left")
    foot = []
    if simulated:
        foot.append("SIMULATED DATA (tools/make_sim_session.py): for checking the analysis only, not results")
    if note:
        foot.append(note)
    lines = []
    for f in foot:
        lines += textwrap.wrap(f, width=int(fig.get_figwidth() * 17))
    if lines:
        fig.text(0.01, 0.005, "\n".join(lines), ha="left", va="bottom", fontsize=8, color=INK_2, linespacing=1.4)
    bottom = (0.012 + 0.022 * len(lines)) * (6.75 / fig.get_figheight()) if lines else 0
    fig.tight_layout(rect=(0, bottom, 1, 0.96))
    fig.savefig(path, dpi=150)
    plt.close(fig)
    return str(path)
