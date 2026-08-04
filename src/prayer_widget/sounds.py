"""The athan catalogue.

Eleven recitations live in the user's assets repository, about 37 MB in total. Bundling
them all would quadruple the size of the executable for files most users never select, so
only the short recitation the Android app already ships is embedded. Everything else is
fetched the first time it is chosen and kept in the app data directory, which means a
recitation stays available offline once it has been played.
"""

from __future__ import annotations

import urllib.error
import urllib.request
from pathlib import Path

from .config import app_data_dir, bundled_path

ASSETS_BASE = (
    "https://raw.githubusercontent.com/NashatShamsadyn7/islamic-app-assets/main/azan"
)

#: The one shipped inside the executable, so a first run with no connection still works.
BUNDLED = "azan_makkah.mp3"

#: Display name -> file name in the assets repository.
CATALOGUE: dict[str, str] = {
    "الأذان المُضمَّن (قصير)": BUNDLED,
    "حسن العوّنان": "Hassan-Al-Awnan.mp3",
    "إسلام صبحي": "Islam-Sobhi.mp3",
    "محمد عبدالله": "Muhammad-Abdullah.mp3",
    "محمد آفاق لون": "Muhammad-Afaq-Lone.mp3",
    "محمد الحيدان": "Muhammad-Al-Haidan.mp3",
    "محمد مروان (قصير)": "Muhammad-Marwan-Qasir.mp3",
    "محمد مروان": "Muhammad-Marwan.mp3",
    "ناصر القطامي": "Nasser-Al-Qatami.mp3",
    "عمر هشام": "Omar-Hisham.mp3",
    "پێشەوا قادر (كردي)": "Peshawa-Qader-Kurdi.mp3",
    "رەعد (كردي)": "Raad-Kurdi.mp3",
}

_TIMEOUT_SECONDS = 30


def sounds_dir() -> Path:
    path = app_data_dir() / "sounds"
    path.mkdir(parents=True, exist_ok=True)
    return path


def local_path(file_name: str) -> Path | None:
    """Where this recitation already is on disk, or None if it must be downloaded."""
    if file_name == BUNDLED:
        return bundled_path("assets", BUNDLED)

    cached = sounds_dir() / file_name
    return cached if cached.exists() else None


def ensure(file_name: str) -> Path:
    """The recitation's path, downloading it if this is the first time it is used.

    Falls back to the bundled athan on any failure. Prayer time arriving in silence because
    a download failed would be a worse bug than playing the wrong recitation.
    """
    existing = local_path(file_name)
    if existing is not None and existing.exists():
        return existing

    target = sounds_dir() / file_name
    partial = target.with_suffix(target.suffix + ".part")
    try:
        request = urllib.request.Request(
            f"{ASSETS_BASE}/{file_name}",
            headers={"User-Agent": "islamic-nashat-desktop"},
        )
        with urllib.request.urlopen(request, timeout=_TIMEOUT_SECONDS) as response:
            if response.status != 200:
                raise OSError(f"HTTP {response.status}")
            partial.write_bytes(response.read())
        # Rename only once the whole file is on disk, so an interrupted download can never
        # leave a truncated mp3 that plays as a click and is then cached forever.
        partial.replace(target)
        return target
    except (urllib.error.URLError, TimeoutError, OSError):
        partial.unlink(missing_ok=True)
        return bundled_path("assets", BUNDLED)


def display_name(file_name: str) -> str:
    for name, value in CATALOGUE.items():
        if value == file_name:
            return name
    return file_name
