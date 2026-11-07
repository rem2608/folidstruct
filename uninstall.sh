#!/usr/bin/env bash
# Remove a FolidStruct installation made with install.sh. Your templates in
# ~/.config/folidstruct are kept.
set -euo pipefail

DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
APP_ID="io.github.rem2608.FolidStruct"

# Rebuild the user icon cache, if there is one: a stale cache hides icons.
refresh_icons() {
    local dir="$DATA/icons/hicolor" tool
    [ -f "$dir/icon-theme.cache" ] || return 0
    tool=$(command -v gtk4-update-icon-cache || command -v gtk-update-icon-cache) || return 0
    "$tool" -f -t -q "$dir" || true
}

rm -rf "$DATA/folidstruct"
rm -f "$HOME/.local/bin/folidstruct" \
      "$DATA/applications/$APP_ID.desktop" \
      "$DATA/metainfo/$APP_ID.metainfo.xml" \
      "$DATA/icons/hicolor/scalable/apps/$APP_ID.svg" \
      "$DATA/icons/hicolor/symbolic/apps/$APP_ID-symbolic.svg" \
      "$DATA/nautilus-python/extensions/folidstruct_nautilus.py" \
      "$DATA/nemo-python/extensions/folidstruct_nemo.py" \
      "$DATA/caja-python/extensions/folidstruct_caja.py" \
      "$DATA/kio/servicemenus/$APP_ID.servicemenu.desktop" \
      "$DATA/kservices5/ServiceMenus/$APP_ID.servicemenu.desktop"
refresh_icons
update-desktop-database -q "$DATA/applications" 2>/dev/null || true
command -v kbuildsycoca5 >/dev/null && kbuildsycoca5 >/dev/null 2>&1 || true

echo "FolidStruct removed. Templates are still in ${XDG_CONFIG_HOME:-$HOME/.config}/folidstruct"
echo "Restart your file manager to remove the menu (e.g. nautilus -q)."
