"""FolidStruct – context menu for GNOME Files (Nautilus), Nemo and Caja.

Adds a "FolidStruct" submenu listing the templates defined in FolidStruct.
Choosing one opens the dialog that asks for the project name and creates the
folders.

The three file managers share the same Python extension API, so this file
serves all of them; install.sh sets ``FILE_MANAGER`` and copies it to:

* Nautilus 43+: ~/.local/share/nautilus-python/extensions/  (python3-nautilus)
* Nemo:         ~/.local/share/nemo-python/extensions/      (nemo-python)
* Caja:         ~/.local/share/caja-python/extensions/      (python3-caja)
"""

import importlib
import json
import os
import shutil
import subprocess
from pathlib import Path

# The python loader of each file manager loads its namespace itself, at the
# version it supports, so it must not be pinned here.
from gi.repository import Gio, GObject

# Replaced by install.sh.
FILE_MANAGER = "@FILE_MANAGER@"
COMMAND_PATH = "@COMMAND_PATH@"

if FILE_MANAGER.startswith("@"):
    FILE_MANAGER = "Nautilus"
FM = importlib.import_module(f"gi.repository.{FILE_MANAGER}")

_LABELS = {
    "it": {"Manage templates…": "Gestisci modelli…"},
}


def _config_path():
    base = os.environ.get("XDG_CONFIG_HOME") or os.path.expanduser("~/.config")
    return Path(base) / "folidstruct" / "config.json"


def _load_config():
    try:
        with open(_config_path(), encoding="utf-8") as f:
            data = json.load(f)
        return data if isinstance(data, dict) else {}
    except (OSError, ValueError):
        return {}


def _language(config):
    """The language chosen in FolidStruct, or the system language ("auto")."""
    choice = config.get("language")
    if choice in _LABELS or choice == "en":
        return choice
    for var in ("LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG"):
        value = os.environ.get(var)
        if value:
            return value.split(":")[0].split(".")[0].split("_")[0]
    return "en"


def _command_path():
    if not COMMAND_PATH.startswith("@") and os.access(COMMAND_PATH, os.X_OK):
        return COMMAND_PATH
    return shutil.which("folidstruct") or str(Path.home() / ".local/bin/folidstruct")


def _read_config():
    if not _config_path().exists():
        # First use after installation: let FolidStruct write the default templates.
        try:
            subprocess.run([_command_path(), "--init"], timeout=5, check=False)
        except (OSError, subprocess.SubprocessError):
            return {}
    return _load_config()


def _template_names(config):
    templates = config.get("templates", [])
    if not isinstance(templates, list):
        return []
    return [t["name"] for t in templates
            if isinstance(t, dict) and isinstance(t.get("name"), str)]


def _local_path(file_info):
    location = None
    if hasattr(file_info, "get_location"):
        location = file_info.get_location()
    if location is None:
        location = Gio.File.new_for_uri(file_info.get_uri())
    return location.get_path()


def _launch(*args):
    try:
        subprocess.Popen([_command_path(), *args], start_new_session=True)
    except OSError:
        pass


class FolidStructMenuProvider(GObject.GObject, FM.MenuProvider):
    # Nautilus 4 passes (files) and (folder); Nemo and Caja also pass the
    # window first. The last argument is always the one we need.

    def get_file_items(self, *args):
        files = args[-1]
        if len(files) != 1 or not files[0].is_directory():
            return []
        path = _local_path(files[0])
        return self._menu("File", path) if path else []

    def get_background_items(self, *args):
        path = _local_path(args[-1])
        return self._menu("Background", path) if path else []

    def _menu(self, kind, path):
        config = _read_config()
        labels = _LABELS.get(_language(config), {})
        top = FM.MenuItem(name=f"FolidStruct::{kind}", label="FolidStruct")
        submenu = FM.Menu()
        top.set_submenu(submenu)

        for i, name in enumerate(_template_names(config)):
            item = FM.MenuItem(name=f"FolidStruct::{kind}::Template{i}", label=name)
            item.connect("activate", lambda _item, n=name: _launch("--new", n, "--dir", path))
            submenu.append_item(item)

        manage = FM.MenuItem(name=f"FolidStruct::{kind}::Manage",
                             label=labels.get("Manage templates…", "Manage templates…"))
        manage.connect("activate", lambda _item: _launch())
        submenu.append_item(manage)
        return [top]
