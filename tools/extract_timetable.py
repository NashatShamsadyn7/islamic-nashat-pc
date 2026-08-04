"""Pulls one city's fixed prayer timetable out of the app's bundled muslim-data DB.

The Android app prefers this table over any astronomical calculation whenever the location
has has_fixed_prayer_time=1, so reading the same 366 rows is the only way a second client
can be guaranteed to show identical times.
"""
import json
import sqlite3
import sys

DB = r"c:\Users\nasha\Downloads\Islamic_Nashat_Complete\app\src\main\assets\databases\muslim_db_v2.5.1.db"
CITY = sys.argv[1] if len(sys.argv) > 1 else "Bardarash"
OUT = sys.argv[2] if len(sys.argv) > 2 else "timetable.json"

conn = sqlite3.connect(DB)
loc = conn.execute(
    "SELECT _id, name, latitude, longitude, has_fixed_prayer_time, prayer_dependent_id "
    "FROM location WHERE name = ?", (CITY,)
).fetchone()

if loc is None:
    sys.exit(f"No location named {CITY!r}")

loc_id, name, lat, lon, fixed, dependent = loc
if not fixed:
    sys.exit(f"{name} has no fixed timetable; it is calculated astronomically instead")

source_id = dependent or loc_id
rows = conn.execute(
    "SELECT date, fajr, sunrise, dhuhr, asr, maghrib, isha "
    "FROM prayer_time WHERE location_id = ? ORDER BY date", (source_id,)
).fetchall()

data = {
    "city": name,
    "latitude": lat,
    "longitude": lon,
    "source": "muslim_db_v2.5.1 prayer_time table",
    "days": {
        d: {"fajr": f, "sunrise": s, "dhuhr": dh, "asr": a, "maghrib": m, "isha": i}
        for d, f, s, dh, a, m, i in rows
    },
}

with open(OUT, "w", encoding="utf-8") as fh:
    json.dump(data, fh, ensure_ascii=False, indent=1)

print(f"{name}: {len(rows)} days -> {OUT}")
