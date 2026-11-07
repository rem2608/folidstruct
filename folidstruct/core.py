"""Core logic: configuration, project naming and folder creation.

This module has no GTK dependency so it can be tested and reused on its own.

Files, inside ``$XDG_CONFIG_HOME/folidstruct`` (usually
``~/.config/folidstruct``):

* ``config.json`` – options and templates, edited by the manager window.
* ``counter``     – the next progressive number, as plain text.

The counter lives in its own file so that a manager window left open with
an old copy of the configuration can never roll the numbering back.
"""

import datetime
import fcntl
import json
import os
from contextlib import contextmanager
from dataclasses import dataclass, field
from pathlib import Path

from . import i18n
from .i18n import _
from .paths import COMMAND, config_dir, config_path

APP_ID = "io.github.rem2608.FolidStruct"
VERSION = "0.1.0"
CONFIG_VERSION = 1
COUNTER_SUFFIX = "#"
MAX_NAME_BYTES = 255


class AppError(Exception):
    """An error with a message that can be shown to the user."""


# --- Paths -------------------------------------------------------------------

def counter_path():
    return config_dir() / "counter"


@contextmanager
def _lock():
    """Serialize access to the counter between concurrent FolidStruct processes."""
    config_dir().mkdir(parents=True, exist_ok=True)
    with open(config_dir() / ".lock", "w") as fd:
        fcntl.flock(fd, fcntl.LOCK_EX)
        yield


def _write_atomic(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    with open(tmp, "w", encoding="utf-8") as f:
        f.write(text)
        f.flush()
        os.fsync(f.fileno())  # never leave an empty file after a power loss
    os.replace(tmp, path)


# --- Configuration -----------------------------------------------------------

@dataclass
class Template:
    name: str
    folders: list = field(default_factory=list)
    # When False the template follows the global options.
    custom_options: bool = False
    use_date: bool = True
    use_counter: bool = False


@dataclass
class Config:
    use_date: bool = True
    use_counter: bool = False
    language: str = i18n.AUTO
    templates: list = field(default_factory=list)

    def options_for(self, template):
        """Return the default ``(use_date, use_counter)`` for ``template``."""
        if template.custom_options:
            return template.use_date, template.use_counter
        return self.use_date, self.use_counter

    def find_template(self, name):
        for template in self.templates:
            if template.name == name:
                return template
        return None

    def to_dict(self):
        return {
            "version": CONFIG_VERSION,
            "use_date": self.use_date,
            "use_counter": self.use_counter,
            "language": self.language,
            "templates": [
                {
                    "name": t.name,
                    "folders": list(t.folders),
                    "custom_options": t.custom_options,
                    "use_date": t.use_date,
                    "use_counter": t.use_counter,
                }
                for t in self.templates
            ],
        }

    @classmethod
    def from_dict(cls, data):
        templates = []
        for item in data.get("templates", []):
            if isinstance(item, dict) and isinstance(item.get("name"), str):
                folders = [f for f in item.get("folders", []) if isinstance(f, str)]
                templates.append(Template(
                    item["name"],
                    folders,
                    custom_options=bool(item.get("custom_options", False)),
                    use_date=bool(item.get("use_date", True)),
                    use_counter=bool(item.get("use_counter", False)),
                ))
        return cls(
            use_date=bool(data.get("use_date", True)),
            use_counter=bool(data.get("use_counter", False)),
            language=str(data.get("language", i18n.AUTO)),
            templates=templates,
        )


# The example template, in English; translated through i18n.
EXAMPLE_TEMPLATE = ("Graphics", ("Fonts", "Images", "Drafts", "Print"))


def example_template(language):
    name, folders = EXAMPLE_TEMPLATE
    return Template(i18n.translate(name, language),
                    [i18n.translate(f, language) for f in folders])


def default_config():
    return Config(templates=[example_template(i18n.current_language())])


def translate_example(config, language):
    """Translate the example template to ``language`` if it is still exactly
    as created, in any language. Templates edited by the user are kept."""
    target = example_template(language)
    if config.find_template(target.name):
        return False
    for template in config.templates:
        for other in ("en", *i18n.TRANSLATIONS):
            example = example_template(other)
            if template.name == example.name and template.folders == example.folders:
                template.name, template.folders = target.name, target.folders
                return True
    return False


def load_config():
    """Load the configuration, or the defaults if none has been saved yet."""
    try:
        data = json.loads(config_path().read_text(encoding="utf-8"))
    except FileNotFoundError:
        return default_config()
    except (ValueError, OSError) as error:
        raise AppError(_("The configuration file is damaged: {error}").format(error=error))
    if not isinstance(data, dict):
        raise AppError(_("The configuration file is damaged: {error}").format(error="not an object"))
    return Config.from_dict(data)


def save_config(config):
    _write_atomic(config_path(), json.dumps(config.to_dict(), indent=2, ensure_ascii=False) + "\n")


def ensure_config():
    """Write the default configuration if none exists yet."""
    if not config_path().exists():
        save_config(default_config())


# --- Counter -----------------------------------------------------------------

def read_counter():
    try:
        return max(1, int(counter_path().read_text().strip()))
    except (FileNotFoundError, ValueError):
        return 1


def write_counter(value):
    try:
        with _lock():
            _write_atomic(counter_path(), f"{max(1, int(value))}\n")
    except OSError as error:
        raise AppError(error.strerror or str(error))


# --- Naming ------------------------------------------------------------------

def validate_name(name):
    """Return an error message if ``name`` is not a valid folder name, else None."""
    if not name.strip():
        return _("The name cannot be empty")
    if "/" in name or "\0" in name:
        return _("The name cannot contain “/”")
    if name.strip() in (".", ".."):
        return _("“.” and “..” are not valid names")
    if len(name.encode("utf-8")) > MAX_NAME_BYTES:
        return _("The name is too long")
    return None


def build_project_name(name, date=None, counter=None):
    """Build the project folder name, e.g. ``2026-11-07 22# Logo``."""
    parts = []
    if date is not None:
        parts.append(date.isoformat())
    if counter is not None:
        parts.append(f"{counter}{COUNTER_SUFFIX}")
    parts.append(name.strip())
    return " ".join(parts)


# --- Creation ----------------------------------------------------------------

def create_project(parent, template, name, use_date, use_counter, today=None):
    """Create the project folder and its subfolders inside ``parent``.

    The progressive number is consumed only if the creation succeeds.
    Returns the path of the new project folder.
    """
    error = validate_name(name)
    if error:
        raise AppError(error)

    # Skip invalid names and duplicates (e.g. "Fonts" and "Fonts ").
    folders = list(dict.fromkeys(f.strip() for f in template.folders if not validate_name(f)))

    try:
        with _lock():
            counter = read_counter() if use_counter else None
            date = (today or datetime.date.today()) if use_date else None
            folder_name = build_project_name(name, date, counter)
            error = validate_name(folder_name)
            if error:
                raise AppError(error)

            target = Path(parent) / folder_name
            try:
                target.mkdir()
            except FileExistsError:
                raise AppError(
                    _("A folder named “{name}” already exists here").format(name=folder_name))

            try:
                for folder in folders:
                    (target / folder).mkdir()
            except OSError:
                # Undo: everything inside is ours and still empty.
                for folder in folders:
                    try:
                        (target / folder).rmdir()
                    except OSError:
                        pass
                target.rmdir()
                raise

            if use_counter:
                _write_atomic(counter_path(), f"{counter + 1}\n")
    except PermissionError:
        raise AppError(_("Permission denied"))
    except OSError as error:
        raise AppError(error.strerror or str(error))

    return target
