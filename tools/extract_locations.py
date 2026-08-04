"""Builds the widget's location database from the Android app's bundled muslim-data file.

Only three tables are copied. The source is 28.8 MB because it also carries the Quran,
azkar and the names of Allah, none of which this widget shows.
"""

from __future__ import annotations

import sqlite3
import sys
from pathlib import Path

SOURCE = Path(
    r"c:\Users\nasha\Downloads\Islamic_Nashat_Complete"
    r"\app\src\main\assets\databases\muslim_db_v2.5.1.db"
)
TARGET = Path(sys.argv[1] if len(sys.argv) > 1 else "assets/locations.db")


def main() -> None:
    if TARGET.exists():
        TARGET.unlink()
    TARGET.parent.mkdir(parents=True, exist_ok=True)

    out = sqlite3.connect(TARGET)
    out.execute("ATTACH DATABASE ? AS src", (str(SOURCE),))

    out.executescript(
        """
        CREATE TABLE country (
            _id INTEGER PRIMARY KEY,
            code TEXT NOT NULL,
            name TEXT NOT NULL
        );
        CREATE TABLE location (
            _id INTEGER PRIMARY KEY,
            country_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            latitude REAL NOT NULL,
            longitude REAL NOT NULL,
            has_fixed_prayer_time INTEGER NOT NULL DEFAULT 0,
            prayer_dependent_id INTEGER
        );
        CREATE TABLE prayer_time (
            location_id INTEGER NOT NULL,
            date TEXT NOT NULL,
            fajr TEXT NOT NULL,
            sunrise TEXT NOT NULL,
            dhuhr TEXT NOT NULL,
            asr TEXT NOT NULL,
            maghrib TEXT NOT NULL,
            isha TEXT NOT NULL,
            PRIMARY KEY (location_id, date)
        ) WITHOUT ROWID;
        """
    )

    out.execute("INSERT INTO country SELECT _id, code, name FROM src.country")
    out.execute(
        "INSERT INTO location SELECT _id, country_id, name, latitude, longitude, "
        "has_fixed_prayer_time, prayer_dependent_id FROM src.location"
    )
    out.execute(
        "INSERT INTO prayer_time SELECT location_id, date, fajr, sunrise, dhuhr, asr, "
        "maghrib, isha FROM src.prayer_time"
    )

    # Search is by name prefix, and the nearest-city lookup scans coordinates; without
    # these a 166k-row table makes every keystroke in the picker visibly slow.
    out.executescript(
        """
        CREATE INDEX idx_location_name ON location(name COLLATE NOCASE);
        CREATE INDEX idx_location_coords ON location(latitude, longitude);
        """
    )

    out.commit()
    out.execute("DETACH DATABASE src")
    out.execute("VACUUM")
    out.close()

    counts = sqlite3.connect(TARGET)
    for table in ("country", "location", "prayer_time"):
        n = counts.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0]
        print(f"{table:<12} {n:>7} rows")
    print(f"\n{TARGET}  {TARGET.stat().st_size / 1e6:.1f} MB")


if __name__ == "__main__":
    main()
