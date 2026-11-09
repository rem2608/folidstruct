#!/usr/bin/env bash
# Set the version everywhere it appears and add a release entry.
#   scripts/set-version.sh 0.2.0
set -euo pipefail
cd "$(dirname "$0")/.."

version=${1:?usage: $0 VERSION}
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Version must look like 1.2.3" >&2; exit 1; }
today=$(date +%F)

sed -i "s/^  version: '.*',/  version: '$version',/" meson.build
sed -i "s/^VERSION = \".*\"/VERSION = \"$version\"/" folidstruct/core.py
sed -i "s/^pkgver=.*/pkgver=$version/; s/^pkgrel=.*/pkgrel=1/" packaging/aur/PKGBUILD
sed -i "s/public const string Version = \".*\";/public const string Version = \"$version\";/" \
    windows/src/FolidStruct.Core/Config.cs
sed -i "s/^    Version=\"[0-9.]*\" \/>/    Version=\"$version.0\" \/>/" \
    windows/src/FolidStruct.App/Package.appxmanifest

if ! grep -q "<release version=\"$version\"" data/io.github.rem2608.FolidStruct.metainfo.xml; then
    sed -i "s|  <releases>|  <releases>\n    <release version=\"$version\" date=\"$today\"/>|" \
        data/io.github.rem2608.FolidStruct.metainfo.xml
fi

if ! head -1 debian/changelog | grep -q "($version)"; then
    { printf 'folidstruct (%s) unstable; urgency=medium\n\n  * New release.\n\n -- %s  %s\n\n' \
          "$version" "rem2608 <m_ricchi@ikmail.com>" "$(date -R)"
      cat debian/changelog; } > debian/changelog.new
    mv debian/changelog.new debian/changelog
fi

echo "Version set to $version. Edit the release notes in debian/changelog and"
echo "data/io.github.rem2608.FolidStruct.metainfo.xml, then commit and tag v$version."
