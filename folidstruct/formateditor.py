"""Editor for the project folder name format: separator and blocks.

The same editor is used in Preferences (global format) and in each template
(custom format); it only needs functions to add and remove its rows.
"""

import copy
import datetime

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Adw", "1")
from gi.repository import Adw, Gio, GLib, Gtk  # noqa: E402

from . import core  # noqa: E402
from .i18n import _  # noqa: E402


def _separator_labels():
    return [_("Space"), _("Underscore (_)"), _("Hyphen (-)"), _("Spaced hyphen ( - )")]


def _kind_title(kind):
    return {
        core.DATE: _("Date"),
        core.COUNTER: _("Progressive number"),
        core.NAME: _("Project name"),
        core.TEXT: _("Fixed text"),
        core.FIELD: _("Field to fill in"),
        core.CHOICE: _("Choice from a list"),
    }[kind]


def _icon_button(icon, tooltip, callback):
    button = Gtk.Button(icon_name=icon, tooltip_text=tooltip, valign=Gtk.Align.CENTER,
                        css_classes=["flat"])
    button.connect("clicked", lambda *_a: callback())
    return button


def _parse_options(text):
    return list(dict.fromkeys(o.strip() for o in text.split(",") if o.strip()))


def edit_block(parent, block, on_save):
    """Ask for the settings of a custom block; ``on_save`` gets the result."""
    dialog = Adw.AlertDialog(heading=_kind_title(block.kind))
    group = Adw.PreferencesGroup()
    rows = {}

    def entry(key, title, text):
        rows[key] = Adw.EntryRow(title=title, text=text)
        rows[key].connect("changed", lambda *_a: validate())
        group.add(rows[key])

    if block.kind == core.TEXT:
        dialog.set_body(_("Always added to the name, e.g. a studio or language code."))
        entry("value", _("Text"), block.value)
    else:
        dialog.set_body(_("Asked when creating the project; left empty, it is skipped.")
                        if block.kind == core.FIELD else
                        _("Picked from a menu when creating the project, so names stay "
                          "consistent; it can also be left empty."))
        entry("label", _("Label, e.g. Client"), block.label)
        if block.kind == core.CHOICE:
            entry("options", _("Values, separated by commas"), ", ".join(block.options))
        entry("value", _("Default value (optional)"), block.value)

    dialog.set_extra_child(group)
    dialog.add_response("cancel", _("Cancel"))
    dialog.add_response("save", _("Save"))
    dialog.set_response_appearance("save", Adw.ResponseAppearance.SUGGESTED)
    dialog.set_default_response("save")
    dialog.set_close_response("cancel")

    def result():
        text = {key: row.get_text().strip() for key, row in rows.items()}
        options = _parse_options(text.get("options", ""))
        return text, options

    def validate():
        text, options = result()
        ok = all("/" not in t for t in text.values()) and all("/" not in o for o in options)
        if block.kind == core.TEXT:
            ok = ok and bool(text["value"])
        else:
            ok = ok and bool(text["label"])
        if block.kind == core.CHOICE:
            ok = ok and bool(options) and (not text["value"] or text["value"] in options)
        dialog.set_response_enabled("save", ok)

    def on_response(_dialog, response):
        if response != "save":
            return
        text, options = result()
        block.value = text["value"]
        block.label = text.get("label", "")
        block.options = options
        on_save(block)

    dialog.connect("response", on_response)
    validate()
    dialog.present(parent)


class FormatEditor:
    """Edits a NameFormat in place and calls ``on_change`` after every change.

    ``add_row`` and ``remove_row`` place the rows (e.g. in a PreferencesGroup
    or an ExpanderRow); ``parent`` is the widget dialogs are attached to;
    ``counter`` returns the next number to show, or is None for the global
    format, where every template has its own.
    """

    def __init__(self, fmt, add_row, remove_row, on_change, parent, counter=None):
        self.fmt = fmt
        self.add_row = add_row
        self.remove_row = remove_row
        self.on_change = on_change
        self.parent = parent
        self.counter = counter
        self.rows = []

        self.separator_row = Adw.ComboRow(
            title=_("Separator"), model=Gtk.StringList.new(_separator_labels()),
            selected=core.SEPARATORS.index(fmt.separator))
        self.separator_row.connect("notify::selected", self._on_separator)
        add_row(self.separator_row)

        # "Add block" with a menu of block kinds.
        actions = Gio.SimpleActionGroup()
        add_action = Gio.SimpleAction.new("add", GLib.VariantType.new("s"))
        add_action.connect("activate", lambda _a, kind: self._add(kind.get_string()))
        actions.add_action(add_action)
        menu = Gio.Menu()
        for kind in core.CUSTOM_BLOCKS:
            menu.append(_kind_title(kind), f"format.add::{kind}")
        add_button = Gtk.MenuButton(icon_name="list-add-symbolic", menu_model=menu,
                                    valign=Gtk.Align.CENTER, css_classes=["flat"],
                                    tooltip_text=_("Add block"))
        add_button.insert_action_group("format", actions)
        self.add_block_row = Adw.ActionRow(
            title=_("Add block"), subtitle=_("Fixed text, field to fill in or choice"),
            activatable_widget=add_button)
        self.add_block_row.add_suffix(add_button)

        self.example_row = Adw.ActionRow(title=_("Example"), css_classes=["property"])
        self._rebuild()

    # Rows

    def _rebuild(self):
        for row in self.rows:
            self.remove_row(row)
        self.rows = [self._block_row(i, b) for i, b in enumerate(self.fmt.blocks)]
        self.rows += [self.add_block_row, self.example_row]
        for row in self.rows:
            self.add_row(row)
        self._update_example()

    def _counter(self):
        return self.counter() if self.counter else 1

    def _subtitle(self, block):
        if block.kind == core.DATE:
            return datetime.date.today().isoformat()
        if block.kind == core.COUNTER:
            if self.counter:
                return f"{self._counter()}{core.COUNTER_SUFFIX}"
            return _("Each template counts on its own")
        if block.kind == core.NAME:
            return _("Typed when creating the project")
        if block.kind == core.TEXT:
            return block.value
        detail = _kind_title(block.kind)
        if block.kind == core.CHOICE:
            detail += ": " + ", ".join(block.options)
        if block.value:
            detail += " · " + _("default: {value}").format(value=block.value)
        return detail

    def _block_row(self, index, block):
        custom = block.kind in core.CUSTOM_BLOCKS
        title = block.label if block.kind in (core.FIELD, core.CHOICE) else _kind_title(block.kind)
        row = Adw.ActionRow(title=GLib.markup_escape_text(title),
                            subtitle=GLib.markup_escape_text(self._subtitle(block)))

        if block.kind in (core.DATE, core.COUNTER):
            switch = Gtk.Switch(active=block.enabled, valign=Gtk.Align.CENTER,
                                tooltip_text=_("Included by default"))
            switch.connect("notify::active", self._on_enabled, block)
            row.add_suffix(switch)
        if custom:
            edit = _icon_button("document-edit-symbolic", _("Edit"), lambda: self._edit(index))
            row.add_suffix(edit)
            row.set_activatable_widget(edit)

        up = _icon_button("go-up-symbolic", _("Move up"), lambda: self._move(index, -1))
        down = _icon_button("go-down-symbolic", _("Move down"), lambda: self._move(index, 1))
        up.set_sensitive(index > 0)
        down.set_sensitive(index < len(self.fmt.blocks) - 1)
        row.add_suffix(up)
        row.add_suffix(down)
        if custom:
            row.add_suffix(_icon_button("user-trash-symbolic", _("Remove"),
                                        lambda: self._remove(index)))
        return row

    def _update_example(self):
        self.example_row.set_subtitle(
            GLib.markup_escape_text(core.example_name(self.fmt, counter=self._counter())))

    def refresh(self):
        """Redraw, e.g. after the counter changed."""
        self._rebuild()

    # Changes

    def _changed(self, rebuild=True):
        self.on_change()
        if rebuild:
            self._rebuild()
        else:
            self._update_example()

    def _on_separator(self, *_args):
        self.fmt.separator = core.SEPARATORS[self.separator_row.get_selected()]
        self._changed(rebuild=False)

    def _on_enabled(self, switch, _pspec, block):
        block.enabled = switch.get_active()
        self._changed(rebuild=False)

    def _add(self, kind):
        def on_save(block):
            self.fmt.blocks.append(block)
            self._changed()
        edit_block(self.parent, core.Block(kind), on_save)

    def _edit(self, index):
        def on_save(block):
            self.fmt.blocks[index] = block
            self._changed()
        edit_block(self.parent, copy.deepcopy(self.fmt.blocks[index]), on_save)

    def _move(self, index, delta):
        blocks = self.fmt.blocks
        blocks[index], blocks[index + delta] = blocks[index + delta], blocks[index]
        self._changed()

    def _remove(self, index):
        del self.fmt.blocks[index]
        self._changed()
