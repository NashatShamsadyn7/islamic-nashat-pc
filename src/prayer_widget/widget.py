"""The floating countdown panel."""

from __future__ import annotations

from datetime import datetime

from PySide6.QtCore import Qt, QPoint
from PySide6.QtGui import QColor, QFont, QMouseEvent, QPainter, QPainterPath
from PySide6.QtWidgets import QLabel, QPushButton, QVBoxLayout, QWidget

from .timetable import Timetable, format_remaining

BACKGROUND = QColor(18, 24, 30, 225)
BORDER = QColor(255, 255, 255, 28)
ACCENT = "#4ade80"
MUTED = "#9aa7b4"


class PrayerWidget(QWidget):
    """A frameless always-on-top panel showing the next prayer and the time left.

    Tool window rather than a normal one, so it stays out of the taskbar and out of
    Alt-Tab: this is a glanceable readout, not something to switch to.
    """

    def __init__(self, timetable: Timetable, config) -> None:
        super().__init__()
        self._timetable = timetable
        self._config = config
        self._drag_origin: QPoint | None = None

        self.setWindowFlags(
            Qt.WindowType.FramelessWindowHint
            | Qt.WindowType.WindowStaysOnTopHint
            | Qt.WindowType.Tool
        )
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        # Never take focus: clicking the widget must not pull the user out of whatever
        # they were typing in.
        self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating)
        self.setLayoutDirection(Qt.LayoutDirection.RightToLeft)
        self.setFixedSize(230, 132)

        self._build_ui()
        self.move(config.position.get("x", 60), config.position.get("y", 60))

    def _build_ui(self) -> None:
        layout = QVBoxLayout(self)
        layout.setContentsMargins(18, 16, 18, 16)
        layout.setSpacing(2)

        self._name = QLabel("—")
        self._name.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._name.setFont(QFont("Segoe UI", 15, QFont.Weight.DemiBold))
        self._name.setStyleSheet("color: #f2f6fa;")

        self._time = QLabel("--:--")
        self._time.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._time.setFont(QFont("Segoe UI", 25, QFont.Weight.Bold))
        self._time.setStyleSheet(f"color: {ACCENT};")

        self._remaining = QLabel("")
        self._remaining.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._remaining.setFont(QFont("Segoe UI", 11))
        self._remaining.setStyleSheet(f"color: {MUTED};")

        self._stop = QPushButton("إيقاف الأذان")
        self._stop.setCursor(Qt.CursorShape.PointingHandCursor)
        self._stop.setStyleSheet(
            "QPushButton {"
            "  background: rgba(248,113,113,0.16); color: #fca5a5;"
            "  border: 1px solid rgba(248,113,113,0.35); border-radius: 8px;"
            "  padding: 4px; font-size: 11px;"
            "}"
            "QPushButton:hover { background: rgba(248,113,113,0.28); }"
        )
        self._stop.hide()

        layout.addWidget(self._name)
        layout.addWidget(self._time)
        layout.addWidget(self._remaining)
        layout.addWidget(self._stop)

    # --- painting -----------------------------------------------------------------

    def paintEvent(self, event) -> None:  # noqa: N802 - Qt naming
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        path = QPainterPath()
        path.addRoundedRect(self.rect().adjusted(0, 0, -1, -1), 16, 16)
        painter.fillPath(path, BACKGROUND)
        painter.setPen(BORDER)
        painter.drawPath(path)

    # --- dragging -----------------------------------------------------------------

    def mousePressEvent(self, event: QMouseEvent) -> None:  # noqa: N802
        if event.button() == Qt.MouseButton.LeftButton:
            self._drag_origin = event.globalPosition().toPoint() - self.pos()

    def mouseMoveEvent(self, event: QMouseEvent) -> None:  # noqa: N802
        if self._drag_origin is not None:
            self.move(event.globalPosition().toPoint() - self._drag_origin)

    def mouseReleaseEvent(self, event: QMouseEvent) -> None:  # noqa: N802
        if self._drag_origin is None:
            return
        self._drag_origin = None
        # Persist here rather than on every move, so a drag writes the file once.
        self._config.position = {"x": self.x(), "y": self.y()}
        self._config.save()

    # --- state --------------------------------------------------------------------

    def refresh(self, now: datetime) -> None:
        try:
            upcoming = self._timetable.next_prayer(now, self._config.offsets)
        except KeyError:
            # The table covers a full year, so this only happens if it was replaced with a
            # partial one. Saying so beats showing a stale countdown.
            self._name.setText("لا توجد بيانات")
            self._time.setText("--:--")
            self._remaining.setText("")
            return

        self._name.setText(upcoming.name)
        self._time.setText(upcoming.at.strftime("%H:%M"))
        self._remaining.setText(format_remaining(upcoming.at - now))

    def set_athan_playing(self, playing: bool) -> None:
        self._stop.setVisible(playing)

    @property
    def stop_button(self) -> QPushButton:
        return self._stop
