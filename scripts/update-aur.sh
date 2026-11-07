#!/usr/bin/env bash
# After publishing a release on GitHub: fill in the checksum and regenerate
# .SRCINFO, ready to be pushed to the AUR repository. Needs makepkg (Arch).
set -euo pipefail
cd "$(dirname "$0")/../packaging/aur"
updpkgsums
makepkg --printsrcinfo > .SRCINFO
echo "Done. Copy PKGBUILD and .SRCINFO to your AUR clone and push."
