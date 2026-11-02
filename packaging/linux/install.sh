#!/bin/sh
# Installs Disc Image Studio for the current user, without root: the executable to
# ~/.local/bin/disc-image-studio, the icon and the menu entry under ~/.local/share.
# Burning also needs read/write access to /dev/sr*: add yourself to the "cdrom" group
# (Debian, Ubuntu, Fedora) or the "optical" group (Arch), then log in again.
set -eu
here=$(cd "$(dirname "$0")" && pwd)
bin_dir="${XDG_BIN_HOME:-$HOME/.local/bin}"
data_dir="${XDG_DATA_HOME:-$HOME/.local/share}"
mkdir -p "$bin_dir" "$data_dir/applications" "$data_dir/icons/hicolor/256x256/apps"
cp "$here/DiscImageStudio" "$bin_dir/disc-image-studio"
chmod 755 "$bin_dir/disc-image-studio"
cp "$here/disc-image-studio.png" "$data_dir/icons/hicolor/256x256/apps/disc-image-studio.png"
sed "s|^Exec=.*|Exec=\"$bin_dir/disc-image-studio\"|" "$here/disc-image-studio.desktop" \
    > "$data_dir/applications/disc-image-studio.desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data_dir/applications" || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q "$data_dir/icons/hicolor" || true
echo "Installed: $bin_dir/disc-image-studio"
