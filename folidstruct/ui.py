"""Small helpers shared by the windows."""

import gi

gi.require_version("Adw", "1")
from gi.repository import Adw  # noqa: E402


def show_error(app, heading, body):
    """Show an error when there is no window to attach it to."""
    dialog = Adw.AlertDialog(heading=heading, body=body)
    dialog.add_response("close", "OK")
    # Without a parent the dialog gets its own window; keep the app alive
    # until it is answered.
    app.hold()
    dialog.connect("response", lambda *_a: app.release())
    dialog.present(None)
