#!/usr/bin/env bash
# ScreenTimeSync for Omarchy/Hyprland — user-local installer (no root needed).
set -euo pipefail

SRC_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
DEST="$DATA_HOME/screentime-sync"
BIN_DIR="$HOME/.local/bin"
ICON_DIR="$DATA_HOME/icons/hicolor/scalable/apps"
APP_DIR="$DATA_HOME/applications"
AUTOSTART_DIR="$HOME/.config/autostart"
DESKTOP_FILE="screentime-sync.desktop"

echo "Installing ScreenTimeSync (Linux/Omarchy)…"

mkdir -p "$DEST" "$BIN_DIR" "$ICON_DIR" "$APP_DIR" "$AUTOSTART_DIR"

cp "$SRC_DIR/screentime_sync.py" "$DEST/screentime_sync.py"
chmod 0755 "$DEST/screentime_sync.py"
ln -sf "$DEST/screentime_sync.py" "$BIN_DIR/screentime-sync"
cp "$SRC_DIR/screentime-sync.svg" "$ICON_DIR/screentime-sync.svg"

cat > "$APP_DIR/$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=ScreenTimeSync
Comment=Per-app screen time tracker (Omarchy/Hyprland)
Exec=$DEST/screentime_sync.py
Icon=screentime-sync
Terminal=false
Categories=Utility;System;
StartupNotify=true
StartupWMClass=dev.screentimesync.Omarchy
EOF

# Autostart on login (tracking only runs while the app is running).
cp "$APP_DIR/$DESKTOP_FILE" "$AUTOSTART_DIR/$DESKTOP_FILE"

command -v update-desktop-database >/dev/null && update-desktop-database "$APP_DIR" || true

echo "Installed."
echo "  App:      $DEST/screentime_sync.py"
echo "  Launcher: $BIN_DIR/screentime-sync"
echo "  Desktop:  $APP_DIR/$DESKTOP_FILE"
echo "  Autostart:$AUTOSTART_DIR/$DESKTOP_FILE"
echo "  Data:     ${XDG_DATA_HOME:-$HOME/.local/share}/screentime-sync/screentime.db"
echo
echo "Launch it now:  screentime-sync   (or pick 'ScreenTimeSync' in your launcher)"
echo "Logs (if launched from terminal):  screentime-sync"
