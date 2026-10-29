"""The analysis runs end to end on a simulated session (tools/make_sim_session.py)."""

import ast
import json
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))

from analysis import figures  # noqa: E402
from analysis.session import load  # noqa: E402
from emg_bridge.config import load_config  # noqa: E402
from make_sim_session import make_session  # noqa: E402


@pytest.fixture(scope="module")
def session_dir(tmp_path_factory):
    root = tmp_path_factory.mktemp("sessions")
    return make_session(load_config(), root, "simtest", short=True, seed=3, artifacts_per_min=4, quiet=True)


def test_session_loads_with_blocks_and_profiles(session_dir):
    s = load(session_dir)
    assert s.simulated and s.fs == 2000
    assert set(s.blocks()) >= {"rest", "biceps", "triceps", "game_full", "game_raw", "game_bandpass"}
    assert set(s.shots["profile"]) == {"full", "raw", "bandpass"}
    assert len(s.latency) > 5 and s.cal is not None and len(s.cal.names) == 5


def test_every_figure_is_made(session_dir, tmp_path):
    sm = figures.analyse(session_dir, tmp_path, log=lambda *_: None)
    assert not sm["errors"], sm["errors"]
    assert set(sm["skipped"]) <= {"14 fatigue"}, sm["skipped"]   # 30 s blocks hold too few strong holds
    out = tmp_path / Path(session_dir).name
    pngs = sorted(p.name for p in out.glob("*.png"))
    assert len(pngs) == 15 - len(sm["skipped"])
    md = (out / "summary.md").read_text()
    assert "SIMULATED" in md and "game_by_profile" not in md  # tables, not raw keys
    js = json.loads((out / "summary.json").read_text())
    assert js["figures"]["10 game by profile"]["game_by_profile"][0]["profile"] == "full"
    figures.combined([sm, sm], tmp_path / "combined")
    assert (tmp_path / "combined" / "game_by_profile_all.png").exists()


def test_a_session_without_the_protocol_skips_cleanly(tmp_path):
    from emg_bridge.main import main
    from test_main import temp_config
    cfg, root = temp_config(tmp_path)
    assert main(["record", "--config", str(cfg), "--no-monitor", "--duration", "1.5"]) == 0
    (d,) = root.iterdir()
    sm = figures.analyse(d, tmp_path / "figs", log=lambda *_: None)
    assert not sm["errors"], sm["errors"]
    assert "01 overview" in sm["figures"] and "03 spectra" in sm["skipped"]


def test_notebook_cells_are_valid_python():
    nb = json.loads((Path(__file__).resolve().parents[1] / "analysis" / "explore.ipynb").read_text())
    for c in nb["cells"]:
        if c["cell_type"] == "code":
            ast.parse("".join(c["source"]))
