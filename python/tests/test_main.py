"""The command line: run / record / replay write complete session folders."""

import json

import yaml

from emg_bridge.config import load_config
from emg_bridge.control.calibration import Calibration, ChannelCalibration
from emg_bridge.main import find_last_calibration, main


def sim_calibration() -> Calibration:
    def ch(m):
        return ChannelCalibration(m, 4e-6, 0.3e-6, 400e-6, 0.3 / 396, 0.10, 0.07, 0.365, 0.2, 0.52, True, True)
    return Calibration(ch("biceps"), ch("triceps"))


def temp_config(tmp_path):
    cfg = load_config()
    cfg["recording"]["root"] = str(tmp_path / "sessions")
    cfg["network"]["in_port"] = 0          # any free port: tests must not need 5006
    cfg["network"]["out_port"] = 59999
    path = tmp_path / "config.yaml"
    path.write_text(yaml.safe_dump(cfg))
    return path, tmp_path / "sessions"


def test_run_with_a_saved_calibration_streams_and_logs(tmp_path):
    cfg, root = temp_config(tmp_path)
    cal = sim_calibration().save(tmp_path / "cal.json")
    rc = main(["run", "--config", str(cfg), "--player", "auto", "--no-monitor", "--duration", "1.5",
               "--calibration", str(cal), "--participant", "P99"])
    assert rc == 0
    (session,) = root.iterdir()
    meta = json.loads((session / "meta.json").read_text())
    assert meta["mode"] == "play" and meta["stats"]["packets"] > 100 and meta["participant"] == "P99"
    assert (session / "calibration.json").exists()
    assert find_last_calibration(root, "P99") == session / "calibration.json"
    assert find_last_calibration(root, "P01") is None


def test_record_with_the_fake_bioradio_then_replay(tmp_path):
    cfg, root = temp_config(tmp_path)
    assert main(["record", "--config", str(cfg), "--source", "bioradio", "--fake-sdk", "--no-monitor",
                 "--duration", "1.5", "--participant", "fake"]) == 0
    (rec,) = root.iterdir()
    meta = json.loads((rec / "meta.json").read_text())
    assert meta["source"]["kind"] == "bioradio" and meta["source"]["details"]["sdk"] == "fake"
    assert (rec / "device_config.xml").exists()
    assert main(["replay", str(rec), "--config", str(cfg), "--speed", "0", "--no-monitor"]) == 0
    replays = [d for d in root.iterdir() if d != rec]
    assert len(replays) == 1
    a = (rec / "envelope.csv").read_text().splitlines()
    b = (replays[0] / "envelope.csv").read_text().splitlines()
    assert len(a) == len(b) and a[1].split(",")[2:4] == b[1].split(",")[2:4]  # same envelope from the same samples


def test_missing_calibration_is_a_clear_error(tmp_path, capsys):
    cfg, _ = temp_config(tmp_path)
    assert main(["run", "--config", str(cfg), "--calibration", "last", "--no-monitor", "--participant", "nobody"]) == 2
    assert "no calibration found for participant 'nobody'" in capsys.readouterr().err
