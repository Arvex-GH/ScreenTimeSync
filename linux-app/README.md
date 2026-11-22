# ScreenTimeSync for Omarchy / Hyprland (Linux-native port)

A Python + GTK4/libadwaita port of the Windows ScreenTimeSync app for the
Omarchy desktop (Hyprland). Same data schema, same admin-panel concept —
including the new **Editor Mode**.

## What works

- **Foreground-window tracking** via `hyprctl -j activewindow` (800 ms poll,
  2 s max credit per tick, gaps from sleep/lock are never counted)
- **Lock detection** (`hyprlock`/`swaylock`/`waylock` running ⇒ 0 credit) and
  **best-effort idle detection** via logind `IdleHint` (Hyprland does not always
  expose it — if yours doesn't, idle time is counted)
- **Per-app time + daily limits** with the same categories as Windows
  (Browser, Development, Productivity, Social, Gaming, Media, Other)
- **SQLite storage** at `~/.local/share/screentime-sync/screentime.db` —
  identical schema and JSON payload to the Windows app
- **Dashboard**: today's total (hero), current app, per-app list with limit
  progress bars, top-3 category bars — dark theme matching the Windows look
- **Admin panel** (gear button in the header bar or **Ctrl+Shift+P**),
  password-gated:
  - **Reset Today** — zero all times for today
  - **Export CSV** — writes `~/Documents/ScreenTimeSync/screentime-<date>.csv`
  - **Enter Editor Mode** — set the *exact* total time (any value up to 24 h),
    and per-app hours/minutes; rename apps, change process/category,
    **add** new apps, **remove** tracked apps (✕)
    - **Enter** (or the Save button) = save & apply immediately
    - **Esc** (or the Cancel button) = discard changes
    - Limits previously set for an app process are preserved
    - Editor-set totals are honored across restarts

## Not ported (yet)

- **Bluetooth LE phone sync** (Android controller) — Windows-only for now
- **Tray icon** — closing the window quits the app (start it via the launcher
  or autostart; there is no tray integration)
- **Weekly stats** — only today is persisted, same as the Windows app

## Install (user-local, no root)

```bash
./install.sh
screentime-sync            # or launch 'ScreenTimeSync' from the launcher
```

The installer copies the app to `~/.local/share/screentime-sync/`, symlinks
`~/.local/bin/screentime-sync`, registers a `.desktop` entry and an
**autostart** entry (remove it from `~/.config/autostart/` to disable).

## Uninstall

```bash
./uninstall.sh          # keeps your tracked data
./uninstall.sh --purge  # also deletes ~/.local/share/screentime-sync
```

## Admin password

Default `akiriro` (same as the Windows build). Override with:

```bash
SCREENTIME_ADMIN_PASSWORD='s3cret' screentime-sync
# or
screentime-sync --admin-password 's3cret'
```

## Self-test (headless)

```bash
screentime-sync --self-test
```

Exercises storage round-trip, editor-mode validation (exact times,
duplicate-process rejection, 24 h caps) and limit preservation — no display needed.
