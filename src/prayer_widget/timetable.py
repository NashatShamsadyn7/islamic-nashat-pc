"""Prayer times, read from a fixed timetable.

The Android app this mirrors does not calculate Bardarash's times astronomically - the city
is flagged ``has_fixed_prayer_time`` in its bundled muslim-data database, so the app looks
up a pre-computed row keyed by ``MM-DD``. Reading the same table is the only way for a
second client to be guaranteed to agree with the phone to the minute, so there is
deliberately no astronomy anywhere in this module.

Everything here is pure: no Qt, no filesystem beyond loading the table, no clock reads
except through an explicit ``now`` argument. That keeps the parts that are easy to get
wrong - midnight rollover, a prayer at the current minute, resuming after sleep - directly
testable.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import date as Date
from datetime import datetime, time, timedelta
from pathlib import Path

# Sunrise is in the table because the phone shows it, but it is not a prayer: it is never
# counted down to and never announced.
PRAYER_KEYS: tuple[str, ...] = ("fajr", "dhuhr", "asr", "maghrib", "isha")
DISPLAY_KEYS: tuple[str, ...] = ("fajr", "sunrise", "dhuhr", "asr", "maghrib", "isha")

ARABIC_NAMES: dict[str, str] = {
    "fajr": "الفجر",
    "sunrise": "الشروق",
    "dhuhr": "الظّهر",
    "asr": "العصر",
    "maghrib": "المغرب",
    "isha": "العشاء",
}


@dataclass(frozen=True)
class Upcoming:
    """The prayer being counted down to."""

    key: str
    at: datetime
    #: True when ``at`` falls on the day after the one asked about - i.e. after Isha, when
    #: the next prayer is tomorrow's Fajr.
    tomorrow: bool

    @property
    def name(self) -> str:
        return ARABIC_NAMES[self.key]


class Timetable:
    """A year of prayer times, one row per ``MM-DD``."""

    def __init__(self, data: dict) -> None:
        self.city: str = data.get("city", "")
        self._days: dict[str, dict[str, str]] = data["days"]
        if not self._days:
            raise ValueError("timetable contains no days")

    @classmethod
    def load(cls, path: str | Path) -> "Timetable":
        with open(path, encoding="utf-8") as fh:
            return cls(json.load(fh))

    def for_date(
        self, day: Date, offsets: dict[str, int] | None = None
    ) -> dict[str, time]:
        """Times for one calendar day, with each prayer shifted by its offset in minutes.

        29 February exists in the table, so a leap year resolves normally; in other years
        the key is simply never requested.
        """
        row = self._days.get(day.strftime("%m-%d"))
        if row is None:
            raise KeyError(f"no timetable entry for {day.isoformat()}")

        offsets = offsets or {}
        result: dict[str, time] = {}
        for key in DISPLAY_KEYS:
            hour, minute = (int(part) for part in row[key].split(":"))
            shifted = datetime.combine(day, time(hour, minute)) + timedelta(
                minutes=offsets.get(key, 0)
            )
            result[key] = shifted.time()
        return result

    def next_prayer(
        self, now: datetime, offsets: dict[str, int] | None = None
    ) -> Upcoming:
        """The next prayer strictly after ``now``.

        A prayer landing on the current minute has passed, not arrived - by the time this
        is asked the announcement for it has already been made.
        """
        today = self.for_date(now.date(), offsets)
        for key in PRAYER_KEYS:
            at = datetime.combine(now.date(), today[key])
            if at > now:
                return Upcoming(key=key, at=at, tomorrow=False)

        # Past Isha: the countdown crosses midnight into tomorrow's Fajr rather than
        # wrapping to a negative or a 23-hour figure.
        tomorrow = now.date() + timedelta(days=1)
        at = datetime.combine(tomorrow, self.for_date(tomorrow, offsets)["fajr"])
        return Upcoming(key="fajr", at=at, tomorrow=True)

    def due_since(
        self,
        last_announced: datetime | None,
        now: datetime,
        offsets: dict[str, int] | None = None,
    ) -> list[tuple[str, datetime]]:
        """Prayers whose time arrived in ``(last_announced, now]``, oldest first.

        Called on a timer and again at startup. Passing the timestamp of the last
        announcement is what stops a restart at 12:30 from replaying the prayer that came
        in at 12:15, and stops a laptop waking from sleep from firing a burst of alarms for
        every prayer it slept through.
        """
        if last_announced is None:
            # Nothing has ever been announced, so treat everything before now as history
            # rather than announcing the whole day at once on first launch.
            return []
        if last_announced >= now:
            return []

        due: list[tuple[str, datetime]] = []
        # A gap longer than a day is a clock jump or a very long sleep; announcing days of
        # backlog helps nobody, so only the day containing `now` and the one before it are
        # examined.
        day = max(last_announced.date(), now.date() - timedelta(days=1))
        while day <= now.date():
            times = self.for_date(day, offsets)
            for key in PRAYER_KEYS:
                at = datetime.combine(day, times[key])
                if last_announced < at <= now:
                    due.append((key, at))
            day += timedelta(days=1)

        due.sort(key=lambda item: item[1])
        return due


def format_remaining(delta: timedelta) -> str:
    """The countdown text: minutes under an hour, ``h:mm`` above it."""
    total_seconds = max(0, int(delta.total_seconds()))
    minutes_total = total_seconds // 60

    if minutes_total < 60:
        # Round up so a countdown never reads "0 دقيقة" for a whole minute before arriving.
        minutes = minutes_total + (1 if total_seconds % 60 else 0)
        if minutes <= 1:
            return "أقل من دقيقة" if total_seconds else "الآن"
        return f"باقٍ {minutes} دقيقة"

    hours, minutes = divmod(minutes_total, 60)
    return f"باقٍ {hours}:{minutes:02d}"


def format_clock(delta: timedelta) -> str:
    """``HH:MM:SS`` for the one-line widget, where the seconds are the point.

    Always three fields, always zero-padded: a countdown whose width changes as it passes
    an hour makes the whole line jump sideways every tick.
    """
    total_seconds = max(0, int(delta.total_seconds()))
    hours, remainder = divmod(total_seconds, 3600)
    minutes, seconds = divmod(remainder, 60)
    return f"{hours:02d}:{minutes:02d}:{seconds:02d}"
