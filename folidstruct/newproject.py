"""The small window opened from the Files context menu: asks for the project
name and creates the folder structure."""

import datetime
from pathlib import Path

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Adw", "1")
from gi.repository import Adw, Gio, GLib, Gtk  # noqa: E402

from . import core  # noqa: E402
from .i18n import _  # noqa: E402
from .ui import show_error  # noqa: E402


def _display_path(path):
    """Show the home folder as ~, like the file manager does."""
    try:
        return str(Path("~") / Path(path).relative_to(Path.home()))
    except ValueError:
        return str(path)


class NewProjectWindow(Adw.ApplicationWindow):
    def __init__(self, app, config, template, parent_dir):
        super().__init__(application=app, title=_("New project"), resizable=False)
        self.set_default_size(480, -1)
        self.config = config
        self.template = template
        self.parent_dir = parent_dir
        self.counter = 1
        self.today = datetime.date.today()
        self.field_rows = {}  # block index -> row

        cancel = Gtk.Button(label=_("Cancel"))
        cancel.connect("clicked", lambda *_a: self.close())
        self.create_button = Gtk.Button(label=_("Create"), css_classes=["suggested-action"])
        self.create_button.connect("clicked", self._on_create)

        header = Adw.HeaderBar(show_start_title_buttons=False, show_end_title_buttons=False)
        header.set_title_widget(Adw.WindowTitle(title=_("New project")))
        header.pack_start(cancel)
        header.pack_end(self.create_button)

        # Template and name
        names = [t.name for t in config.templates]
        self.template_row = Adw.ComboRow(title=_("Template"),
                                         model=Gtk.StringList.new(names),
                                         selected=config.templates.index(template))
        self.template_row.connect("notify::selected", self._on_template_changed)
        self.name_row = Adw.EntryRow(title=_("Project name"))
        self.name_row.connect("changed", self._update)
        self.name_row.connect("entry-activated", self._on_create)
        name_group = Adw.PreferencesGroup()
        name_group.add(self.template_row)
        name_group.add(self.name_row)

        # Fields and choices of the name format, rebuilt for each template
        self.fields_group = Adw.PreferencesGroup(title=_("Details"))

        # Options
        self.date_row = Adw.SwitchRow(title=_("Date"), subtitle=self.today.isoformat())
        self.counter_row = Adw.SwitchRow(title=_("Progressive number"))
        self.date_row.connect("notify::active", self._update)
        self.counter_row.connect("notify::active", self._update)
        options_group = Adw.PreferencesGroup(title=_("Options"))
        options_group.add(self.date_row)
        options_group.add(self.counter_row)

        # Preview
        self.result_row = Adw.ActionRow(title=_("Folder"), css_classes=["property"],
                                        subtitle_selectable=True)
        self.subfolders_row = Adw.ActionRow(title=_("Subfolders"), css_classes=["property"])
        location_row = Adw.ActionRow(title=_("Location"), subtitle=_display_path(parent_dir),
                                     css_classes=["property"])
        result_group = Adw.PreferencesGroup(title=_("Result"))
        for row in (self.result_row, self.subfolders_row, location_row):
            result_group.add(row)

        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=18,
                      margin_top=12, margin_bottom=18, margin_start=18, margin_end=18)
        for group in (name_group, self.fields_group, options_group, result_group):
            box.append(group)

        self.toasts = Adw.ToastOverlay(child=box)
        toolbar = Adw.ToolbarView(content=self.toasts)
        toolbar.add_top_bar(header)
        self.set_content(toolbar)

        escape = Gtk.ShortcutController()
        escape.add_shortcut(Gtk.Shortcut(
            trigger=Gtk.ShortcutTrigger.parse_string("Escape"),
            action=Gtk.CallbackAction.new(lambda *_a: self.close() or True),
        ))
        self.add_controller(escape)

        self._on_template_changed()
        self.name_row.grab_focus()

    def _on_template_changed(self, *_args):
        self.template = self.config.templates[self.template_row.get_selected()]
        self.format = self.config.format_for(self.template)
        # Fields and counter first: toggling the switches updates the preview.
        self._rebuild_fields()
        self._refresh_counter()
        self.date_row.set_active(self.format.block(core.DATE).enabled)
        self.counter_row.set_active(self.format.block(core.COUNTER).enabled)
        self.subfolders_row.set_subtitle(", ".join(self.template.folders) or _("No subfolders"))
        self._update()

    def _refresh_counter(self):
        self.counter = core.read_counter(self.template)
        self.counter_row.set_subtitle(f"{self.counter}{core.COUNTER_SUFFIX}")

    def _rebuild_fields(self):
        for row in self.field_rows.values():
            self.fields_group.remove(row)
        self.field_rows = {}
        for index, block in enumerate(self.format.blocks):
            if block.kind == core.FIELD:
                row = Adw.EntryRow(title=block.label, text=block.value)
                row.connect("changed", self._update)
                row.connect("entry-activated", self._on_create)
            elif block.kind == core.CHOICE:
                # The first entry leaves the block out of the name.
                options = ["—", *block.options]
                selected = options.index(block.value) if block.value in options else 0
                row = Adw.ComboRow(title=block.label, model=Gtk.StringList.new(options),
                                   selected=selected)
                row.connect("notify::selected", self._update)
            else:
                continue
            self.fields_group.add(row)
            self.field_rows[index] = row
        self.fields_group.set_visible(bool(self.field_rows))

    def _values(self):
        values = {}
        for index, row in self.field_rows.items():
            if isinstance(row, Adw.ComboRow):
                selected = row.get_selected()
                values[index] = self.format.blocks[index].options[selected - 1] if selected else ""
            else:
                values[index] = row.get_text()
        return values

    def _folder_name(self):
        return core.build_project_name(
            self.format,
            self.name_row.get_text() or _("Project"),
            date=self.today if self.date_row.get_active() else None,
            counter=self.counter if self.counter_row.get_active() else None,
            values=self._values(),
        )

    def _update(self, *_args):
        text = self.name_row.get_text()
        valid = bool(text.strip())
        for row in (self.name_row, *self.field_rows.values()):
            if isinstance(row, Adw.ComboRow):
                continue
            value = row.get_text()
            error = value and core.validate_name(value)
            if error:
                row.add_css_class("error")
                valid = False
            else:
                row.remove_css_class("error")
        folder_name = self._folder_name()
        valid = valid and not core.validate_name(folder_name)
        self.create_button.set_sensitive(valid)
        self.result_row.set_subtitle(GLib.markup_escape_text(folder_name))

    def _on_create(self, *_args):
        if not self.create_button.get_sensitive():
            return
        try:
            core.create_project(
                self.parent_dir, self.template, self.name_row.get_text(), self.format,
                use_date=self.date_row.get_active(),
                use_counter=self.counter_row.get_active(),
                values=self._values(),
                today=self.today,
            )
        except core.AppError as error:
            self.toasts.add_toast(Adw.Toast(title=str(error), timeout=5))
            # Another FolidStruct window may have used the number meanwhile.
            self._refresh_counter()
            self._update()
            return
        self.close()


def run(template_name, parent_dir):
    """Show the dialog. ``template_name`` preselects a template; if it is None
    or no longer exists, the first one is selected."""
    app = Adw.Application(application_id=core.APP_ID + ".New",
                          flags=Gio.ApplicationFlags.NON_UNIQUE)

    def on_activate(app):
        try:
            config = core.load_config()
        except core.AppError as error:
            show_error(app, _("Cannot create the project"), str(error))
            return
        if not config.templates:
            show_error(app, _("No templates"),
                        _("Open FolidStruct from the applications menu to create one."))
            return
        template = config.find_template(template_name) or config.templates[0]
        NewProjectWindow(app, config, template, Path(parent_dir)).present()

    app.connect("activate", on_activate)
    return app.run(None)
