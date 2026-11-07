#!/usr/bin/env bash
# Check that every file carries the same version (and the tag, if given).
#   scripts/check-version.sh [v1.2.3]
set -euo pipefail
cd "$(dirname "$0")/.."

meson=$(sed -n "s/^  version: '\(.*\)',/\1/p" meson.build)
python=$(sed -n 's/^VERSION = "\(.*\)"/\1/p' folidstruct/core.py)
debian=$(sed -n '1s/.*(\(.*\)).*/\1/p' debian/changelog)
aur=$(sed -n 's/^pkgver=//p' packaging/aur/PKGBUILD)
metainfo=$(grep -o '<release version="[^"]*"' data/io.github.rem2608.FolidStruct.metainfo.xml | head -1 | cut -d'"' -f2)

status=0
for name in python debian aur metainfo; do
    if [ "${!name}" != "$meson" ]; then
        echo "Version mismatch: meson.build has $meson, $name has ${!name}" >&2
        status=1
    fi
done
if [ $# -gt 0 ] && [ "${1#v}" != "$meson" ]; then
    echo "Tag $1 does not match version $meson" >&2
    status=1
fi
[ $status = 0 ] && echo "Version $meson"
exit $status
