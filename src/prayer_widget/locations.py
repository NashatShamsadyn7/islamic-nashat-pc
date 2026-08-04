"""Choosing where the prayer times are for.

Two ways in: search 166,404 known places by name, or let the app work it out from the
network connection. Whichever is used, the result resolves through the same two-tier logic
the Android app uses - a fixed timetable when the place has one, the astronomical
calculation otherwise.
"""

from __future__ import annotations

import json
import math
import sqlite3
import urllib.error
import urllib.request
from dataclasses import dataclass
from datetime import date as Date
from datetime import datetime, time, timedelta
from pathlib import Path

from .calculator import PrayerCalculator, method_for_country
from .config import bundled_path
from .timetable import DISPLAY_KEYS, Timetable

_TIMEOUT_SECONDS = 8


@dataclass(frozen=True)
class Place:
    id: int
    name: str
    country_code: str
    latitude: float
    longitude: float
    has_fixed_timetable: bool
    #: Some cities share a neighbour's timetable; this is whose rows to read.
    timetable_id: int | None = None
    #: Hours east of UTC. None means "use whatever this machine is set to", which is right
    #: for the common case of the widget running in the city it is showing.
    tz_offset_hours: float | None = None

    @property
    def label(self) -> str:
        return f"{self.name} ({self.country_code})"

    @property
    def source_id(self) -> int:
        return self.timetable_id or self.id

    def to_dict(self) -> dict:
        return {
            "id": self.id,
            "name": self.name,
            "country_code": self.country_code,
            "latitude": self.latitude,
            "longitude": self.longitude,
            "has_fixed_timetable": self.has_fixed_timetable,
            "timetable_id": self.timetable_id,
            "tz_offset_hours": self.tz_offset_hours,
        }

    @classmethod
    def from_dict(cls, data: dict) -> "Place":
        return cls(
            id=int(data["id"]),
            name=data["name"],
            country_code=data.get("country_code", ""),
            latitude=float(data["latitude"]),
            longitude=float(data["longitude"]),
            has_fixed_timetable=bool(data.get("has_fixed_timetable", False)),
            timetable_id=data.get("timetable_id"),
            tz_offset_hours=data.get("tz_offset_hours"),
        )


#: Where the app starts before anyone chooses anything.
BARDARASH = Place(
    id=77353,
    name="Bardarash",
    country_code="IQ",
    latitude=36.501899,
    longitude=43.584857,
    has_fixed_timetable=True,
)


def _database() -> sqlite3.Connection:
    path = bundled_path("assets", "locations.db")
    # Read-only: the file lives inside the bundle and is never written to.
    connection = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
    connection.row_factory = sqlite3.Row
    return connection


_SELECT = """
    SELECT l._id AS id, l.name AS name, c.code AS country_code,
           l.latitude AS latitude, l.longitude AS longitude,
           l.has_fixed_prayer_time AS has_fixed, l.prayer_dependent_id AS dependent
    FROM location l JOIN country c ON c._id = l.country_id
"""


def _to_place(row: sqlite3.Row) -> Place:
    return Place(
        id=row["id"],
        name=row["name"],
        country_code=row["country_code"],
        latitude=row["latitude"],
        longitude=row["longitude"],
        has_fixed_timetable=bool(row["has_fixed"]),
        timetable_id=row["dependent"],
    )


def search(query: str, limit: int = 40) -> list[Place]:
    """Places whose name starts with, or contains, ``query``.

    Prefix matches come first: someone typing "bag" wants Baghdad, not "Kafr Bagdad".
    Places with a fixed timetable are surfaced above calculated ones at equal relevance,
    because those are the ones guaranteed to agree with the phone.
    """
    query = query.strip()
    if len(query) < 2:
        return []

    with _database() as db:
        rows = db.execute(
            _SELECT
            + """
            WHERE l.name LIKE ? COLLATE NOCASE
            ORDER BY
              CASE WHEN l.name LIKE ? COLLATE NOCASE THEN 0 ELSE 1 END,
              l.has_fixed_prayer_time DESC,
              LENGTH(l.name),
              l.name
            LIMIT ?
            """,
            (f"%{query}%", f"{query}%", limit),
        ).fetchall()
    return [_to_place(row) for row in rows]


def nearest(latitude: float, longitude: float, prefer_fixed_within_km: float = 40.0) -> Place | None:
    """The closest known place to a coordinate.

    A place with a fixed timetable within ``prefer_fixed_within_km`` wins over a merely
    closer one without: agreeing with the phone matters more than a few kilometres of
    precision, and the fixed tables are city-wide anyway.
    """
    # A degree of latitude is ~111 km everywhere; longitude shrinks toward the poles. This
    # box is only a prefilter so the distance sort runs over a handful of rows rather than
    # all 166,404.
    span = 1.5
    with _database() as db:
        rows = db.execute(
            _SELECT
            + """
            WHERE l.latitude BETWEEN ? AND ? AND l.longitude BETWEEN ? AND ?
            """,
            (latitude - span, latitude + span, longitude - span, longitude + span),
        ).fetchall()

    if not rows:
        return None

    places = [_to_place(row) for row in rows]
    scored = sorted(places, key=lambda p: _distance_km(latitude, longitude, p.latitude, p.longitude))

    for place in scored:
        if place.has_fixed_timetable:
            if _distance_km(latitude, longitude, place.latitude, place.longitude) <= prefer_fixed_within_km:
                return place
            break
    return scored[0]


def _distance_km(lat1: float, lon1: float, lat2: float, lon2: float) -> float:
    """Great-circle distance. Haversine rather than a flat approximation, so the result
    stays sane near the poles and across the date line."""
    radius = 6371.0
    d_lat = math.radians(lat2 - lat1)
    d_lon = math.radians(lon2 - lon1)
    a = (
        math.sin(d_lat / 2) ** 2
        + math.cos(math.radians(lat1)) * math.cos(math.radians(lat2)) * math.sin(d_lon / 2) ** 2
    )
    return 2 * radius * math.asin(math.sqrt(a))


def detect() -> tuple[Place, float] | None:
    """Works out the current place from the network connection.

    A desktop has no GPS, so this is IP geolocation: accurate to the city, wrong on a VPN.
    Returns the matched place and the distance in kilometres between the reported
    coordinate and that place, so the caller can warn when the nearest known city is far
    away and its times will not really apply.
    """
    fix = _geolocate()
    if fix is None:
        return None

    latitude, longitude, offset_hours = fix
    place = nearest(latitude, longitude)
    if place is None:
        return None

    distance = _distance_km(latitude, longitude, place.latitude, place.longitude)
    if offset_hours is not None:
        place = Place(**{**place.to_dict(), "tz_offset_hours": offset_hours})
    return place, distance


def _geolocate() -> tuple[float, float, float | None] | None:
    """Latitude, longitude and UTC offset in hours, from whichever service answers."""
    providers = (
        ("https://ipapi.co/json/", "latitude", "longitude", None),
        ("http://ip-api.com/json/?fields=status,lat,lon,offset", "lat", "lon", "offset"),
    )

    for url, lat_key, lon_key, offset_key in providers:
        try:
            request = urllib.request.Request(
                url, headers={"User-Agent": "islamic-nashat-desktop"}
            )
            with urllib.request.urlopen(request, timeout=_TIMEOUT_SECONDS) as response:
                if response.status != 200:
                    continue
                payload = json.loads(response.read().decode("utf-8"))
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError, OSError):
            continue

        if lat_key not in payload or lon_key not in payload:
            continue

        offset = None
        if offset_key and isinstance(payload.get(offset_key), (int, float)):
            offset = payload[offset_key] / 3600.0
        try:
            return float(payload[lat_key]), float(payload[lon_key]), offset
        except (TypeError, ValueError):
            continue

    return None


class PlaceTimetable(Timetable):
    """Prayer times for a chosen place, resolved the same way the Android app resolves them.

    Subclasses :class:`Timetable` so the countdown, the midnight rollover and the
    already-announced tracking are the exact code the JSON-backed version is tested
    against; only the day lookup changes.
    """

    def __init__(self, place: Place) -> None:
        self.place = place
        self.city = place.name
        self._cache: dict[Date, dict[str, time]] = {}
        self._source: str = "fixed" if place.has_fixed_timetable else "calculated"

    @property
    def source(self) -> str:
        """``fixed`` when read from the shared timetable, ``calculated`` otherwise."""
        return self._source

    def for_date(self, day: Date, offsets: dict[str, int] | None = None) -> dict[str, time]:
        base = self._cache.get(day)
        if base is None:
            base = self._fixed_times(day) if self.place.has_fixed_timetable else None
            if base is None:
                # A place flagged as fixed but missing a row would otherwise raise and
                # leave the widget blank; falling through keeps it useful.
                base = self._calculated_times(day)
                self._source = "calculated"
            else:
                self._source = "fixed"
            self._cache[day] = base

        offsets = offsets or {}
        if not any(offsets.values()):
            return base
        return {
            key: (
                datetime.combine(day, base[key]) + timedelta(minutes=offsets.get(key, 0))
            ).time()
            for key in DISPLAY_KEYS
        }

    def _fixed_times(self, day: Date) -> dict[str, time] | None:
        with _database() as db:
            row = db.execute(
                "SELECT fajr, sunrise, dhuhr, asr, maghrib, isha FROM prayer_time "
                "WHERE location_id = ? AND date = ?",
                (self.place.source_id, day.strftime("%m-%d")),
            ).fetchone()
        if row is None:
            return None
        return {key: _parse(row[key]) for key in DISPLAY_KEYS}

    def _calculated_times(self, day: Date) -> dict[str, time]:
        calculator = PrayerCalculator(
            method=method_for_country(self.place.country_code),
            asr_juristic=0,
            adjust_high_lats=3,
        )
        computed = calculator.prayer_times(
            day, self.place.latitude, self.place.longitude, self._offset_for(day)
        )
        mapping = {
            "fajr": "Fajr",
            "sunrise": "Sunrise",
            "dhuhr": "Dhuhr",
            "asr": "Asr",
            "maghrib": "Maghrib",
            "isha": "Isha",
        }
        return {key: _parse(computed[name]) for key, name in mapping.items()}

    def _offset_for(self, day: Date) -> float:
        if self.place.tz_offset_hours is not None:
            return self.place.tz_offset_hours
        # Ask the machine for the offset *on that day*, so a date the far side of a
        # daylight-saving change is still an hour right.
        local = datetime.combine(day, time(12, 0)).astimezone()
        return (local.utcoffset() or timedelta()).total_seconds() / 3600.0


def _parse(value: str) -> time:
    """Accepts ``HH:MM`` from the database and ``HH:MM:SS`` from the calculator."""
    parts = [int(part) for part in value.split(":")]
    return time(parts[0], parts[1])
