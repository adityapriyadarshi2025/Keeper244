"""Load python/config.yaml. The single place tunables are read from."""

from __future__ import annotations

from pathlib import Path
from typing import Any

import yaml

DEFAULT_CONFIG_PATH = Path(__file__).resolve().parent.parent / "config.yaml"


def load_config(path: str | Path | None = None) -> dict[str, Any]:
    """Return the parsed config as a nested dict."""
    cfg_path = Path(path) if path is not None else DEFAULT_CONFIG_PATH
    with cfg_path.open("r", encoding="utf-8") as f:
        cfg = yaml.safe_load(f)
    if not isinstance(cfg, dict):
        raise ValueError(f"{cfg_path} did not parse to a mapping")
    return cfg
