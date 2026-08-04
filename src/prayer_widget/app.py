"""Wires the widget, the tray icon, notifications and the athan together."""

from __future__ import annotations

import sys
import webbrowser
from datetime import datetime, timedelta
from pathlib import Path

from PySide6.QtCore import Qt, QThread, QTimer, QUrl, Signal
from PySide6.QtGui import QAction, QActionGroup, QIcon, QPainter, QPixmap, QColor, QFont
from PySide6.QtMultimedia import QAudioOutput, QMediaPlayer
from PySide6.QtWidgets import QApplication, QMenu, QMessageBox, QSystemTrayIcon

from . import __version__, autostart, sounds, updater
from .config import Config
from .locations import PlaceTimetable
from .settings_dialog import SettingsDialog
from .timetable import ARABIC_NAMES, DISPLAY_KEYS, format_remaining
from .widget import PrayerWidget

APP_NAME = "أوقات الصلاة"
TICK_MILLISECONDS = 1000


def _make_icon() -> QIcon:
    """A crescent drawn at runtime, so no .ico file has to be shipped or kept in sync."""
    pixmap = QPixmap(64, 64)
    pixmap.fill(QColor(0, 0, 0, 0))
    painter = QPainter(pixmap)
    painter.setRenderHint(QPainter.RenderHint.Antialiasing)
    painter.setPen(QColor(0, 0, 0, 0))
    painter.setBrush(QColor("#4ade80"))
    painter.drawEllipse(8, 8, 48, 48)
    painter.setBrush(QColor(0, 0, 0, 0))
    painter.setCompositionMode(QPainter.CompositionMode.CompositionMode_Clear)
    painter.setBrush(QColor("#000000"))
    painter.drawEllipse(24, 4, 44, 44)
    painter.end()
    return QIcon(pixmap)


class UpdateCheck(QThread):
    """Runs the release check off the UI thread so a slow network never freezes the widget."""

    found = Signal(object)

    def run(self) -> None:
        self.found.emit(updater.check(__version__))


class SoundFetch(QThread):
    """Downloads a recitation off the UI thread."""

    ready = Signal(str)

    def __init__(self, file_name: str) -> None:
        super().__init__()
        self._file_name = file_name

    def run(self) -> None:
        self.ready.emit(str(sounds.ensure(self._file_name)))


class PrayerApp:
    def __init__(self) -> None:
        self.config = Config.load()
        self.timetable = PlaceTimetable(self.config.place())

        self.qt = QApplication(sys.argv)
        self.qt.setQuitOnLastWindowClosed(False)

        self.widget = PrayerWidget(self.timetable, self.config)
        self.widget.stop_button.clicked.connect(self.stop_athan)

        self._audio = QAudioOutput()
        self._player = QMediaPlayer()
        self._player.setAudioOutput(self._audio)
        self._player.playbackStateChanged.connect(self._on_playback_changed)

        self._pending_reminder: datetime | None = None
        self._threads: list[QThread] = []

        self._build_tray()
        if self.config.widget_visible:
            self.widget.show()

        self._tick()
        self._timer = QTimer()
        self._timer.timeout.connect(self._tick)
        self._timer.start(TICK_MILLISECONDS)

        if self.config.check_for_updates:
            self._start_update_check()

    # --- tray ---------------------------------------------------------------------

    def _build_tray(self) -> None:
        self.tray = QSystemTrayIcon(_make_icon())
        self.tray.setToolTip(APP_NAME)
        self.tray.activated.connect(self._on_tray_activated)

        menu = QMenu()
        menu.setLayoutDirection(Qt.LayoutDirection.RightToLeft)

        self._times_actions: dict[str, QAction] = {}
        for key in DISPLAY_KEYS:
            action = QAction(ARABIC_NAMES[key], menu)
            action.setEnabled(False)
            menu.addAction(action)
            self._times_actions[key] = action

        menu.addSeparator()

        self._toggle_widget = QAction("إظهار الويدجت", menu, checkable=True)
        self._toggle_widget.setChecked(self.config.widget_visible)
        self._toggle_widget.triggered.connect(self._set_widget_visible)
        menu.addAction(self._toggle_widget)

        self._compact_action = QAction("شريط مضغوط", menu, checkable=True)
        self._compact_action.setChecked(self.config.compact)
        self._compact_action.triggered.connect(self._set_compact)
        menu.addAction(self._compact_action)

        settings = QAction("الإعدادات…", menu)
        settings.triggered.connect(self._open_settings)
        menu.addAction(settings)

        startup = QAction("التشغيل مع ويندوز", menu, checkable=True)
        # Read the real state rather than the stored one: the user may have deleted the
        # Startup entry by hand since the setting was last written.
        startup.setChecked(autostart.is_enabled())
        startup.triggered.connect(self._set_autostart)
        menu.addAction(startup)
        self._startup_action = startup

        menu.addSeparator()
        check = QAction("التحقّق من التحديثات", menu)
        check.triggered.connect(lambda: self._start_update_check(announce_up_to_date=True))
        menu.addAction(check)

        quit_action = QAction("إغلاق", menu)
        quit_action.triggered.connect(self.qt.quit)
        menu.addAction(quit_action)

        self.tray.setContextMenu(menu)
        self.tray.show()

    def _on_tray_activated(self, reason: QSystemTrayIcon.ActivationReason) -> None:
        if reason == QSystemTrayIcon.ActivationReason.Trigger:
            self._set_widget_visible(not self.widget.isVisible())

    def _set_widget_visible(self, visible: bool) -> None:
        self.widget.setVisible(visible)
        self._toggle_widget.setChecked(visible)
        self._set("widget_visible", visible)

    def _set(self, key: str, value) -> None:
        setattr(self.config, key, value)
        self.config.save()

    def _set_autostart(self, enabled: bool) -> None:
        if autostart.set_enabled(enabled):
            self._set("start_with_windows", enabled)
        else:
            # Writing to the Startup folder can be blocked by policy. Reverting the tick
            # is more honest than leaving it on for a thing that will not happen.
            self._startup_action.setChecked(autostart.is_enabled())
            self.tray.showMessage(
                APP_NAME, "تعذّر تعديل التشغيل التلقائي", _make_icon(), 5000
            )

    def _set_compact(self, compact: bool) -> None:
        self._set("compact", compact)
        self.widget.apply_layout(compact)
        self._tick()

    def _open_settings(self) -> None:
        before = self.config.place()
        dialog = SettingsDialog(self.config, self._preview)
        if dialog.exec() != SettingsDialog.DialogCode.Accepted:
            return

        after = self.config.place()
        if (after.id, after.tz_offset_hours) != (before.id, before.tz_offset_hours):
            # A new place means a new set of times, so the cached day and the
            # already-announced marker both have to go with it.
            self.timetable = PlaceTimetable(after)
            self.widget.set_timetable(self.timetable)
            self.tray.showMessage(APP_NAME, f"الموقع: {after.label}", _make_icon(), 4000)

        self.widget.apply_layout(self.config.compact)
        self._compact_action.setChecked(self.config.compact)
        autostart.set_enabled(self.config.start_with_windows)
        self._startup_action.setChecked(autostart.is_enabled())
        self._pending_reminder = None
        self._tick()

    def _preview(self, path: str | None) -> None:
        """Plays a recitation for the settings dialog, or stops whatever is playing."""
        if path is None:
            self._player.stop()
            return
        self._play_file(path)

    # --- ticking ------------------------------------------------------------------

    def _tick(self) -> None:
        now = datetime.now().replace(microsecond=0)
        self.widget.refresh(now)
        self._refresh_tray(now)
        self._announce_due(now)
        self._maybe_remind(now)

    def _refresh_tray(self, now: datetime) -> None:
        try:
            times = self.timetable.for_date(now.date(), self.config.offsets)
            upcoming = self.timetable.next_prayer(now, self.config.offsets)
        except KeyError:
            return

        for key, action in self._times_actions.items():
            marker = " ◀" if key == upcoming.key and not upcoming.tomorrow else ""
            action.setText(f"{ARABIC_NAMES[key]}   {times[key].strftime('%H:%M')}{marker}")

        self.tray.setToolTip(
            f"{upcoming.name} {upcoming.at.strftime('%H:%M')} — "
            f"{format_remaining(upcoming.at - now)}"
        )

    def _announce_due(self, now: datetime) -> None:
        last = self.config.last_announced_at()
        if last is None:
            # First ever run: start the clock here so the rest of today is not replayed.
            self.config.mark_announced(now)
            return

        due = self.timetable.due_since(last, now, self.config.offsets)
        if not due:
            return

        # Only the most recent matters. Waking a laptop that slept through three prayers
        # should not play three athans back to back.
        key, at = due[-1]
        self.config.mark_announced(now)

        if self.config.notifications:
            self.tray.showMessage(
                APP_NAME, f"حان وقت صلاة {ARABIC_NAMES[key]}", _make_icon(), 10000
            )
        if self.config.athan_sound:
            self.play_athan()

    def _maybe_remind(self, now: datetime) -> None:
        minutes = self.config.pre_athan_minutes
        if not minutes or not self.config.notifications:
            return
        try:
            upcoming = self.timetable.next_prayer(now, self.config.offsets)
        except KeyError:
            return

        remaining = upcoming.at - now
        if not (timedelta(0) < remaining <= timedelta(minutes=minutes)):
            return
        if self._pending_reminder == upcoming.at:
            return

        self._pending_reminder = upcoming.at
        self.tray.showMessage(
            APP_NAME, f"{upcoming.name} بعد {minutes} دقيقة", _make_icon(), 8000
        )

    # --- athan --------------------------------------------------------------------

    def play_athan(self) -> None:
        path = sounds.local_path(self.config.athan_file)
        if path is None:
            self._run(SoundFetch(self.config.athan_file), self._play_file)
            return
        self._play_file(str(path))

    def _play_file(self, path: str) -> None:
        if not Path(path).exists():
            return
        self._player.setSource(QUrl.fromLocalFile(path))
        self._player.play()

    def stop_athan(self) -> None:
        self._player.stop()

    def _on_playback_changed(self, state: QMediaPlayer.PlaybackState) -> None:
        self.widget.set_athan_playing(state == QMediaPlayer.PlaybackState.PlayingState)

    # --- updates ------------------------------------------------------------------

    def _start_update_check(self, announce_up_to_date: bool = False) -> None:
        thread = UpdateCheck()
        self._run(
            thread,
            lambda release: self._on_update(release, announce_up_to_date),
            signal="found",
        )

    def _on_update(self, release, announce_up_to_date: bool) -> None:
        if release is None:
            if announce_up_to_date:
                self.tray.showMessage(
                    APP_NAME, f"أنت على أحدث إصدار ({__version__})", _make_icon(), 5000
                )
            return

        box = QMessageBox()
        box.setLayoutDirection(Qt.LayoutDirection.RightToLeft)
        box.setWindowTitle(APP_NAME)
        box.setText(f"يتوفّر إصدار جديد: {release.tag}")
        if release.notes:
            box.setInformativeText(release.notes[:400])
        download = box.addButton("تنزيل", QMessageBox.ButtonRole.AcceptRole)
        box.addButton("لاحقاً", QMessageBox.ButtonRole.RejectRole)
        box.exec()

        if box.clickedButton() is download:
            webbrowser.open(release.url)

    # --- helpers ------------------------------------------------------------------

    def _run(self, thread: QThread, on_result, signal: str = "ready") -> None:
        # Keep a reference: a QThread garbage-collected while running crashes the process.
        self._threads.append(thread)
        getattr(thread, signal).connect(on_result)
        thread.finished.connect(lambda: self._threads.remove(thread))
        thread.start()

    def exec(self) -> int:
        return self.qt.exec()


def main() -> int:
    return PrayerApp().exec()
