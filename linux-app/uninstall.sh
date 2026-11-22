#!/usr/bin/env bash
# ScreenTimeSync (Linux/Omarchy) uninstaller — removes binaries/launchers.
# Your tracked data in ~/.local/share/screentime-sync is kept unless --purge.
set -euo pipefail

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
DEST="$DATA_HOME/screentime-sync"
BIN_DIR="$HOME/.local/bin"
ICON_DIR="$DATA_HOME/icons/hicolor/scalable/apps"
APP_DIR="$DATA_HOME/applications"
AUTOSTART_DIR="$HOME/.config/autostart"
DESKTOP_FILE="screentime-sync.desktop"

rm -f "$BIN_DIR/screentime-sync" "$ICON_DIR/screentime-sync.svg" \
      "$APP_DIR/$DESKTOP_FILE" "$AUTOSTART_DIR/$DESKTOP_FILE"
rm -rf "$DEST"

if [[ "${1:-}" == "--purge" ]]; then
    rm -rf "$DATA_HOME/screentime-sync"
    echo "Removed app and its data ($DATA_HOME/screentime-sync)."
else
    echo "Removed app files. Data kept at $DATA_HOME/screentime-sync (use --purge to delete)."
fi
