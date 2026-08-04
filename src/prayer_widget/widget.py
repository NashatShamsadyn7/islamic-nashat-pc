"""The floating countdown panel.

Two shapes. Compact is a single line - time, prayer, seconds ticking - meant to sit at the
edge of the screen and be read without being looked at. Card is the taller version for
anyone who wants it more legible.
"""

from __future__ import annotations

from datetime import datetime

from PySide6.QtCore import QPoint, Qt
from PySide6.QtGui import QColor, QFont, QMouseEvent, QPainter, QPainterPath
from PySide6.QtWidgets import QHBoxLayout, QLabel, QPushButton, QVBoxLayout, QWidget

from .timetable import Timetable, format_clock, format_remaining

BACKGROUND = QColor(18, 24, 30, 225)
BORDER = QColor(255, 255, 255, 28)
ACCENT = "#4ade80"
PRIMARY = "#f2f6fa"
MUTED = "#9aa7b4"
SEPARATOR = "#3d4852"

COMPACT_SIZE = (250, 36)
CARD_SIZE = (230, 132)


class PrayerWidget(QWidget):
    """Frameless, always on top, out of the taskbar and out of Alt-Tab.

    A tool window rather than a normal one: this is a glanceable readout, not something to
    switch to.
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
        # Never take focus: clicking must not pull the user out of what they were typing.
        self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating)
        self.setLayoutDirection(Qt.LayoutDirection.RightToLeft)

        self._build()
        self.apply_layout(getattr(config, "compact", True))
        self.move(config.position.get("x", 60), config.position.get("y", 60))

    def set_timetable(self, timetable: Timetable) -> None:
        self._timetable = timetable

    # --- construction -------------------------------------------------------------

    def _build(self) -> None:
        self._name = QLabel("—")
        self._time = QLabel("--:--")
        self._remaining = QLabel("")
        self._separators = [QLabel("|"), QLabel("|")]
        for sep in self._separators:
            sep.setStyleSheet(f"color: {SEPARATOR};")

        self._stop = QPushButton("إيقاف الأذان")
        self._stop.setCursor(Qt.CursorShape.PointingHandCursor)
        self._stop.setStyleSheet(
            "QPushButton {"
            "  background: rgba(248,113,113,0.16); color: #fca5a5;"
            "  border: 1px solid rgba(248,113,113,0.35); border-radius: 8px;"
            "  padding: 3px 8px; font-size: 11px;"
            "}"
            "QPushButton:hover { background: rgba(248,113,113,0.28); }"
        )
        self._stop.hide()

    def apply_layout(self, compact: bool) -> None:
        """Rebuilds the layout in place, so the mode can be switched without restarting."""
        self._compact = compact

        old = self.layout()
        if old is not None:
            while old.count():
                item = old.takeAt(0)
                if item.widget():
                    item.widget().setParent(None)
            QWidget().setLayout(old)

        if compact:
            self._apply_compact()
        else:
            self._apply_card()
        self.update()

    def _apply_compact(self) -> None:
        self.setFixedSize(*COMPACT_SIZE)
        layout = QHBoxLayout(self)
        layout.setContentsMargins(14, 0, 14, 0)
        layout.setSpacing(9)

        # Left to right on screen: clock, prayer, countdown. The layout direction is RTL,
        # so they are added in the mirrored order.
        self._time.setFont(QFont("Segoe UI", 12, QFont.Weight.DemiBold))
        self._time.setStyleSheet(f"color: {PRIMARY};")
        self._name.setFont(QFont("Segoe UI", 12, QFont.Weight.DemiBold))
        self._name.setStyleSheet(f"color: {ACCENT};")
        self._remaining.setFont(QFont("Consolas", 12))
        self._remaining.setStyleSheet(f"color: {MUTED};")

        for widget in (self._time, self._separators[0], self._name, self._separators[1]):
            widget.show()
            layout.addWidget(widget)
        self._remaining.show()
        layout.addWidget(self._remaining)
        layout.addStretch(1)
        layout.addWidget(self._stop)

    def _apply_card(self) -> None:
        self.setFixedSize(*CARD_SIZE)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(18, 14, 18, 14)
        layout.setSpacing(2)

        for sep in self._separators:
            sep.hide()

        self._name.setFont(QFont("Segoe UI", 15, QFont.Weight.DemiBold))
        self._name.setStyleSheet(f"color: {PRIMARY};")
        self._name.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._time.setFont(QFont("Segoe UI", 25, QFont.Weight.Bold))
        self._time.setStyleSheet(f"color: {ACCENT};")
        self._time.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._remaining.setFont(QFont("Segoe UI", 11))
        self._remaining.setStyleSheet(f"color: {MUTED};")
        self._remaining.setAlignment(Qt.AlignmentFlag.AlignCenter)

        for widget in (self._name, self._time, self._remaining, self._stop):
            widget.show() if widget is not self._stop else None
            layout.addWidget(widget)

    # --- painting -----------------------------------------------------------------

    def paintEvent(self, event) -> None:  # noqa: N802 - Qt naming
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        path = QPainterPath()
        radius = self.height() / 2 if self._compact else 16
        path.addRoundedRect(self.rect().adjusted(0, 0, -1, -1), radius, radius)
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
        # Persisted here rather than on every move, so a drag writes the file once.
        self._config.position = {"x": self.x(), "y": self.y()}
        self._config.save()

    # --- state --------------------------------------------------------------------

    def refresh(self, now: datetime) -> None:
        try:
            upcoming = self._timetable.next_prayer(now, self._config.offsets)
        except (KeyError, OSError):
            self._name.setText("لا توجد بيانات")
            self._time.setText("--:--")
            self._remaining.setText("")
            return

        remaining = upcoming.at - now
        self._name.setText(upcoming.name)
        self._time.setText(upcoming.at.strftime("%H:%M"))
        self._remaining.setText(
            format_clock(remaining) if self._compact else format_remaining(remaining)
        )

    def set_athan_playing(self, playing: bool) -> None:
        self._stop.setVisible(playing)

    @property
    def stop_button(self) -> QPushButton:
        return self._stop
