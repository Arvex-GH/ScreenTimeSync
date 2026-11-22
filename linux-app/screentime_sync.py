#!/usr/bin/env python3
"""
ScreenTimeSync for Omarchy/Hyprland — Linux-native port of the Windows app.

Tracks foreground-window time per app (hyprctl), enforces daily limits,
persists to SQLite with the SAME schema/fields as the Windows app
(~/.local/share/screentime-sync/screentime.db), and exposes an admin panel
with an exact-time editor mode (Enter = save, Esc = cancel).

BLE sync to the Android phone controller is Windows-only and intentionally
not ported. Everything else is feature-matched incl. the hidden admin drawer
(Ctrl+Shift+P or the gear button).
"""

import argparse
import csv
import datetime as dt
import json
import os
import sqlite3
import subprocess
import sys
from pathlib import Path

CATEGORIES = ["Browser", "Development", "Productivity", "Social", "Gaming", "Media", "Other"]

CATEGORY_MAP = {
    "chrome": "Browser", "chromium": "Browser", "firefox": "Browser", "opera": "Browser",
    "brave": "Browser", "microsoft-edge": "Browser", "epiphany": "Browser",
    "code": "Development", "code-oss": "Development", "codium": "Development", "devenv": "Development",
    "rider": "Development", "idea": "Development", "vim": "Development", "nvim": "Development",
    "emacs": "Development", "gdb": "Development",
    "photoshop": "Productivity", "figma": "Productivity", "notion": "Productivity",
    "libreoffice": "Productivity", "obsidian": "Productivity",
    "spotify": "Media", "vlc": "Media", "mpv": "Media", "youtube": "Media", "strawberry": "Media",
    "discord": "Social", "slack": "Social", "teams": "Social", "telegram": "Social",
    "whatsapp": "Social", "signal": "Social", "element": "Social",
    "steam": "Gaming", "lutris": "Gaming", "cs2": "Gaming", "dota2": "Gaming", "minecraft": "Gaming",
}

CATEGORY_COLORS = {
    "Browser": "#6C5CE7", "Productivity": "#00B894", "Social": "#E84393",
    "Gaming": "#FDCB6E", "Development": "#0984E3", "Media": "#E17055", "Other": "#636E72",
}

DATA_DIR = Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local" / "share")) / "screentime-sync"
DB_PATH = DATA_DIR / "screentime.db"

ACCENT = "#6C5CE7"
BG = "#0B0E14"
CARD = "#151A26"
CARD_BORDER = "#232A3D"
TEXT_MUTED = "#9AA0B8"
TEXT_DIM = "#5A6380"
SUCCESS = "#00CEC9"
DANGER = "#FF6B6B"
WARNING = "#FDCB6E"

CSS = f"""
window {{
    background-color: {BG};
}}
.card {{
    background-color: {CARD};
    border: 1px solid {CARD_BORDER};
    border-radius: 16px;
    padding: 20px;
}}
.hero-total {{
    font-size: 44px;
    font-weight: 800;
    color: #F8F9FF;
}}
.title {{
    color: #F8F9FF;
    font-weight: 700;
}}
.muted {{ color: {TEXT_MUTED}; font-size: 12px; }}
.dim {{ color: {TEXT_DIM}; font-size: 11px; }}
.section {{ color: {TEXT_MUTED}; font-size: 11px; font-weight: 700; }}
.danger {{ color: {DANGER}; font-size: 12px; }}
.success {{ color: {SUCCESS}; font-size: 12px; }}
.warning {{ color: {WARNING}; font-size: 12px; font-weight: 700; }}
.app-name {{ color: #F8F9FF; font-weight: 600; }}
.drawer {{
    background-color: {CARD};
    border-left: 1px solid {CARD_BORDER};
    padding: 22px;
}}
entry, combobox, combobox button {{
    background-color: #0F1220;
    color: #F8F9FF;
    border: 1px solid #2A3350;
    border-radius: 8px;
    min-height: 30px;
}}
progressbar > trough {{ background-color: #1E2438; border-radius: 6px; min-height: 6px; }}
progressbar > trough > progress {{ background-color: {ACCENT}; border-radius: 6px; }}
.remove-btn {{ color: {DANGER}; }}
.row-card {{
    background-color: #0F1220;
    border: 1px solid {CARD_BORDER};
    border-radius: 10px;
    padding: 10px;
}}
"""


def fmt_hm(seconds: float) -> str:
    total_minutes = int(seconds // 60)
    return f"{total_minutes // 60}h {total_minutes % 60:02d}m"


def today_key() -> str:
    return dt.datetime.now().strftime("%Y-%m-%d")


def prettify(name: str) -> str:
    name = (name or "").strip()
    return name[:1].upper() + name[1:] if name else "App"


def category_for(process: str) -> str:
    key = process.lower().split(".")[0]
    return CATEGORY_MAP.get(key, "Other")


class AppEntry:
    """One tracked application (mirrors AppUsageModel on Windows)."""

    def __init__(self, process, app_name, category, today_seconds=0,
                 limit_minutes=120, is_limited=False):
        self.process = process
        self.app_name = app_name
        self.category = category or "Other"
        self.today_seconds = float(today_seconds)
        self.daily_limit_seconds = int(limit_minutes) * 60
        self.is_limited = bool(is_limited)
        self.is_active = False

    @property
    def usage_percent(self):
        if self.is_limited and self.daily_limit_seconds > 0:
            return min(100.0, self.today_seconds / self.daily_limit_seconds * 100.0)
        return 0.0

    def limit_text(self):
        if not self.is_limited:
            return "No limit"
        mins = self.daily_limit_seconds // 60
        return f"{mins // 60}h {mins % 60:02d}m limit"

    def to_payload(self):
        return {
            "ProcessName": self.process,
            "AppName": self.app_name,
            "Category": self.category,
            "Minutes": int(self.today_seconds // 60),
            "LimitMinutes": int(self.daily_limit_seconds // 60),
            "IsLimited": self.is_limited,
        }


class Store:
    """SQLite persistence, byte-compatible schema with the Windows app."""

    def __init__(self, path: Path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(str(self.path))
        self.conn.execute("""
            CREATE TABLE IF NOT EXISTS daily_usage (
                date TEXT PRIMARY KEY,
                total_minutes INTEGER,
                payload_json TEXT
            )""")
        self.conn.execute("""
            CREATE TABLE IF NOT EXISTS app_limits (
                process_name TEXT PRIMARY KEY,
                limit_minutes INTEGER,
                is_limited INTEGER
            )""")
        self.conn.commit()

    def load_today(self):
        row = self.conn.execute(
            "SELECT total_minutes, payload_json FROM daily_usage WHERE date=?",
            (today_key(),)).fetchone()
        if not row:
            return [], None
        total_minutes, payload_json = row
        apps = []
        try:
            raw = json.loads(payload_json or "[]")
        except json.JSONDecodeError:
            raw = []
        limits = {}
        for proc, lim in self.conn.execute(
                "SELECT process_name, limit_minutes FROM app_limits WHERE is_limited=1"):
            limits[proc.lower()] = lim
        for item in raw:
            proc = str(item.get("ProcessName", "")).strip().lower()
            if not proc:
                continue
            minutes = max(0, int(item.get("Minutes", 0)))
            minutes = min(minutes, 24 * 60)
            entry = AppEntry(
                process=proc,
                app_name=str(item.get("AppName") or prettify(proc)),
                category=str(item.get("Category") or "Other"),
                today_seconds=minutes * 60,
                limit_minutes=int(item.get("LimitMinutes", 120)),
                is_limited=bool(item.get("IsLimited", False)),
            )
            if proc in limits:
                entry.daily_limit_seconds = limits[proc] * 60
                entry.is_limited = True
            apps.append(entry)
        total_seconds = None
        if total_minutes is not None and total_minutes > 0:
            total_seconds = min(total_minutes, 24 * 60) * 60
        return apps, total_seconds

    def save_snapshot(self, entries, total_seconds: float):
        payload = json.dumps([e.to_payload() for e in entries])
        self.conn.execute(
            "INSERT OR REPLACE INTO daily_usage(date, total_minutes, payload_json) VALUES(?,?,?)",
            (today_key(), int(total_seconds // 60), payload))
        for e in entries:
            if e.is_limited:
                self.conn.execute(
                    "INSERT OR REPLACE INTO app_limits(process_name, limit_minutes, is_limited) VALUES(?,?,1)",
                    (e.process, int(e.daily_limit_seconds // 60)))
        self.conn.commit()

    def close(self):
        self.conn.close()


def active_window_class():
    """Foreground window class via hyprctl; None on any failure."""
    try:
        out = subprocess.run(["hyprctl", "-j", "activewindow"],
                             capture_output=True, timeout=2, text=True).stdout
        data = json.loads(out or "{}")
        cls = (data.get("class") or "").strip().lower()
        return cls or None
    except Exception:
        return None


def lock_screen_active():
    """Best-effort: hyprlock (or hyprlock-like) running => session locked."""
    for proc in ("hyprlock", "swaylock", "waylock"):
        if subprocess.run(["pidof", proc], capture_output=True).returncode == 0:
            return True
    return False


_idle_cache = {"ts": 0.0, "idle": False}

def session_idle():
    """Best-effort idle detection via logind (not all compositors set it)."""
    now = dt.datetime.now().timestamp()
    if now - _idle_cache["ts"] < 5:
        return _idle_cache["idle"]
    idle = False
    try:
        sessions = subprocess.run(["loginctl", "list-sessions", "--no-legend"],
                                  capture_output=True, timeout=2, text=True).stdout
        for line in sessions.splitlines():
            parts = line.split()
            if not parts:
                continue
            out = subprocess.run(["loginctl", "show-session", parts[0], "-p", "IdleHint"],
                                 capture_output=True, timeout=2, text=True).stdout
            if "IdleHint=yes" in out:
                idle = True
                break
    except Exception:
        idle = False
    _idle_cache.update({"ts": now, "idle": idle})
    return idle


class Tracker:
    """Foreground-window time accounting (tick-credit model, like Windows)."""

    MAX_CREDIT_PER_TICK = 2.0

    def __init__(self, store: Store):
        self.store = store
        self.entries = {}
        self.total_seconds = 0.0
        apps, stored_total = store.load_today()
        for a in apps:
            self.entries[a.process] = a
        if stored_total is not None:
            # Honor an explicit total (e.g. set via the admin time-editor).
            self.total_seconds = float(stored_total)
        else:
            self.total_seconds = float(sum(a.today_seconds for a in apps))
        self._last_tick = dt.datetime.now()
        self.dirty = False

    # ---- tick -------------------------------------------------------------

    def credit_tick(self):
        """Advance accounting by the elapsed time. Returns True if anything was credited."""
        now = dt.datetime.now()
        delta = (now - self._last_tick).total_seconds()
        self._last_tick = now
        if delta <= 0:
            return False
        delta = min(delta, self.MAX_CREDIT_PER_TICK)  # sleep / hibernate gap cap

        if lock_screen_active():
            self._clear_active()
            return False
        if session_idle():
            self._clear_active()
            return False

        proc = active_window_class()
        if not proc:
            self._clear_active()
            return False

        entry = self.entries.get(proc)
        if entry is None:
            entry = AppEntry(process=proc, app_name=prettify(proc), category=category_for(proc))
            self.entries[proc] = entry
        self._clear_active()
        entry.is_active = True
        entry.today_seconds += delta
        self.total_seconds += delta
        self.dirty = True
        return True

    def _clear_active(self):
        for e in self.entries.values():
            e.is_active = False

    # ---- commands used by UI / admin panel --------------------------------

    def reset_today(self):
        for e in self.entries.values():
            e.today_seconds = 0.0
            e.is_active = False
        self.total_seconds = 0.0
        self.dirty = True
        self.persist()

    def apply_editor_changes(self, edited, total_seconds: float):
        """Replace today's tracked data with exact user-entered values."""
        self.entries.clear()
        for e in edited:
            e.today_seconds = max(0.0, min(float(e.today_seconds), 24 * 3600))
            e.is_active = False
            self.entries[e.process] = e
        self.total_seconds = max(0.0, min(float(total_seconds), 24 * 3600))
        self.dirty = True
        self.persist()

    def persist(self):
        self.store.save_snapshot(list(self.entries.values()), self.total_seconds)
        self.dirty = False


# ---------------------------------------------------------------------------
# Editor-mode validation (pure helpers — shared by UI and --self-test)
# ---------------------------------------------------------------------------

def _parse_int(text):
    try:
        return int(str(text).strip())
    except (ValueError, TypeError):
        return None


def validate_editor_inputs(total_hours_text, total_minutes_text, rows):
    """
    Validate the editor form. Returns (error_message_or_None, total_seconds, entries).
    `rows` is a list of dicts with keys: app_name, process, category, hours, minutes,
    and optionally 'source' (the original AppEntry) to preserve limit settings.
    """
    th = _parse_int(total_hours_text)
    tm = _parse_int(total_minutes_text)
    if th is None or tm is None or th < 0 or tm < 0:
        return "Total time must be whole numbers (0-24 h / 0-59 m).", 0, []
    if tm > 59:
        return "Total minutes must be 0-59.", 0, []
    if th > 24 or th * 60 + tm > 24 * 60:
        return "Total time cannot exceed 24 h.", 0, []
    total_seconds = float((th * 60 + tm) * 60)

    entries = []
    seen = set()
    for i, row in enumerate(rows, start=1):
        app_name = str(row.get("app_name", "")).strip()
        label = f'"{app_name}"' if app_name else f"row {i}"

        hours = _parse_int(row.get("hours"))
        minutes = _parse_int(row.get("minutes"))
        if hours is None or minutes is None or hours < 0 or minutes < 0:
            return f"Hours/minutes for {label} must be whole numbers (0-24 / 0-59).", 0, []
        if minutes > 59:
            return f"Minutes for {label} must be 0-59.", 0, []
        if hours > 24 or hours * 60 + minutes > 24 * 60:
            return f"Time for {label} cannot exceed 24 h.", 0, []

        process = str(row.get("process", "")).strip().lower()
        if process.endswith(".exe"):
            process = process[:-4]
        if not process:
            process = "".join(c for c in app_name.lower() if c.isalnum())
        if not process:
            return f"Row {i} needs at least an app name or a process name.", 0, []
        if process in seen:
            return f'Duplicate process "{process}" — each app can appear only once.', 0, []
        seen.add(process)

        source = row.get("source")
        entry = AppEntry(
            process=process,
            app_name=app_name or prettify(process),
            category=(str(row.get("category", "")).strip() or "Other"),
            today_seconds=(hours * 60 + minutes) * 60,
            limit_minutes=(source.daily_limit_seconds // 60) if source else 120,
            is_limited=source.is_limited if source else False,
        )
        entries.append(entry)
    return None, total_seconds, entries


# ---------------------------------------------------------------------------
# GUI (imported lazily so --self-test works without GTK/display)
# ---------------------------------------------------------------------------

def run_gui(args):
    import gi
    gi.require_version("Gtk", "4.0")
    gi.require_version("Adw", "1")
    from gi.repository import Adw, Gdk, GLib, Gtk

    store = Store(Path(args.db_path) if args.db_path else DB_PATH)
    tracker = Tracker(store)

    class ScreenTimeWindow(Adw.ApplicationWindow):
        def __init__(self, app):
            super().__init__(application=app, title="ScreenTimeSync — Omarchy")
            self.tracker = tracker
            self.admin_password = args.admin_password
            self.set_default_size(1040, 720)
            self.set_size_request(880, 620)
            self._editor_row_widgets = []
            self._build_ui()
            self._install_key_controller()
            GLib.timeout_add(800, self._tick)
            GLib.timeout_add(10_000, self._rebuild_apps)
            GLib.timeout_add(10_000, self._persist_if_dirty)
            self._refresh_hero()
            self._rebuild_apps()
            self.connect("close-request", self._on_close)

        # ---------------- layout ----------------

        def _build_ui(self):
            css = Gtk.CssProvider()
            css.load_from_string(CSS)
            Gtk.StyleContext.add_provider_for_display(
                Gdk.Display.get_default(), css, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
            try:
                Adw.StyleManager.get_default().set_color_scheme(Adw.ColorScheme.FORCE_DARK)
            except Exception:
                pass

            header = Adw.HeaderBar()
            title = Adw.WindowTitle(title="ScreenTimeSync",
                                     subtitle="Per-app tracking • Omarchy (Hyprland)")
            header.set_title_widget(title)
            admin_btn = Gtk.Button(icon_name="emblem-system-symbolic")
            admin_btn.set_tooltip_text("Admin Panel (Ctrl+Shift+P)")
            admin_btn.connect("clicked", lambda _b: self.toggle_admin())
            header.pack_end(admin_btn)

            toolbar = Adw.ToolbarView()
            toolbar.add_top_bar(header)
            toolbar.set_content(self._build_overlay())
            self.set_content(toolbar)

        def _build_overlay(self):
            overlay = Gtk.Overlay()

            content = Gtk.ScrolledWindow()
            content.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
            outer = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=16)
            outer.set_margin_top(22); outer.set_margin_bottom(22)
            outer.set_margin_start(22); outer.set_margin_end(22)
            content.set_child(outer)

            # hero card
            hero = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
            hero.add_css_class("card")
            self.date_label = Gtk.Label(xalign=0); self.date_label.add_css_class("muted")
            self.date_label.set_text(dt.datetime.now().strftime("%A, %B %d"))
            cap = Gtk.Label(xalign=0); cap.add_css_class("muted"); cap.set_text("Today's Screen Time")
            self.total_label = Gtk.Label(xalign=0); self.total_label.add_css_class("hero-total"); self.total_label.set_text("0h 00m")
            self.state_label = Gtk.Label(xalign=0); self.state_label.add_css_class("dim"); self.state_label.set_text("active now")
            hero.append(self.date_label); hero.append(cap); hero.append(self.total_label); hero.append(self.state_label)
            outer.append(hero)

            # apps card
            apps_card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
            apps_card.add_css_class("card")
            apps_cap = Gtk.Label(xalign=0); apps_cap.add_css_class("section"); apps_cap.set_text("APPS TODAY")
            apps_card.append(apps_cap)
            self.apps_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
            apps_card.append(self.apps_box)
            outer.append(apps_card)

            # categories card
            cat_card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
            cat_card.add_css_class("card")
            cat_cap = Gtk.Label(xalign=0); cat_cap.add_css_class("section"); cat_cap.set_text("TOP CATEGORIES")
            cat_card.append(cat_cap)
            self.cat_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6)
            cat_card.append(self.cat_box)
            outer.append(cat_card)

            foot = Gtk.Label(xalign=0); foot.add_css_class("dim")
            foot.set_text("Local & private • SQLite • Hyprctl tracking • BLE phone sync is Windows-only")
            outer.append(foot)

            overlay.set_child(content)
            self.drawer = self._build_drawer()
            overlay.add_overlay(self.drawer)
            return overlay

        def _build_drawer(self):
            drawer = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
            drawer.add_css_class("drawer")
            drawer.set_size_request(340, -1)
            drawer.set_halign(Gtk.Align.END)
            drawer.set_valign(Gtk.Align.FILL)
            drawer.set_visible(False)

            head = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL)
            title = Gtk.Label(xalign=0); title.add_css_class("title"); title.set_text("Admin Panel")
            title.set_hexpand(True)
            close_btn = Gtk.Button(icon_name="window-close-symbolic")
            close_btn.add_css_class("flat")
            close_btn.connect("clicked", lambda _b: self.toggle_admin(False))
            head.append(title); head.append(close_btn)
            drawer.append(head)

            self.admin_stack = Gtk.Stack()
            self.admin_stack.set_transition_type(Gtk.StackTransitionType.CROSSFADE)
            self.admin_stack.add_named(self._build_locked_page(), "locked")
            self.admin_stack.add_named(self._build_unlocked_page(), "unlocked")
            self.admin_stack.add_named(self._build_editor_page(), "editor")
            drawer.append(self.admin_stack)
            return drawer

        # ---------------- admin pages ----------------

        def _build_locked_page(self):
            page = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
            cap = Gtk.Label(xalign=0); cap.add_css_class("muted"); cap.set_text("Enter admin password")
            self.password_entry = Gtk.Entry()
            self.password_entry.set_visibility(False)
            self.password_entry.set_input_purpose(Gtk.InputPurpose.PASSWORD)
            self.password_entry.connect("activate", lambda _e: self.try_unlock())
            self.unlock_error = Gtk.Label(xalign=0); self.unlock_error.add_css_class("danger")
            self.unlock_error.set_text("Wrong password"); self.unlock_error.set_visible(False)
            unlock_btn = Gtk.Button(label="Unlock")
            unlock_btn.add_css_class("suggested-action")
            unlock_btn.connect("clicked", lambda _b: self.try_unlock())
            page.append(cap); page.append(self.password_entry); page.append(self.unlock_error); page.append(unlock_btn)
            return page

        def _build_unlocked_page(self):
            page = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
            cap = Gtk.Label(xalign=0); cap.add_css_class("muted"); cap.set_text("Admin actions")
            reset_btn = Gtk.Button(label="Reset Today")
            reset_btn.connect("clicked", lambda _b: self.on_reset_today())
            export_btn = Gtk.Button(label="Export CSV")
            export_btn.connect("clicked", lambda _b: self.on_export_csv())
            sep = Gtk.Separator()
            editing_cap = Gtk.Label(xalign=0); editing_cap.add_css_class("muted"); editing_cap.set_text("Time editing")
            editor_btn = Gtk.Button(label="Enter Editor Mode")
            editor_btn.connect("clicked", lambda _b: self.enter_editor())
            hint = Gtk.Label(xalign=0, wrap=True); hint.add_css_class("dim")
            hint.set_text("Set exact total and per-app times. Enter saves, Esc cancels.")
            self.admin_status = Gtk.Label(xalign=0, wrap=True); self.admin_status.add_css_class("success")
            self.admin_status.set_visible(False)
            for w in (cap, reset_btn, export_btn, sep, editing_cap, editor_btn, hint, self.admin_status):
                page.append(w)
            return page

        def _build_editor_page(self):
            page = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)

            head = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL)
            title = Gtk.Label(xalign=0); title.add_css_class("warning"); title.set_text("Editor Mode")
            title.set_hexpand(True)
            key_hint = Gtk.Label(); key_hint.add_css_class("muted"); key_hint.set_text("Enter = Save • Esc = Cancel")
            head.append(title); head.append(key_hint)
            page.append(head)

            desc = Gtk.Label(xalign=0, wrap=True); desc.add_css_class("muted")
            desc.set_text("Set exact values for today. Changes apply immediately and persist.")
            page.append(desc)

            # total time row
            total_card = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            total_card.add_css_class("card")
            total_texts = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
            t1 = Gtk.Label(xalign=0); t1.add_css_class("section"); t1.set_text("TOTAL SCREEN TIME")
            t2 = Gtk.Label(xalign=0); t2.add_css_class("dim"); t2.set_text("Exact value, saved as-is")
            total_texts.append(t1); total_texts.append(t2)
            total_texts.set_hexpand(True)
            total_card.append(total_texts)
            self.total_hours = Gtk.Entry(); self.total_hours.set_input_purpose(Gtk.InputPurpose.NUMBER)
            self.total_hours.set_width_chars(3); self.total_hours.set_max_length(2); self.total_hours.set_text("0")
            self.total_minutes = Gtk.Entry(); self.total_minutes.set_input_purpose(Gtk.InputPurpose.NUMBER)
            self.total_minutes.set_width_chars(3); self.total_minutes.set_max_length(2); self.total_minutes.set_text("00")
            h_lbl = Gtk.Label(); h_lbl.set_text("h"); m_lbl = Gtk.Label(); m_lbl.set_text("m")
            for w in (self.total_hours, h_lbl, self.total_minutes, m_lbl):
                total_card.append(w)
            page.append(total_card)

            section = Gtk.Label(xalign=0); section.add_css_class("section")
            section.set_text("APPS — HOURS+MINUTES TODAY")
            page.append(section)

            scroller = Gtk.ScrolledWindow()
            scroller.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
            scroller.set_max_content_height(330)
            scroller.set_propagate_natural_height(True)
            self.editor_rows_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
            scroller.set_child(self.editor_rows_box)
            page.append(scroller)

            add_btn = Gtk.Button(label="Add App")
            add_btn.connect("clicked", lambda _b: self._add_editor_row())
            page.append(add_btn)

            self.editor_error = Gtk.Label(xalign=0, wrap=True); self.editor_error.add_css_class("danger")
            self.editor_error.set_visible(False)
            page.append(self.editor_error)

            btn_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            save_btn = Gtk.Button(label="Save (Enter)")
            save_btn.add_css_class("suggested-action"); save_btn.set_hexpand(True)
            save_btn.connect("clicked", lambda _b: self.commit_editor())
            cancel_btn = Gtk.Button(label="Cancel (Esc)")
            cancel_btn.set_hexpand(True)
            cancel_btn.connect("clicked", lambda _b: self.cancel_editor())
            btn_row.append(save_btn); btn_row.append(cancel_btn)
            page.append(btn_row)
            return page

        # ---------------- editor rows ----------------

        def _add_editor_row(self, app=None):
            row = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6)
            row.add_css_class("row-card")

            top = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6)
            name_entry = Gtk.Entry(hexpand=True, placeholder_text="App name")
            if app:
                name_entry.set_text(app.app_name)
            remove_btn = Gtk.Button(icon_name="window-close-symbolic")
            remove_btn.add_css_class("flat"); remove_btn.add_css_class("remove-btn")
            remove_btn.set_tooltip_text("Remove app")
            top.append(name_entry); top.append(remove_btn)

            process_entry = Gtk.Entry(placeholder_text="process/class (e.g. firefox)")
            if app:
                process_entry.set_text(app.process)

            bottom = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=4)
            hours_entry = Gtk.Entry(width_chars=3, max_length=2)
            hours_entry.set_input_purpose(Gtk.InputPurpose.NUMBER)
            minutes_entry = Gtk.Entry(width_chars=3, max_length=2)
            minutes_entry.set_input_purpose(Gtk.InputPurpose.NUMBER)
            category_entry = Gtk.Entry(hexpand=True, placeholder_text="Other")
            if app:
                hours_entry.set_text(str(int(app.today_seconds // 3600)))
                minutes_entry.set_text(f"{int((app.today_seconds % 3600) // 60):02d}")
                category_entry.set_text(app.category)
            else:
                hours_entry.set_text("0"); minutes_entry.set_text("00"); category_entry.set_text("Other")
            h_lbl = Gtk.Label(); h_lbl.set_text("h")
            m_lbl = Gtk.Label(); m_lbl.set_text("m")
            for w in (hours_entry, h_lbl, minutes_entry, m_lbl, category_entry):
                bottom.append(w)

            row.append(top); row.append(process_entry); row.append(bottom)
            self.editor_rows_box.append(row)

            widgets = {"row": row, "name": name_entry, "process": process_entry,
                       "hours": hours_entry, "minutes": minutes_entry, "category": category_entry,
                       "source": app}
            remove_btn.connect("clicked", lambda _b: self._remove_editor_row(widgets))
            self._editor_row_widgets.append(widgets)

        def _remove_editor_row(self, widgets):
            self._editor_row_widgets.remove(widgets)
            self.editor_rows_box.remove(widgets["row"])

        def _clear_editor_rows(self):
            for w in list(self._editor_row_widgets):
                self._remove_editor_row(w)
        # ---------------- admin actions ----------------

        def toggle_admin(self, force=None):
            visible = (not self.drawer.get_visible()) if force is None else force
            if visible:
                self.admin_stack.set_visible_child_name("locked")
                self.password_entry.set_text("")
                self.unlock_error.set_visible(False)
                self.admin_status.set_visible(False)
                self.editor_error.set_visible(False)
                self.drawer.set_visible(True)
                self.password_entry.grab_focus()
            else:
                self.drawer.set_visible(False)

        def try_unlock(self):
            if self.password_entry.get_text() == self.admin_password:
                self.unlock_error.set_visible(False)
                self.password_entry.set_text("")
                self.admin_stack.set_visible_child_name("unlocked")
            else:
                self.unlock_error.set_visible(True)
                self.password_entry.set_text("")
                self.password_entry.grab_focus()

        def on_reset_today(self):
            self.tracker.reset_today()
            self._rebuild_apps()
            self._refresh_hero()
            self.admin_status.set_text("Today reset to 0.")
            self.admin_status.set_visible(True)

        def on_export_csv(self):
            try:
                out_dir = Path.home() / "Documents" / "ScreenTimeSync"
                out_dir.mkdir(parents=True, exist_ok=True)
                out_file = out_dir / f"screentime-{dt.datetime.now():%Y%m%d-%H%M}.csv"
                with out_file.open("w", newline="") as f:
                    w = csv.writer(f)
                    w.writerow(["App", "Process", "Category", "Minutes", "LimitMinutes", "IsLimited"])
                    for e in sorted(self.tracker.entries.values(),
                                    key=lambda e: e.today_seconds, reverse=True):
                        w.writerow([e.app_name, e.process, e.category,
                                    int(e.today_seconds // 60),
                                    int(e.daily_limit_seconds // 60),
                                    "true" if e.is_limited else "false"])
                    w.writerow([f"Total,,,,{int(self.tracker.total_seconds // 60)},"])
                self.admin_status.set_text(f"Exported to {out_file}")
            except Exception as ex:
                self.admin_status.set_text(f"Export failed: {ex}")
            self.admin_status.set_visible(True)

        # ---------------- editor mode ----------------

        def enter_editor(self):
            self._clear_editor_rows()
            for app in sorted(self.tracker.entries.values(),
                              key=lambda e: e.today_seconds, reverse=True):
                self._add_editor_row(app)
            total = self.tracker.total_seconds
            self.total_hours.set_text(str(int(total // 3600)))
            self.total_minutes.set_text(f"{int((total % 3600) // 60):02d}")
            self.editor_error.set_visible(False)
            self.admin_stack.set_visible_child_name("editor")
            self.total_hours.grab_focus()

        def cancel_editor(self):
            self.editor_error.set_visible(False)
            self.admin_stack.set_visible_child_name("unlocked")

        def commit_editor(self):
            rows = [{
                "app_name": w["name"].get_text(),
                "process": w["process"].get_text(),
                "category": w["category"].get_text(),
                "hours": w["hours"].get_text(),
                "minutes": w["minutes"].get_text(),
                "source": w["source"],
            } for w in self._editor_row_widgets]
            error, total_seconds, entries = validate_editor_inputs(
                self.total_hours.get_text(), self.total_minutes.get_text(), rows)
            if error:
                self.editor_error.set_text(error)
                self.editor_error.set_visible(True)
                return
            self.tracker.apply_editor_changes(entries, total_seconds)
            self._rebuild_apps()
            self._refresh_hero()
            self.editor_error.set_visible(False)
            self.admin_status.set_text(
                f"Saved: total {fmt_hm(total_seconds)}, {len(entries)} app(s).")
            self.admin_status.set_visible(True)
            self.admin_stack.set_visible_child_name("unlocked")

        def _editor_visible(self):
            return (self.drawer.get_visible()
                    and self.admin_stack.get_visible_child_name() == "editor")

        def _install_key_controller(self):
            kc = Gtk.EventControllerKey()
            kc.set_propagation_phase(Gtk.PropagationPhase.CAPTURE)
            kc.connect("key-pressed", self._on_key_pressed)
            self.add_controller(kc)

        def _on_key_pressed(self, _ctrl, keyval, _keycode, state):
            ctrl_mod = bool(state & Gdk.ModifierType.CONTROL_MASK)
            shift_mod = bool(state & Gdk.ModifierType.SHIFT_MASK)
            if ctrl_mod and shift_mod and keyval in (Gdk.KEY_p, Gdk.KEY_P):
                self.toggle_admin()
                return True
            if self._editor_visible():
                if keyval in (Gdk.KEY_Return, Gdk.KEY_KP_Enter):
                    self.commit_editor()
                    return True
                if keyval == Gdk.KEY_Escape:
                    self.cancel_editor()
                    return True
            return False

        # ---------------- refresh ----------------

        def _tick(self):
            self.tracker.credit_tick()
            self._refresh_hero()
            return True

        def _persist_if_dirty(self):
            if self.tracker.dirty:
                self.tracker.persist()
            return True

        def _refresh_hero(self):
            self.total_label.set_text(fmt_hm(self.tracker.total_seconds))
            self.date_label.set_text(dt.datetime.now().strftime("%A, %B %d"))
            active = next((e for e in self.tracker.entries.values() if e.is_active), None)
            if lock_screen_active():
                self.state_label.set_text("Locked — not counting")
            elif session_idle():
                self.state_label.set_text("Idle — not counting")
            else:
                self.state_label.set_text(f"Active: {active.app_name if active else '—'}")

        def _rebuild_apps(self):
            for child in list(_children(self.apps_box)):
                self.apps_box.remove(child)
            for child in list(_children(self.cat_box)):
                self.cat_box.remove(child)

            apps = sorted(self.tracker.entries.values(),
                          key=lambda e: e.today_seconds, reverse=True)
            if not apps:
                empty = Gtk.Label(xalign=0, wrap=True); empty.add_css_class("dim")
                empty.set_text("No apps tracked yet — switch windows and time starts counting.")
                self.apps_box.append(empty)
            for e in apps[:12]:
                self.apps_box.append(self._app_row(e))

            totals = {}
            for e in apps:
                totals[e.category] = totals.get(e.category, 0) + e.today_seconds
            top = sorted(totals.items(), key=lambda kv: kv[1], reverse=True)[:3]
            peak = max([v for _, v in top], default=0)
            if peak <= 0:
                none = Gtk.Label(xalign=0); none.add_css_class("dim"); none.set_text("—")
                self.cat_box.append(none)
            for name, secs in top:
                row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
                name_lbl = Gtk.Label(xalign=0); name_lbl.add_css_class("muted")
                name_lbl.set_text(name); name_lbl.set_size_request(110, -1)
                bar = Gtk.ProgressBar(hexpand=True, valign=Gtk.Align.CENTER)
                bar.set_fraction(max(0.02, secs / peak))
                mins_lbl = Gtk.Label(); mins_lbl.add_css_class("dim"); mins_lbl.set_text(f"{int(secs // 60)}m")
                row.append(name_lbl); row.append(bar); row.append(mins_lbl)
                self.cat_box.append(row)
            return True

        def _app_row(self, e):
            wrap = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
            row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            dot = Gtk.Label()
            dot.set_markup(f'<span foreground="{CATEGORY_COLORS.get(e.category, CATEGORY_COLORS["Other"])}">●</span>')
            col = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=0, hexpand=True)
            name_lbl = Gtk.Label(xalign=0); name_lbl.add_css_class("app-name"); name_lbl.set_text(e.app_name)
            sub = Gtk.Label(xalign=0); sub.add_css_class("dim")
            sub.set_text(f"{e.category} • {e.limit_text()}")
            col.append(name_lbl); col.append(sub)
            time_lbl = Gtk.Label(valign=Gtk.Align.CENTER); time_lbl.add_css_class("app-name")
            time_lbl.set_text(fmt_hm(e.today_seconds))
            row.append(dot); row.append(col); row.append(time_lbl)
            wrap.append(row)
            if e.is_limited:
                bar = Gtk.ProgressBar()
                bar.set_fraction(min(1.0, max(0.0, e.usage_percent / 100.0)))
                wrap.append(bar)
            return wrap

        def _on_close(self, _window):
            try:
                self.tracker.persist()
                self.tracker.store.close()
            except Exception:
                pass
            return False

    def _children(box):
        child = box.get_first_child()
        while child is not None:
            nxt = child.get_next_sibling()
            yield child
            child = nxt

    app = Adw.Application(application_id="dev.screentimesync.Omarchy")
    holder = {}

    def on_activate(application):
        if "window" not in holder:
            holder["window"] = ScreenTimeWindow(application)
        holder["window"].present()

    app.connect("activate", on_activate)
    return app.run(None)


# ---------------------------------------------------------------------------
# Self-test (no GTK/display required)
# ---------------------------------------------------------------------------

def self_test():
    import shutil
    import tempfile

    tmp = Path(tempfile.mkdtemp(prefix="screentime-test-"))
    db = tmp / "test.db"
    try:
        store = Store(db)
        tracker = Tracker(store)
        assert tracker.total_seconds == 0.0
        assert tracker.entries == {}

        # seed + persist, then reload (stored total must be honored)
        seed = AppEntry("firefox", "Firefox", "Browser", today_seconds=3600,
                        limit_minutes=30, is_limited=True)
        tracker.entries["firefox"] = seed
        tracker.total_seconds = 3600
        tracker.persist()
        store.close()

        tracker2 = Tracker(Store(db))
        assert tracker2.total_seconds == 3600, tracker2.total_seconds
        assert tracker2.entries["firefox"].today_seconds == 3600
        assert tracker2.entries["firefox"].is_limited

        # editor: exact values different from summed apps on purpose
        err, total_seconds, entries = validate_editor_inputs(
            "2", "30",
            [{"app_name": "Firefox", "process": "firefox", "category": "Browser",
               "hours": "1", "minutes": "05", "source": tracker2.entries["firefox"]},
              {"app_name": "Code", "process": "code", "category": "Development",
               "hours": "0", "minutes": "30"}])
        assert err is None, err
        tracker2.apply_editor_changes(entries, total_seconds)
        tracker2.store.close()

        tracker3 = Tracker(Store(db))
        assert tracker3.total_seconds == 2.5 * 3600, tracker3.total_seconds
        assert tracker3.entries["firefox"].today_seconds == 65 * 60
        assert tracker3.entries["firefox"].is_limited
        assert tracker3.entries["firefox"].daily_limit_seconds == 30 * 60
        assert tracker3.entries["code"].category == "Development"
        assert "code" in tracker3.entries
        tracker3.store.close()

        # validation failures
        assert validate_editor_inputs("0", "60", [])[0]
        assert validate_editor_inputs("25", "0", [])[0]
        assert validate_editor_inputs("1", "0",
            [{"app_name": "Slack", "process": "", "category": "", "hours": "x", "minutes": "05"}])[0]
        assert validate_editor_inputs("0", "10",
            [{"app_name": "", "process": "", "category": "", "hours": "0", "minutes": "05"}])[0]
        assert "Duplicate" in validate_editor_inputs("0", "10",
            [{"app_name": "A", "process": "same", "category": "", "hours": "0", "minutes": "10"},
             {"app_name": "B", "process": "same", "category": "", "hours": "0", "minutes": "10"}])[0]
        ok, total_seconds, entries = validate_editor_inputs("0", "00", [])
        assert ok is None and total_seconds == 0 and entries == []

        # per-app cap
        assert validate_editor_inputs("0", "10",
            [{"app_name": "War", "process": "war", "category": "Gaming", "hours": "24", "minutes": "01"}])[0]

        shutil.rmtree(tmp, ignore_errors=True)
        print("SELF-TEST OK")
        return 0
    except Exception:
        shutil.rmtree(tmp, ignore_errors=True)
        raise


def main():
    import argparse
    parser = argparse.ArgumentParser(description="ScreenTimeSync for Omarchy/Hyprland")
    parser.add_argument("--db-path", default=None,
                        help=f"SQLite path (default: {DB_PATH})")
    parser.add_argument("--admin-password",
                        default=os.environ.get("SCREENTIME_ADMIN_PASSWORD", "akiriro"),
                        help="Admin panel password (or set SCREENTIME_ADMIN_PASSWORD)")
    parser.add_argument("--self-test", action="store_true",
                        help="Run headless core self-test and exit")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    return run_gui(args)


if __name__ == "__main__":
    sys.exit(main())
