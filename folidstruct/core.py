"""Core logic: configuration, project naming and folder creation.

This module has no GTK dependency so it can be tested and reused on its own.

Files, inside ``$XDG_CONFIG_HOME/folidstruct`` (usually
``~/.config/folidstruct``):

* ``config.json``   – options and templates, edited by the manager window.
* ``counters.json`` – the next progressive number of each template.

The counters live in their own file so that a manager window left open with
an old copy of the configuration can never roll the numbering back.
"""

import copy
import datetime
import fcntl
import json
import os
import uuid
from contextlib import contextmanager
from dataclasses import dataclass, field
from pathlib import Path

from . import i18n
from .i18n import _
from .paths import COMMAND, config_dir, config_path  # noqa: F401 (COMMAND re-exported)

APP_ID = "io.github.rem2608.FolidStruct"
VERSION = "0.2.0"
CONFIG_VERSION = 2
COUNTER_SUFFIX = "#"
MAX_NAME_BYTES = 255


class AppError(Exception):
    """An error with a message that can be shown to the user."""


# --- Files -------------------------------------------------------------------

def counters_path():
    return config_dir() / "counters.json"


def _legacy_counter_path():
    """The single shared counter used by version 1 of the configuration."""
    return config_dir() / "counter"


@contextmanager
def _lock():
    """Serialize writes between concurrent FolidStruct processes.

    Not reentrant: code holding the lock must not call a function that takes it.
    """
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


def _write_json(path, data):
    _write_atomic(path, json.dumps(data, indent=2, ensure_ascii=False) + "\n")


# --- Name format -------------------------------------------------------------

# Built-in blocks: always present exactly once, can be moved but not removed.
DATE, COUNTER, NAME = "date", "counter", "name"
# Custom blocks, added by the user.
TEXT, FIELD, CHOICE = "text", "field", "choice"

BUILTIN_BLOCKS = (DATE, COUNTER, NAME)
CUSTOM_BLOCKS = (TEXT, FIELD, CHOICE)
SEPARATORS = (" ", "_", "-", " - ")


@dataclass
class Block:
    kind: str
    enabled: bool = True  # date and counter: on by default when creating
    label: str = ""       # field and choice: shown next to the input
    value: str = ""       # text: the text; field and choice: the default value
    options: list = field(default_factory=list)  # choice: the values to pick from

    def to_dict(self):
        data = {"kind": self.kind}
        if self.kind in (DATE, COUNTER):
            data["enabled"] = self.enabled
        if self.kind in (FIELD, CHOICE):
            data["label"] = self.label
        if self.kind in (TEXT, FIELD, CHOICE):
            data["value"] = self.value
        if self.kind == CHOICE:
            data["options"] = list(self.options)
        return data

    @classmethod
    def from_dict(cls, data):
        if not isinstance(data, dict) or data.get("kind") not in BUILTIN_BLOCKS + CUSTOM_BLOCKS:
            return None
        options = data.get("options", [])
        return cls(
            kind=data["kind"],
            enabled=bool(data.get("enabled", data["kind"] != COUNTER)),
            label=str(data.get("label", "")),
            value=str(data.get("value", "")),
            options=[str(o) for o in options] if isinstance(options, list) else [],
        )


def default_blocks(use_date=True, use_counter=False):
    return [Block(DATE, enabled=use_date), Block(COUNTER, enabled=use_counter), Block(NAME)]


@dataclass
class NameFormat:
    """How the project folder name is built: blocks joined by a separator."""

    separator: str = " "
    blocks: list = field(default_factory=default_blocks)

    def block(self, kind):
        for block in self.blocks:
            if block.kind == kind:
                return block
        return None

    def copy(self):
        return copy.deepcopy(self)

    def to_dict(self):
        return {"separator": self.separator, "blocks": [b.to_dict() for b in self.blocks]}

    @classmethod
    def from_dict(cls, data):
        if not isinstance(data, dict):
            return cls()
        separator = data.get("separator", " ")
        blocks = []
        for item in data.get("blocks", []) if isinstance(data.get("blocks"), list) else []:
            block = Block.from_dict(item)
            # Keep only the first copy of each built-in block.
            if block and not (block.kind in BUILTIN_BLOCKS
                              and any(b.kind == block.kind for b in blocks)):
                blocks.append(block)
        # Restore any missing built-in block, in its default position.
        kinds = [b.kind for b in blocks]
        if DATE not in kinds:
            blocks.insert(0, Block(DATE))
        if COUNTER not in kinds:
            date_index = next(i for i, b in enumerate(blocks) if b.kind == DATE)
            blocks.insert(date_index + 1, Block(COUNTER, enabled=False))
        if NAME not in kinds:
            blocks.append(Block(NAME))
        return cls(separator=separator if separator in SEPARATORS else " ", blocks=blocks)


def build_project_name(fmt, name, date=None, counter=None, values=None):
    """Build the project folder name, e.g. ``2026-11-07 22# Rossi Logo``.

    ``date`` and ``counter`` are left out when None. ``values`` maps the index
    of a field or choice block to what was entered; blocks missing from it use
    their default value. Empty blocks are skipped.
    """
    values = values or {}
    parts = []
    for index, block in enumerate(fmt.blocks):
        if block.kind == DATE:
            part = date.isoformat() if date is not None else ""
        elif block.kind == COUNTER:
            part = f"{counter}{COUNTER_SUFFIX}" if counter is not None else ""
        elif block.kind == NAME:
            part = name
        elif block.kind == TEXT:
            part = block.value
        else:
            part = values.get(index, block.value)
        part = " ".join(part.split())  # no line breaks or double spaces
        if part:
            parts.append(part)
    return fmt.separator.join(parts)


def example_name(fmt, today=None, counter=1):
    """A sample name for previews; fields without a default show their label."""
    values = {i: b.value or f"‹{b.label}›"
              for i, b in enumerate(fmt.blocks) if b.kind in (FIELD, CHOICE)}
    return build_project_name(
        fmt, _("Project"),
        date=(today or datetime.date.today()) if fmt.block(DATE).enabled else None,
        counter=counter if fmt.block(COUNTER).enabled else None,
        values=values,
    )


# --- Configuration -----------------------------------------------------------

def _new_id():
    return uuid.uuid4().hex


@dataclass
class Template:
    name: str
    folders: list = field(default_factory=list)
    # When False the template follows the global name format.
    custom_format: bool = False
    format: NameFormat = field(default_factory=NameFormat)
    # Stable identity, so the counter survives renaming.
    id: str = field(default_factory=_new_id)


@dataclass
class Config:
    format: NameFormat = field(default_factory=NameFormat)
    language: str = i18n.AUTO
    templates: list = field(default_factory=list)

    def format_for(self, template):
        return template.format if template.custom_format else self.format

    def find_template(self, name):
        for template in self.templates:
            if template.name == name:
                return template
        return None

    def to_dict(self):
        return {
            "version": CONFIG_VERSION,
            "language": self.language,
            "format": self.format.to_dict(),
            "templates": [
                {
                    "id": t.id,
                    "name": t.name,
                    "folders": list(t.folders),
                    "custom_format": t.custom_format,
                    "format": t.format.to_dict(),
                }
                for t in self.templates
            ],
        }

    @classmethod
    def from_dict(cls, data):
        """Read a configuration, converting version 1 on the fly."""

        def read_format(item):
            if "format" in item:
                return NameFormat.from_dict(item["format"])
            # Version 1: only the date and counter switches.
            return NameFormat(blocks=default_blocks(bool(item.get("use_date", True)),
                                                    bool(item.get("use_counter", False))))

        templates = []
        for item in data.get("templates", []):
            if isinstance(item, dict) and isinstance(item.get("name"), str):
                templates.append(Template(
                    item["name"],
                    [f for f in item.get("folders", []) if isinstance(f, str)],
                    custom_format=bool(item.get("custom_format",
                                                item.get("custom_options", False))),
                    format=read_format(item),
                    id=str(item.get("id") or _new_id()),
                ))
        return cls(
            format=read_format(data),
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


def new_template(config, name):
    """A new template, starting from a copy of the global name format."""
    return Template(name, format=config.format.copy())


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


def _read_config_data():
    try:
        data = json.loads(config_path().read_text(encoding="utf-8"))
    except FileNotFoundError:
        return None
    except (ValueError, OSError) as error:
        raise AppError(_("The configuration file is damaged: {error}").format(error=error))
    if not isinstance(data, dict):
        raise AppError(_("The configuration file is damaged: {error}").format(error="not an object"))
    return data


def _needs_upgrade(data):
    return (data.get("version", 1) < CONFIG_VERSION
            or any(isinstance(t, dict) and not t.get("id") for t in data.get("templates", [])))


def load_config():
    """Load the configuration, or the defaults if none has been saved yet.

    An older configuration is converted and saved once, so that template ids
    (and with them the counters) stay the same from then on.
    """
    data = _read_config_data()
    if data is None:
        return default_config()
    config = Config.from_dict(data)
    if not _needs_upgrade(data):
        return config
    try:
        with _lock():
            data = _read_config_data()  # another process may have upgraded it
            if data is not None:
                config = Config.from_dict(data)
                if _needs_upgrade(data):
                    _upgrade_counters(config)
                    save_config(config)
    except OSError:
        pass  # read-only configuration: use it as it is
    return config


def _upgrade_counters(config):
    """Version 1 had one counter for all templates: each template starts from it."""
    legacy = _legacy_counter_path()
    try:
        start = max(1, int(legacy.read_text().strip()))
    except (FileNotFoundError, ValueError):
        return
    counters = _read_counters()
    for template in config.templates:
        counters.setdefault(template.id, start)
    _write_json(counters_path(), counters)
    legacy.unlink()


def save_config(config):
    _write_json(config_path(), config.to_dict())


def ensure_config():
    """Write the default configuration if none exists yet."""
    if not config_path().exists():
        save_config(default_config())


# --- Counters ----------------------------------------------------------------

def _read_counters():
    try:
        data = json.loads(counters_path().read_text(encoding="utf-8"))
        return data if isinstance(data, dict) else {}
    except (FileNotFoundError, ValueError):
        return {}


def _counter_from(counters, template):
    try:
        return max(1, int(counters.get(template.id, 1)))
    except (TypeError, ValueError):
        return 1


def read_counter(template):
    """The next progressive number of ``template``."""
    return _counter_from(_read_counters(), template)


def write_counter(template, value):
    try:
        with _lock():
            counters = _read_counters()
            counters[template.id] = max(1, int(value))
            _write_json(counters_path(), counters)
    except OSError as error:
        raise AppError(error.strerror or str(error))


def forget_counter(template):
    """Drop the counter of a deleted template."""
    try:
        with _lock():
            counters = _read_counters()
            if counters.pop(template.id, None) is not None:
                _write_json(counters_path(), counters)
    except OSError:
        pass  # a leftover number is harmless


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


# --- Creation ----------------------------------------------------------------

def create_project(parent, template, name, fmt, use_date, use_counter,
                   values=None, today=None):
    """Create the project folder and its subfolders inside ``parent``.

    ``fmt`` is the name format to use and ``values`` the content of its field
    and choice blocks (see build_project_name). The template's progressive
    number is consumed only if the creation succeeds.
    Returns the path of the new project folder.
    """
    error = validate_name(name)
    if error:
        raise AppError(error)

    # Skip invalid names and duplicates (e.g. "Fonts" and "Fonts ").
    folders = list(dict.fromkeys(f.strip() for f in template.folders if not validate_name(f)))

    try:
        with _lock():
            counters = _read_counters()
            counter = _counter_from(counters, template) if use_counter else None
            date = (today or datetime.date.today()) if use_date else None
            folder_name = build_project_name(fmt, name, date, counter, values)
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
                counters[template.id] = counter + 1
                _write_json(counters_path(), counters)
    except PermissionError:
        raise AppError(_("Permission denied"))
    except OSError as error:
        raise AppError(error.strerror or str(error))

    return target
