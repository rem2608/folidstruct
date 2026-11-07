"""Locations shared by the core and the translations."""

import os
from pathlib import Path

COMMAND = "folidstruct"


def config_dir():
    base = os.environ.get("XDG_CONFIG_HOME") or os.path.expanduser("~/.config")
    return Path(base) / COMMAND


def config_path():
    return config_dir() / "config.json"
