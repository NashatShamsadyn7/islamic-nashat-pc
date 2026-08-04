"""Location lookup against the real bundled database."""

from __future__ import annotations

from datetime import date, datetime, timedelta

import pytest

from prayer_widget import locations
from prayer_widget.locations import BARDARASH, Place, PlaceTimetable, _distance_km
from prayer_widget.timetable import DISPLAY_KEYS, format_clock


class TestSearch:
    def test_finds_a_city_by_prefix(self) -> None:
        results = locations.search("Bardarash")

        assert results
        assert results[0].name == "Bardarash"
        assert results[0].country_code == "IQ"

    def test_a_prefix_match_outranks_a_mere_substring(self) -> None:
        results = locations.search("Baghd")

        assert results[0].name.lower().startswith("baghd")

    def test_search_is_case_insensitive(self) -> None:
        assert locations.search("bardarash")[0].id == locations.search("BARDARASH")[0].id

    def test_a_single_character_returns_nothing(self) -> None:
        # 166k rows means a one-letter query is both useless and slow.
        assert locations.search("b") == []
        assert locations.search("") == []

    def test_results_are_capped(self) -> None:
        assert len(locations.search("san", limit=10)) <= 10


class TestNearest:
    def test_the_exact_coordinate_of_a_city_finds_that_city(self) -> None:
        place = locations.nearest(BARDARASH.latitude, BARDARASH.longitude)

        assert place is not None
        assert place.id == BARDARASH.id

    def test_a_fixed_timetable_city_wins_over_a_marginally_closer_one(self) -> None:
        # A few kilometres off Bardarash must still resolve to Bardarash rather than to
        # some nameless hamlet without a timetable.
        place = locations.nearest(BARDARASH.latitude + 0.03, BARDARASH.longitude + 0.03)

        assert place is not None
        assert place.has_fixed_timetable

    def test_the_middle_of_an_ocean_has_nothing_nearby(self) -> None:
        assert locations.nearest(0.0, -140.0) is None


class TestDistance:
    def test_the_same_point_is_zero(self) -> None:
        assert _distance_km(36.5, 43.5, 36.5, 43.5) == pytest.approx(0.0, abs=1e-9)

    def test_a_degree_of_latitude_is_about_111_km(self) -> None:
        assert _distance_km(36.0, 43.0, 37.0, 43.0) == pytest.approx(111.2, abs=1.0)

    def test_it_wraps_the_date_line_instead_of_going_the_long_way(self) -> None:
        # A flat approximation would answer ~40,000 km here.
        assert _distance_km(0.0, 179.5, 0.0, -179.5) == pytest.approx(111.2, abs=2.0)


class TestPlaceTimetable:
    def test_bardarash_reads_the_fixed_table(self) -> None:
        table = PlaceTimetable(BARDARASH)
        times = table.for_date(date(2026, 1, 1))

        assert table.source == "fixed"
        # The exact row seen in the Android app's database.
        assert times["fajr"].strftime("%H:%M") == "05:47"
        assert times["dhuhr"].strftime("%H:%M") == "12:15"
        assert times["isha"].strftime("%H:%M") == "18:22"

    def test_every_display_key_is_present(self) -> None:
        times = PlaceTimetable(BARDARASH).for_date(date(2026, 8, 4))

        assert set(times) == set(DISPLAY_KEYS)

    def test_a_city_without_a_fixed_table_is_calculated(self) -> None:
        paris = Place(
            id=-1,
            name="Paris",
            country_code="FR",
            latitude=48.8566,
            longitude=2.3522,
            has_fixed_timetable=False,
            tz_offset_hours=2.0,
        )
        table = PlaceTimetable(paris)
        times = table.for_date(date(2026, 8, 4))

        assert table.source == "calculated"
        # Sanity only - exactness against the phone is proven in test_calculator.py.
        assert times["fajr"] < times["sunrise"] < times["dhuhr"] < times["asr"]
        assert times["asr"] < times["maghrib"] < times["isha"]

    def test_offsets_apply_to_the_fixed_table_too(self) -> None:
        table = PlaceTimetable(BARDARASH)
        shifted = table.for_date(date(2026, 1, 1), offsets={"dhuhr": 5})

        assert shifted["dhuhr"].strftime("%H:%M") == "12:20"

    def test_the_inherited_countdown_logic_still_works(self) -> None:
        # PlaceTimetable only overrides the day lookup; next_prayer and due_since are the
        # code the JSON-backed tests already cover.
        table = PlaceTimetable(BARDARASH)
        upcoming = table.next_prayer(datetime(2026, 1, 1, 23, 30))

        assert upcoming.key == "fajr"
        assert upcoming.tomorrow

    def test_a_leap_day_resolves(self) -> None:
        times = PlaceTimetable(BARDARASH).for_date(date(2028, 2, 29))

        assert times["fajr"] is not None


class TestPlaceRoundTrip:
    def test_a_place_survives_being_saved_and_reloaded(self) -> None:
        assert Place.from_dict(BARDARASH.to_dict()) == BARDARASH

    def test_the_timezone_override_is_preserved(self) -> None:
        place = Place(**{**BARDARASH.to_dict(), "tz_offset_hours": 3.5})

        assert Place.from_dict(place.to_dict()).tz_offset_hours == 3.5

    def test_a_dependent_city_reads_its_parents_rows(self) -> None:
        with locations._database() as db:
            row = db.execute(
                "SELECT _id, prayer_dependent_id FROM location "
                "WHERE prayer_dependent_id IS NOT NULL LIMIT 1"
            ).fetchone()
        if row is None:
            pytest.skip("no dependent locations in this database")

        place = Place(
            id=row["_id"],
            name="dependent",
            country_code="IQ",
            latitude=0.0,
            longitude=0.0,
            has_fixed_timetable=True,
            timetable_id=row["prayer_dependent_id"],
        )
        assert place.source_id == row["prayer_dependent_id"]


class TestFormatClock:
    @pytest.mark.parametrize(
        "delta, expected",
        [
            (timedelta(seconds=45), "00:00:45"),
            (timedelta(minutes=17, seconds=3), "00:17:03"),
            (timedelta(hours=2, minutes=35, seconds=9), "02:35:09"),
            (timedelta(0), "00:00:00"),
        ],
    )
    def test_formats(self, delta: timedelta, expected: str) -> None:
        assert format_clock(delta) == expected

    def test_the_width_never_changes(self) -> None:
        # A countdown that narrows as it drops below an hour makes the whole bar jump.
        widths = {
            len(format_clock(timedelta(seconds=s)))
            for s in (0, 59, 60, 3599, 3600, 36000)
        }
        assert widths == {8}

    def test_a_negative_delta_reads_as_zero(self) -> None:
        assert format_clock(timedelta(seconds=-30)) == "00:00:00"
