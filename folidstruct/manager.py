"""The main window: create and edit templates, set the global options."""

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Adw", "1")
from gi.repository import Adw, Gio, GLib, Gtk, Pango  # noqa: E402

from . import core, i18n  # noqa: E402
from .formateditor import FormatEditor  # noqa: E402
from .i18n import _  # noqa: E402
from .ui import show_error  # noqa: E402

LANGUAGE_CHOICES = [i18n.AUTO, *i18n.LANGUAGE_NAMES]


def _icon_button(icon, tooltip, callback, *args):
    button = Gtk.Button(icon_name=icon, tooltip_text=tooltip, valign=Gtk.Align.CENTER,
                        css_classes=["flat"])
    button.connect("clicked", lambda *_a: callback(*args))
    return button


def _unique(base, existing):
    name, n = base, 2
    while name in existing:
        name, n = f"{base} {n}", n + 1
    return name


def _set_error(widget, error):
    if error:
        widget.add_css_class("error")
        widget.set_tooltip_text(error)
    else:
        widget.remove_css_class("error")
        widget.set_tooltip_text(None)


class TemplateEditor(Adw.PreferencesPage):
    """Edits one template; every valid change is saved immediately."""

    def __init__(self, window, template):
        super().__init__()
        self.window = window
        self.template = template

        # Name
        self.name_row = Adw.EntryRow(title=_("Name"), text=template.name)
        self.name_row.connect("changed", self._on_name_changed)
        group = Adw.PreferencesGroup(title=_("Template"))
        group.add(self.name_row)
        self.add(group)

        # Project folder name: this template's counter and optional format
        self.next_row = Adw.SpinRow.new_with_range(1, 999999, 1)
        self.next_row.set_title(_("Next number"))
        self.next_row.set_subtitle(_("Each template counts on its own"))
        self.next_row.set_value(core.read_counter(template))
        self.next_row.connect("notify::value", self._on_counter_changed)

        self.options_row = Adw.ExpanderRow(
            title=_("Custom options"),
            subtitle=_("Use this name format instead of the global one"),
            show_enable_switch=True,
            enable_expansion=template.custom_format,
            expanded=template.custom_format,
        )
        self.options_row.connect("notify::enable-expansion", self._on_custom_toggled)
        self.format_editor = FormatEditor(
            template.format, self.options_row.add_row, self.options_row.remove,
            on_change=window.save, parent=self,
            counter=lambda: core.read_counter(self.template))

        group = Adw.PreferencesGroup(title=_("Project folder name"))
        group.add(self.next_row)
        group.add(self.options_row)
        self.add(group)

        # Folders
        self.folders_group = Adw.PreferencesGroup(title=_("Folders"))
        add = Gtk.Button(icon_name="list-add-symbolic", tooltip_text=_("Add folder"),
                         css_classes=["flat"])
        add.connect("clicked", self._on_add_folder)
        self.folders_group.set_header_suffix(add)
        self.add(self.folders_group)
        self.folder_rows = []
        self._rebuild_folders()

    # Name

    def _on_name_changed(self, row):
        name = row.get_text().strip()
        error = core.validate_name(name)
        if not error and any(t is not self.template and t.name == name
                             for t in self.window.config.templates):
            error = _("A template with this name already exists")
        _set_error(row, error)
        if not error and name != self.template.name:
            self.template.name = name
            self.window.on_template_renamed(self.template)
            self.window.save()

    # Project folder name

    def _on_counter_changed(self, *_args):
        try:
            core.write_counter(self.template, int(self.next_row.get_value()))
        except core.AppError as error:
            self.window.toasts.add_toast(Adw.Toast(title=str(error)))
        self.format_editor.refresh()

    def _on_custom_toggled(self, *_args):
        self.template.custom_format = self.options_row.get_enable_expansion()
        self.window.save()

    # Folders

    def _rebuild_folders(self, focus=None):
        for row in self.folder_rows:
            self.folders_group.remove(row)
        self.folder_rows = []
        count = len(self.template.folders)
        for i, folder in enumerate(self.template.folders):
            row = Adw.EntryRow(title=_("Folder {n}").format(n=i + 1), text=folder)
            row.connect("changed", self._on_folder_changed, i)
            up = _icon_button("go-up-symbolic", _("Move up"), self._move, i, -1)
            down = _icon_button("go-down-symbolic", _("Move down"), self._move, i, 1)
            up.set_sensitive(i > 0)
            down.set_sensitive(i < count - 1)
            row.add_suffix(up)
            row.add_suffix(down)
            row.add_suffix(_icon_button("user-trash-symbolic", _("Remove"), self._remove, i))
            self.folders_group.add(row)
            self.folder_rows.append(row)
        if focus is not None and focus < len(self.folder_rows):
            self.folder_rows[focus].grab_focus()

    def _on_folder_changed(self, row, index):
        name = row.get_text().strip()
        error = core.validate_name(name)
        if not error and any(j != index and f == name
                             for j, f in enumerate(self.template.folders)):
            error = _("This folder is already in the list")
        _set_error(row, error)
        if not error:
            self.template.folders[index] = name
            self.window.save()

    def _on_add_folder(self, *_args):
        self.template.folders.append(_unique(_("New folder"), self.template.folders))
        self.window.save()
        self._rebuild_folders(focus=len(self.template.folders) - 1)

    def _move(self, index, delta):
        folders = self.template.folders
        folders[index], folders[index + delta] = folders[index + delta], folders[index]
        self.window.save()
        self._rebuild_folders(focus=index + delta)

    def _remove(self, index):
        del self.template.folders[index]
        self.window.save()
        self._rebuild_folders()


class ManagerWindow(Adw.ApplicationWindow):
    def __init__(self, app, config):
        super().__init__(application=app, title="FolidStruct",
                         default_width=860, default_height=600)
        self.config = config

        # Sidebar
        add = Gtk.Button(icon_name="list-add-symbolic", tooltip_text=_("New template"))
        add.connect("clicked", self._on_new_template)
        menu = Gio.Menu()
        menu.append(_("Preferences"), "app.preferences")
        menu.append(_("About FolidStruct"), "app.about")
        sidebar_header = Adw.HeaderBar()
        sidebar_header.pack_start(add)
        sidebar_header.pack_end(Gtk.MenuButton(icon_name="open-menu-symbolic", menu_model=menu))

        self.list = Gtk.ListBox(css_classes=["navigation-sidebar"])
        self.list.connect("row-selected", self._on_row_selected)
        sidebar = Adw.ToolbarView(content=Gtk.ScrolledWindow(child=self.list,
                                                             vexpand=True))
        sidebar.add_top_bar(sidebar_header)

        # Content
        self.delete_button = Gtk.Button(icon_name="user-trash-symbolic",
                                        tooltip_text=_("Delete template"))
        self.delete_button.connect("clicked", self._on_delete_template)
        content_header = Adw.HeaderBar()
        content_header.pack_end(self.delete_button)

        create = Gtk.Button(label=_("Create template"), halign=Gtk.Align.CENTER,
                            css_classes=["pill", "suggested-action"])
        create.connect("clicked", self._on_new_template)
        self.empty_page = Adw.StatusPage(
            icon_name="folder-new-symbolic", title=_("No templates"),
            description=_("Create a template to define a folder structure"), child=create)
        self.content = Adw.ToolbarView(content=self.empty_page)
        self.content.add_top_bar(content_header)
        self.content_page = Adw.NavigationPage(title=_("Template"), child=self.content)

        self.split = Adw.NavigationSplitView(
            sidebar=Adw.NavigationPage(title="FolidStruct", child=sidebar),
            content=self.content_page,
        )
        self.toasts = Adw.ToastOverlay(child=self.split)
        self.set_content(self.toasts)

        breakpoint = Adw.Breakpoint(condition=Adw.BreakpointCondition.parse("max-width: 560sp"))
        breakpoint.add_setter(self.split, "collapsed", True)
        self.add_breakpoint(breakpoint)

        self._rebuild_list()

    # Persistence

    def save(self):
        try:
            core.save_config(self.config)
        except OSError as error:
            self.toasts.add_toast(Adw.Toast(title=str(error)))

    # Sidebar

    def _rebuild_list(self, select=None):
        self.list.remove_all()
        for template in self.config.templates:
            row = Gtk.ListBoxRow()
            row.set_child(Gtk.Label(label=template.name, xalign=0,
                                    ellipsize=Pango.EllipsizeMode.END))
            row.template = template
            self.list.append(row)
        if not self.config.templates:
            self._show_template(None)
            return
        index = self.config.templates.index(select) if select in self.config.templates else 0
        self.list.select_row(self.list.get_row_at_index(index))

    def _on_row_selected(self, _list, row):
        if row is not None:
            self._show_template(row.template)

    def _show_template(self, template):
        self.delete_button.set_visible(template is not None)
        if template is None:
            self.content.set_content(self.empty_page)
            self.content_page.set_title(_("Template"))
            return
        self.editor = TemplateEditor(self, template)
        self.content.set_content(self.editor)
        self.content_page.set_title(template.name)
        self.split.set_show_content(True)

    def on_template_renamed(self, template):
        row = self.list.get_selected_row()
        if row is not None and row.template is template:
            row.get_child().set_label(template.name)
        self.content_page.set_title(template.name)

    # Actions

    def _on_new_template(self, *_args):
        names = [t.name for t in self.config.templates]
        template = core.new_template(self.config, _unique(_("New template"), names))
        self.config.templates.append(template)
        self.save()
        self._rebuild_list(select=template)
        self.editor.name_row.grab_focus()

    def _on_delete_template(self, *_args):
        row = self.list.get_selected_row()
        if row is None:
            return
        template = row.template
        dialog = Adw.AlertDialog(
            heading=_("Delete “{name}”?").format(name=template.name),
            body=_("The template will be removed. Folders already created are not affected."),
        )
        dialog.add_response("cancel", _("Cancel"))
        dialog.add_response("delete", _("Delete"))
        dialog.set_response_appearance("delete", Adw.ResponseAppearance.DESTRUCTIVE)
        dialog.set_default_response("cancel")

        def on_response(_dialog, response):
            if response == "delete":
                self.config.templates.remove(template)
                self.save()
                core.forget_counter(template)
                self._rebuild_list()

        dialog.connect("response", on_response)
        dialog.present(self)


class PreferencesDialog(Adw.PreferencesDialog):
    def __init__(self, window):
        super().__init__(title=_("Preferences"))
        self.window = window
        config = window.config

        labels = [_("Automatic")] + list(i18n.LANGUAGE_NAMES.values())
        choice = config.language if config.language in LANGUAGE_CHOICES else i18n.AUTO
        self.language_row = Adw.ComboRow(
            title=_("Language"), subtitle=_("“Automatic” uses the system language"),
            model=Gtk.StringList.new(labels), selected=LANGUAGE_CHOICES.index(choice))
        self.language_row.connect("notify::selected", self._on_language_changed)
        language_group = Adw.PreferencesGroup()
        language_group.add(self.language_row)

        format_group = Adw.PreferencesGroup(
            title=_("Project folder name"),
            description=_("Used by every template without custom options. Date and number "
                          "can also be switched on or off when creating a project."),
        )
        self.format_editor = FormatEditor(config.format, format_group.add, format_group.remove,
                                          on_change=window.save, parent=self)

        page = Adw.PreferencesPage()
        page.add(language_group)
        page.add(format_group)
        self.add(page)

    def _on_language_changed(self, *_args):
        config = self.window.config
        config.language = LANGUAGE_CHOICES[self.language_row.get_selected()]
        i18n.set_language(config.language)
        core.translate_example(config, i18n.current_language())
        self.window.save()
        # Rebuild the interface in the new language.
        app = self.window.get_application()
        self.close()
        GLib.idle_add(app.rebuild_window)


class ManagerApplication(Adw.Application):
    def __init__(self):
        super().__init__(application_id=core.APP_ID)
        self.window = None
        for name, callback in (("preferences", self._on_preferences),
                               ("about", self._on_about)):
            action = Gio.SimpleAction.new(name, None)
            action.connect("activate", callback)
            self.add_action(action)
        self.set_accels_for_action("app.preferences", ["<Control>comma"])
        self.set_accels_for_action("window.close", ["<Control>w", "<Control>q"])

    def do_activate(self):
        if self.window is None:
            try:
                config = core.load_config()
            except core.AppError as error:
                show_error(self, "FolidStruct", str(error))
                return
            self.window = ManagerWindow(self, config)
        self.window.present()

    def rebuild_window(self):
        old = self.window
        self.window = ManagerWindow(self, old.config)
        self.window.set_default_size(old.get_width(), old.get_height())
        if old.is_maximized():
            self.window.maximize()
        self.window.present()
        old.destroy()
        return GLib.SOURCE_REMOVE

    def _on_preferences(self, *_args):
        PreferencesDialog(self.window).present(self.window)

    def _on_about(self, *_args):
        about = Adw.AboutDialog(
            application_name="FolidStruct",
            application_icon=core.APP_ID,
            version=core.VERSION,
            comments=_("Create project folder structures from templates") + "\n\n"
                     + _("Right-click a folder in Files and choose FolidStruct"),
            license_type=Gtk.License.GPL_3_0,
        )
        about.present(self.window)


def run():
    GLib.set_application_name("FolidStruct")
    return ManagerApplication().run(None)
