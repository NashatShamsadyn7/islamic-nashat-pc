"""User settings, stored as plain JSON so they can be edited by hand."""

from __future__ import annotations

import json
import os
import sys
from dataclasses import asdict, dataclass, field
from datetime import datetime
from pathlib import Path

APP_DIR_NAME = "IslamicNashatWidget"


def app_data_dir() -> Path:
    """Where settings and downloaded sounds live.

    Never beside the executable: a user who drops the exe in Program Files or on a
    read-only share would otherwise lose every setting silently.
    """
    base = os.environ.get("APPDATA") or Path.home()
    path = Path(base) / APP_DIR_NAME
    path.mkdir(parents=True, exist_ok=True)
    return path


def bundled_path(*parts: str) -> Path:
    """Resolve a file shipped inside the app, whether frozen by PyInstaller or not."""
    if getattr(sys, "frozen", False):
        root = Path(getattr(sys, "_MEIPASS"))
    else:
        root = Path(__file__).resolve().parent.parent.parent
    return root.joinpath(*parts)


@dataclass
class Config:
    notifications: bool = True
    athan_sound: bool = True
    athan_file: str = "azan_makkah.mp3"
    pre_athan_minutes: int = 0
    offsets: dict[str, int] = field(
        default_factory=lambda: {
            "fajr": 0,
            "sunrise": 0,
            "dhuhr": 0,
            "asr": 0,
            "maghrib": 0,
            "isha": 0,
        }
    )
    start_with_windows: bool = False
    check_for_updates: bool = True
    widget_visible: bool = True
    #: One-line bar rather than the taller card.
    compact: bool = True
    #: The chosen place, as produced by Place.to_dict(). None means the built-in default.
    location: dict | None = None
    position: dict[str, int] = field(default_factory=lambda: {"x": 60, "y": 60})
    #: ISO timestamp of the last prayer announced, so a restart does not replay it.
    last_announced: str | None = None

    @property
    def path(self) -> Path:
        return app_data_dir() / "settings.json"

    @classmethod
    def load(cls) -> "Config":
        path = app_data_dir() / "settings.json"
        if not path.exists():
            return cls()
        try:
            stored = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            # A corrupted settings file must not stop the app from telling the time.
            return cls()

        config = cls()
        for key, value in stored.items():
            if hasattr(config, key):
                setattr(config, key, value)
        return config

    def save(self) -> None:
        try:
            self.path.write_text(
                json.dumps(asdict(self), ensure_ascii=False, indent=1), encoding="utf-8"
            )
        except OSError:
            pass

    def last_announced_at(self) -> datetime | None:
        if not self.last_announced:
            return None
        try:
            return datetime.fromisoformat(self.last_announced)
        except ValueError:
            return None

    def mark_announced(self, moment: datetime) -> None:
        self.last_announced = moment.isoformat(timespec="seconds")
        self.save()

    def place(self):
        """The chosen place, falling back to the default if the stored one is unusable.

        Imported here rather than at module scope because locations imports config.
        """
        from .locations import BARDARASH, Place

        if not self.location:
            return BARDARASH
        try:
            return Place.from_dict(self.location)
        except (KeyError, TypeError, ValueError):
            # A settings file edited by hand into something invalid should not stop the
            # widget from showing times.
            return BARDARASH
