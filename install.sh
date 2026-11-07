#!/usr/bin/env bash
# Install FolidStruct for the current user (no root needed). The context menu is
# added to every supported file manager found: Nautilus, Nemo, Caja, Dolphin.
#
# Distribution packages use Meson instead (see README).
set -euo pipefail

SRC="$(cd "$(dirname "$0")" && pwd)"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
BIN="$HOME/.local/bin"
APP_ID="io.github.rem2608.FolidStruct"
LIB="$DATA/folidstruct"
CMD="$BIN/folidstruct"

bold() { printf '\033[1m%s\033[0m\n' "$*"; }
warn() { printf '  \033[33m!\033[0m %s\n' "$*"; }
ok()   { printf '  \033[32m✓\033[0m %s\n' "$*"; }

# Rebuild the user icon cache, if there is one: a stale cache hides icons.
refresh_icons() {
    local dir="$DATA/icons/hicolor" tool
    [ -f "$dir/icon-theme.cache" ] || return 0
    tool=$(command -v gtk4-update-icon-cache || command -v gtk-update-icon-cache) || return 0
    "$tool" -f -t -q "$dir" || true
}

# Escape a value for the replacement side of sed s|…|…|.
sed_escape() { printf '%s' "$1" | sed 's/[\\&|]/\\&/g'; }

# Is a file manager's python extension loader installed?
has_lib() { compgen -G "/usr/lib*/$1" >/dev/null || compgen -G "/usr/lib*/*/$1" >/dev/null; }

# --- Requirements ------------------------------------------------------------

bold "Checking requirements…"

if [ -x /usr/bin/folidstruct ] || [ -x /usr/local/bin/folidstruct ]; then
    echo "FolidStruct is already installed system-wide (e.g. from a package)." >&2
    echo "Installing it again for this user would show the menu twice." >&2
    exit 1
fi

if ! command -v python3 >/dev/null; then
    echo "Python 3 is required." >&2
    exit 1
fi

if ! python3 - <<'PY' 2>/dev/null
import sys
import gi
gi.require_version("Gtk", "4.0")
gi.require_version("Adw", "1")
from gi.repository import Adw
sys.exit(0 if sys.version_info >= (3, 10)
         and (Adw.get_major_version(), Adw.get_minor_version()) >= (1, 5) else 1)
PY
then
    cat >&2 <<'MSG'

FolidStruct needs Python 3.10+, GTK 4 and libadwaita 1.5 or newer, which ship with
Ubuntu 24.04, Debian 13, Fedora 40 and later. If your system is recent,
install the missing packages:

    sudo apt install python3-gi gir1.2-gtk-4.0 gir1.2-adw-1     # Debian/Ubuntu
    sudo dnf install python3-gobject gtk4 libadwaita            # Fedora
    sudo pacman -S python-gobject gtk4 libadwaita               # Arch
MSG
    exit 1
fi
ok "Python, GTK 4 and libadwaita"

# --- Application -------------------------------------------------------------

bold "Installing FolidStruct…"

rm -rf "$LIB"
mkdir -p "$LIB"
cp -r "$SRC/folidstruct" "$LIB/"
find "$LIB" -name '__pycache__' -prune -exec rm -rf {} +

mkdir -p "$BIN"
CMD_SED=$(sed_escape "$CMD")
sed -e "s|@PYTHON@|$(sed_escape "$(command -v python3)")|" -e "s|@PKGDATADIR@|$(sed_escape "$LIB")|" \
    "$SRC/bin/folidstruct.in" > "$CMD"
chmod +x "$CMD"

mkdir -p "$DATA/applications" "$DATA/metainfo" \
         "$DATA/icons/hicolor/scalable/apps" "$DATA/icons/hicolor/symbolic/apps"
sed "s|@COMMAND_PATH@|$CMD_SED|" "$SRC/data/$APP_ID.desktop.in" > "$DATA/applications/$APP_ID.desktop"
cp "$SRC/data/$APP_ID.svg" "$DATA/icons/hicolor/scalable/apps/"
cp "$SRC/data/$APP_ID-symbolic.svg" "$DATA/icons/hicolor/symbolic/apps/"
cp "$SRC/data/$APP_ID.metainfo.xml" "$DATA/metainfo/"
refresh_icons
update-desktop-database -q "$DATA/applications" 2>/dev/null || true
ok "Application (open “FolidStruct” from the applications menu)"

# --- File manager menus ------------------------------------------------------

bold "Adding the context menu…"

found=0
restart=()

# install_python_ext NAME NAMESPACE LOADER_LIB EXT_DIR PACKAGE_HINT
install_python_ext() {
    local name=$1 namespace=$2 lib=$3 dir=$4 hint=$5
    command -v "$name" >/dev/null || return 0
    found=1
    if ! has_lib "$lib"; then
        warn "$namespace: install the extension support first, then run install.sh again:"
        echo "      $hint"
        return 0
    fi
    mkdir -p "$DATA/$dir"
    sed -e "s|@COMMAND_PATH@|$CMD_SED|" -e "s|@FILE_MANAGER@|$namespace|" \
        "$SRC/extensions/folidstruct_menu.py" > "$DATA/$dir/folidstruct_${name}.py"
    ok "$namespace"
    restart+=("$name -q")
}

# Nautilus 43+ (GTK 4). Older versions use a different extension API.
if command -v nautilus >/dev/null; then
    version=$(nautilus --version 2>/dev/null | sed -n 's/^GNOME nautilus \([0-9.]*\).*/\1/p' | head -1)
    if [ -n "$version" ] && [ "${version%%.*}" -lt 43 ]; then
        found=1
        warn "Nautilus $version is too old (43 or newer is required)"
    else
        install_python_ext nautilus Nautilus "nautilus/extensions-4/libnautilus-python.so" \
            "nautilus-python/extensions" \
            "sudo apt install python3-nautilus   |   sudo dnf install nautilus-python   |   sudo pacman -S python-nautilus"
    fi
fi

install_python_ext nemo Nemo "nemo/extensions-3.0/libnemo-python.so" \
    "nemo-python/extensions" \
    "sudo apt install nemo-python   |   sudo dnf install nemo-python"

install_python_ext caja Caja "caja/extensions-2.0/libcaja-python.so" \
    "caja-python/extensions" \
    "sudo apt install python3-caja   |   sudo dnf install python3-caja"

# Dolphin: a fixed service menu; the template is chosen in the dialog.
if command -v dolphin >/dev/null; then
    found=1
    menu_dirs=("$DATA/kio/servicemenus")
    command -v kbuildsycoca5 >/dev/null && menu_dirs+=("$DATA/kservices5/ServiceMenus")
    for dir in "${menu_dirs[@]}"; do
        mkdir -p "$dir"
        sed "s|@COMMAND_PATH@|$CMD_SED|" "$SRC/data/$APP_ID.servicemenu.desktop.in" \
            > "$dir/$APP_ID.servicemenu.desktop"
        chmod +x "$dir/$APP_ID.servicemenu.desktop"
    done
    ok "Dolphin"
fi

if [ "$found" = 0 ]; then
    warn "No supported file manager found (Nautilus, Nemo, Caja, Dolphin)."
    echo "      FolidStruct can still be used from the command line:  folidstruct --new TEMPLATE --dir DIR"
fi

# Default configuration (only if there is none yet).
"$CMD" --init
command -v kbuildsycoca5 >/dev/null && kbuildsycoca5 >/dev/null 2>&1 || true

bold "Done."
if [ ${#restart[@]} -gt 0 ]; then
    echo "Restart the file manager to load the menu:  ${restart[*]}"
fi
