"""Settings: where the times are for, and which athan plays."""

from __future__ import annotations

from PySide6.QtCore import Qt, QThread, QTimer, Signal
from PySide6.QtWidgets import (
    QCheckBox,
    QComboBox,
    QDialog,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QListWidget,
    QListWidgetItem,
    QPushButton,
    QSpinBox,
    QTabWidget,
    QVBoxLayout,
    QWidget,
)

from . import locations, sounds
from .locations import Place

STYLE = """
QDialog { background: #141a20; }
QLabel { color: #e6edf3; }
QLabel[hint="true"] { color: #9aa7b4; font-size: 11px; }
QLabel[warn="true"] { color: #fbbf24; font-size: 11px; }
QLineEdit, QComboBox, QSpinBox {
    background: #1d252d; color: #e6edf3; border: 1px solid #2f3a45;
    border-radius: 6px; padding: 6px;
}
QListWidget {
    background: #1d252d; color: #e6edf3; border: 1px solid #2f3a45; border-radius: 6px;
}
QListWidget::item:selected { background: #23503b; color: #d7f7e4; }
QPushButton {
    background: #23303b; color: #e6edf3; border: 1px solid #35424f;
    border-radius: 6px; padding: 6px 14px;
}
QPushButton:hover { background: #2c3a47; }
QPushButton:disabled { color: #5c6874; }
QCheckBox { color: #e6edf3; }
QTabWidget::pane { border: 1px solid #2f3a45; border-radius: 6px; }
QTabBar::tab { background: #1d252d; color: #9aa7b4; padding: 7px 16px; }
QTabBar::tab:selected { background: #23303b; color: #e6edf3; }
"""


class _Detect(QThread):
    done = Signal(object)

    def run(self) -> None:
        self.done.emit(locations.detect())


class _Fetch(QThread):
    done = Signal(str)

    def __init__(self, file_name: str) -> None:
        super().__init__()
        self._file_name = file_name

    def run(self) -> None:
        self.done.emit(str(sounds.ensure(self._file_name)))


class SettingsDialog(QDialog):
    """Returns the chosen place through :attr:`selected_place` when accepted."""

    def __init__(self, config, preview, parent=None) -> None:
        super().__init__(parent)
        self.config = config
        #: Callable taking a path to play, or None to stop. Owned by the app so the dialog
        #: does not need a second audio device.
        self._preview = preview
        self.selected_place: Place | None = None
        self._threads: list[QThread] = []

        self.setWindowTitle("الإعدادات")
        self.setLayoutDirection(Qt.LayoutDirection.RightToLeft)
        self.setStyleSheet(STYLE)
        self.setMinimumWidth(430)

        tabs = QTabWidget()
        tabs.addTab(self._location_tab(), "الموقع")
        tabs.addTab(self._sound_tab(), "الأذان")
        tabs.addTab(self._general_tab(), "عام")

        buttons = QHBoxLayout()
        buttons.addStretch(1)
        cancel = QPushButton("إلغاء")
        cancel.clicked.connect(self.reject)
        save = QPushButton("حفظ")
        save.setDefault(True)
        save.clicked.connect(self._save)
        buttons.addWidget(cancel)
        buttons.addWidget(save)

        layout = QVBoxLayout(self)
        layout.addWidget(tabs)
        layout.addLayout(buttons)

    # --- location -----------------------------------------------------------------

    def _location_tab(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)

        self._current = QLabel()
        layout.addWidget(self._current)

        self._auto = QPushButton("تحديد موقعي تلقائياً")
        self._auto.clicked.connect(self._detect)
        layout.addWidget(self._auto)

        note = QLabel(
            "التحديد التلقائي يعتمد على الاتصال بالإنترنت لا على GPS — "
            "دقّته على مستوى المدينة، ويعطي نتيجة خاطئة عند استعمال VPN."
        )
        note.setProperty("hint", "true")
        note.setWordWrap(True)
        layout.addWidget(note)

        self._search = QLineEdit()
        self._search.setPlaceholderText("ابحث عن مدينة… (بالإنجليزية)")
        # Searching on every keystroke over 166k rows is wasteful; wait for a pause.
        self._debounce = QTimer(self)
        self._debounce.setSingleShot(True)
        self._debounce.setInterval(220)
        self._debounce.timeout.connect(self._run_search)
        self._search.textChanged.connect(lambda _: self._debounce.start())
        layout.addWidget(self._search)

        self._results = QListWidget()
        self._results.setMinimumHeight(190)
        self._results.currentItemChanged.connect(self._on_pick)
        layout.addWidget(self._results)

        self._accuracy = QLabel()
        self._accuracy.setWordWrap(True)
        layout.addWidget(self._accuracy)

        self._show_place(self.config.place())
        return page

    def _show_place(self, place: Place) -> None:
        self.selected_place = place
        self._current.setText(f"الموقع الحالي:  {place.label}")
        if place.has_fixed_timetable:
            self._accuracy.setProperty("warn", "false")
            self._accuracy.setProperty("hint", "true")
            self._accuracy.setText(
                "✓ جدول ثابت — مطابق لتطبيق الهاتف تماماً."
            )
        else:
            self._accuracy.setProperty("hint", "false")
            self._accuracy.setProperty("warn", "true")
            self._accuracy.setText(
                "⚠ لا يوجد جدول ثابت لهذه المدينة، فتُحسب فلكياً بالخوارزمية نفسها "
                "التي يستعملها الهاتف. 252 مدينة فقط تملك جدولاً ثابتاً."
            )
        self._accuracy.style().unpolish(self._accuracy)
        self._accuracy.style().polish(self._accuracy)

    def _run_search(self) -> None:
        self._results.clear()
        for place in locations.search(self._search.text()):
            mark = "✓" if place.has_fixed_timetable else " "
            item = QListWidgetItem(f"{mark}  {place.label}")
            item.setData(Qt.ItemDataRole.UserRole, place)
            self._results.addItem(item)

    def _on_pick(self, item: QListWidgetItem | None) -> None:
        if item is not None:
            self._show_place(item.data(Qt.ItemDataRole.UserRole))

    def _detect(self) -> None:
        self._auto.setEnabled(False)
        self._auto.setText("جارٍ التحديد…")
        thread = _Detect()
        self._start(thread, self._on_detected, "done")

    def _on_detected(self, result) -> None:
        self._auto.setEnabled(True)
        self._auto.setText("تحديد موقعي تلقائياً")
        if result is None:
            self._current.setText("تعذّر التحديد — تحقّق من الاتصال أو اختر يدوياً")
            return

        place, distance_km = result
        self._show_place(place)
        if distance_km > 25:
            # Being honest about this matters: prayer times shift by roughly a minute per
            # 20 km of longitude, so a far-off match is quietly wrong.
            self._accuracy.setProperty("hint", "false")
            self._accuracy.setProperty("warn", "true")
            self._accuracy.setText(
                f"⚠ أقرب مدينة معروفة تبعد {distance_km:.0f} كم عن موقعك المُكتشف، "
                "وقد تختلف الأوقات بدقائق. اختر مدينتك يدوياً إن كانت في القائمة."
            )
            self._accuracy.style().unpolish(self._accuracy)
            self._accuracy.style().polish(self._accuracy)

    # --- sound --------------------------------------------------------------------

    def _sound_tab(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)

        layout.addWidget(QLabel("اختر الأذان، واستمع إليه قبل أن تحفظ:"))

        self._sounds = QListWidget()
        for label, file_name in sounds.CATALOGUE.items():
            cached = sounds.local_path(file_name) is not None
            item = QListWidgetItem(f"{label}{'' if cached else '   ⤓'}")
            item.setData(Qt.ItemDataRole.UserRole, file_name)
            self._sounds.addItem(item)
            if file_name == self.config.athan_file:
                self._sounds.setCurrentItem(item)
        layout.addWidget(self._sounds)

        controls = QHBoxLayout()
        play = QPushButton("▶  استماع")
        play.clicked.connect(self._play_selected)
        stop = QPushButton("■  إيقاف")
        stop.clicked.connect(lambda: self._preview(None))
        controls.addWidget(play)
        controls.addWidget(stop)
        controls.addStretch(1)
        layout.addLayout(controls)

        self._sound_status = QLabel("⤓ تعني أن الملف يُنزَّل عند أول استماع، ثم يعمل بلا إنترنت.")
        self._sound_status.setProperty("hint", "true")
        self._sound_status.setWordWrap(True)
        layout.addWidget(self._sound_status)

        self._athan_on = QCheckBox("تشغيل الأذان عند دخول الوقت")
        self._athan_on.setChecked(self.config.athan_sound)
        layout.addWidget(self._athan_on)
        return page

    def _play_selected(self) -> None:
        item = self._sounds.currentItem()
        if item is None:
            return
        file_name = item.data(Qt.ItemDataRole.UserRole)

        existing = sounds.local_path(file_name)
        if existing is not None:
            self._preview(str(existing))
            return

        self._sound_status.setText("جارٍ التنزيل…")
        thread = _Fetch(file_name)
        self._start(thread, self._on_fetched, "done")

    def _on_fetched(self, path: str) -> None:
        self._sound_status.setText("جاهز — يعمل بلا إنترنت من الآن.")
        item = self._sounds.currentItem()
        if item is not None:
            item.setText(item.text().replace("   ⤓", ""))
        self._preview(path)

    # --- general ------------------------------------------------------------------

    def _general_tab(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)

        self._compact = QCheckBox("شريط مضغوط بسطر واحد")
        self._compact.setChecked(getattr(self.config, "compact", True))
        layout.addWidget(self._compact)

        self._notify = QCheckBox("إشعار ويندوز عند دخول الوقت")
        self._notify.setChecked(self.config.notifications)
        layout.addWidget(self._notify)

        self._startup = QCheckBox("التشغيل مع ويندوز")
        self._startup.setChecked(self.config.start_with_windows)
        layout.addWidget(self._startup)

        self._updates = QCheckBox("التحقّق من التحديثات")
        self._updates.setChecked(self.config.check_for_updates)
        layout.addWidget(self._updates)

        row = QHBoxLayout()
        row.addWidget(QLabel("تنبيه قبل الأذان بـ"))
        self._pre = QComboBox()
        self._pre.addItem("معطّل", 0)
        for minutes in (5, 10, 15, 20, 30):
            self._pre.addItem(f"{minutes} دقيقة", minutes)
        index = self._pre.findData(self.config.pre_athan_minutes)
        self._pre.setCurrentIndex(index if index >= 0 else 0)
        row.addWidget(self._pre)
        row.addStretch(1)
        layout.addLayout(row)

        layout.addWidget(QLabel("إزاحة الأوقات بالدقائق:"))
        self._offsets: dict[str, QSpinBox] = {}
        from .timetable import ARABIC_NAMES, DISPLAY_KEYS

        grid = QHBoxLayout()
        for key in DISPLAY_KEYS:
            column = QVBoxLayout()
            name = QLabel(ARABIC_NAMES[key])
            name.setProperty("hint", "true")
            name.setAlignment(Qt.AlignmentFlag.AlignCenter)
            box = QSpinBox()
            box.setRange(-60, 60)
            box.setValue(int(self.config.offsets.get(key, 0)))
            box.setFixedWidth(56)
            column.addWidget(name)
            column.addWidget(box)
            grid.addLayout(column)
            self._offsets[key] = box
        layout.addLayout(grid)
        layout.addStretch(1)
        return page

    # --- saving -------------------------------------------------------------------

    def _save(self) -> None:
        self._preview(None)
        item = self._sounds.currentItem()
        if item is not None:
            self.config.athan_file = item.data(Qt.ItemDataRole.UserRole)

        self.config.athan_sound = self._athan_on.isChecked()
        self.config.compact = self._compact.isChecked()
        self.config.notifications = self._notify.isChecked()
        self.config.check_for_updates = self._updates.isChecked()
        self.config.start_with_windows = self._startup.isChecked()
        self.config.pre_athan_minutes = self._pre.currentData()
        self.config.offsets = {key: box.value() for key, box in self._offsets.items()}
        if self.selected_place is not None:
            self.config.location = self.selected_place.to_dict()

        self.config.save()
        self.accept()

    def reject(self) -> None:
        self._preview(None)
        super().reject()

    def _start(self, thread: QThread, on_result, signal: str) -> None:
        # Hold a reference: a QThread collected while running takes the process with it.
        self._threads.append(thread)
        getattr(thread, signal).connect(on_result)
        thread.finished.connect(lambda: self._threads.remove(thread))
        thread.start()
