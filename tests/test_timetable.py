from datetime import date, datetime, time, timedelta

import pytest

from prayer_widget.timetable import Timetable, format_remaining

FIXTURE = {
    "city": "Test",
    "days": {
        "01-01": {
            "fajr": "05:47",
            "sunrise": "07:19",
            "dhuhr": "12:15",
            "asr": "14:49",
            "maghrib": "17:06",
            "isha": "18:22",
        },
        "01-02": {
            "fajr": "05:48",
            "sunrise": "07:20",
            "dhuhr": "12:16",
            "asr": "14:50",
            "maghrib": "17:07",
            "isha": "18:23",
        },
        "02-29": {
            "fajr": "05:20",
            "sunrise": "06:45",
            "dhuhr": "12:20",
            "asr": "15:40",
            "maghrib": "17:55",
            "isha": "19:10",
        },
    },
}


@pytest.fixture
def table() -> Timetable:
    return Timetable(FIXTURE)


class TestNextPrayer:
    def test_picks_the_next_prayer_later_today(self, table: Timetable) -> None:
        upcoming = table.next_prayer(datetime(2026, 1, 1, 12, 20))

        assert upcoming.key == "asr"
        assert upcoming.at == datetime(2026, 1, 1, 14, 49)
        assert not upcoming.tomorrow

    def test_sunrise_is_never_counted_down_to(self, table: Timetable) -> None:
        # 07:19 is sunrise; the next thing to announce is Dhuhr, not sunrise.
        upcoming = table.next_prayer(datetime(2026, 1, 1, 6, 0))

        assert upcoming.key == "dhuhr"

    def test_a_prayer_on_the_current_minute_has_passed(self, table: Timetable) -> None:
        # Dhuhr is 12:15 exactly. By the time this is asked its announcement has been made,
        # so counting down to it again would show a frozen zero for a whole minute.
        upcoming = table.next_prayer(datetime(2026, 1, 1, 12, 15))

        assert upcoming.key == "asr"

    def test_one_second_before_a_prayer_still_counts_down_to_it(
        self, table: Timetable
    ) -> None:
        upcoming = table.next_prayer(datetime(2026, 1, 1, 12, 14, 59))

        assert upcoming.key == "dhuhr"

    def test_after_isha_it_crosses_midnight_to_tomorrows_fajr(
        self, table: Timetable
    ) -> None:
        # The classic failure: a negative countdown, or 23 hours back to today's Fajr.
        upcoming = table.next_prayer(datetime(2026, 1, 1, 23, 30))

        assert upcoming.key == "fajr"
        assert upcoming.tomorrow
        assert upcoming.at == datetime(2026, 1, 2, 5, 48)
        assert timedelta(0) < upcoming.at - datetime(2026, 1, 1, 23, 30) < timedelta(
            hours=7
        )

    def test_just_after_midnight_uses_the_new_days_row(self, table: Timetable) -> None:
        upcoming = table.next_prayer(datetime(2026, 1, 2, 0, 1))

        assert upcoming.at == datetime(2026, 1, 2, 5, 48)
        assert not upcoming.tomorrow

    def test_the_countdown_is_never_negative_across_a_whole_day(
        self, table: Timetable
    ) -> None:
        moment = datetime(2026, 1, 1, 0, 0)
        for _ in range(24 * 60):
            assert table.next_prayer(moment).at > moment
            moment += timedelta(minutes=1)


class TestLeapDay:
    def test_29_february_resolves(self, table: Timetable) -> None:
        times = table.for_date(date(2028, 2, 29))

        assert times["fajr"] == time(5, 20)

    def test_a_missing_day_is_an_explicit_error(self, table: Timetable) -> None:
        # Silently substituting another day would show wrong times that look plausible.
        with pytest.raises(KeyError):
            table.for_date(date(2026, 6, 1))


class TestOffsets:
    def test_an_offset_shifts_only_its_own_prayer(self, table: Timetable) -> None:
        times = table.for_date(date(2026, 1, 1), offsets={"asr": 5})

        assert times["asr"] == time(14, 54)
        assert times["dhuhr"] == time(12, 15)

    def test_a_negative_offset_moves_earlier(self, table: Timetable) -> None:
        times = table.for_date(date(2026, 1, 1), offsets={"fajr": -7})

        assert times["fajr"] == time(5, 40)

    def test_offsets_are_honoured_when_choosing_the_next_prayer(
        self, table: Timetable
    ) -> None:
        # Without the offset, 12:16 is past Dhuhr. Pushed to 12:20 it is still upcoming.
        upcoming = table.next_prayer(datetime(2026, 1, 1, 12, 16), offsets={"dhuhr": 5})

        assert upcoming.key == "dhuhr"
        assert upcoming.at == datetime(2026, 1, 1, 12, 20)


class TestDueSince:
    def test_a_prayer_that_arrived_since_the_last_check_is_due(
        self, table: Timetable
    ) -> None:
        due = table.due_since(
            last_announced=datetime(2026, 1, 1, 12, 0),
            now=datetime(2026, 1, 1, 12, 16),
        )

        assert [key for key, _ in due] == ["dhuhr"]

    def test_a_restart_after_a_prayer_does_not_replay_it(self, table: Timetable) -> None:
        # Announced at 12:15, app restarted at 12:30. Nothing new has arrived.
        due = table.due_since(
            last_announced=datetime(2026, 1, 1, 12, 15),
            now=datetime(2026, 1, 1, 12, 30),
        )

        assert due == []

    def test_a_first_ever_launch_announces_nothing(self, table: Timetable) -> None:
        # Otherwise installing the app at 20:00 would fire five athans in a row.
        due = table.due_since(last_announced=None, now=datetime(2026, 1, 1, 20, 0))

        assert due == []

    def test_waking_from_sleep_reports_the_missed_prayers_in_order(
        self, table: Timetable
    ) -> None:
        due = table.due_since(
            last_announced=datetime(2026, 1, 1, 11, 0),
            now=datetime(2026, 1, 1, 19, 0),
        )

        assert [key for key, _ in due] == ["dhuhr", "asr", "maghrib", "isha"]
        assert due == sorted(due, key=lambda item: item[1])

    def test_a_gap_across_midnight_covers_both_days(self, table: Timetable) -> None:
        due = table.due_since(
            last_announced=datetime(2026, 1, 1, 18, 0),
            now=datetime(2026, 1, 2, 6, 0),
        )

        assert [key for key, _ in due] == ["isha", "fajr"]

    def test_a_very_long_gap_is_capped_rather_than_replaying_days(
        self, table: Timetable
    ) -> None:
        # A machine off for a week must not fire thirty-five athans on boot.
        due = table.due_since(
            last_announced=datetime(2026, 1, 1, 0, 0),
            now=datetime(2026, 1, 2, 20, 0),
        )

        assert len(due) <= len(("fajr", "dhuhr", "asr", "maghrib", "isha")) * 2

    def test_a_clock_moved_backwards_produces_nothing(self, table: Timetable) -> None:
        due = table.due_since(
            last_announced=datetime(2026, 1, 1, 18, 0),
            now=datetime(2026, 1, 1, 9, 0),
        )

        assert due == []


class TestFormatRemaining:
    @pytest.mark.parametrize(
        "delta, expected",
        [
            (timedelta(minutes=17), "باقٍ 17 دقيقة"),
            (timedelta(minutes=59), "باقٍ 59 دقيقة"),
            (timedelta(minutes=60), "باقٍ 1:00"),
            (timedelta(hours=2, minutes=35), "باقٍ 2:35"),
            (timedelta(seconds=30), "أقل من دقيقة"),
            (timedelta(0), "الآن"),
        ],
    )
    def test_formats(self, delta: timedelta, expected: str) -> None:
        assert format_remaining(delta) == expected

    def test_a_negative_delta_reads_as_now_rather_than_a_minus_sign(self) -> None:
        assert format_remaining(timedelta(minutes=-5)) == "الآن"

    def test_seconds_round_up_so_the_display_never_sticks_at_zero(self) -> None:
        # 90 seconds is a minute and a half; showing "1 دقيقة" then counting down would
        # freeze on the same text for 30 seconds.
        assert format_remaining(timedelta(seconds=90)) == "باقٍ 2 دقيقة"
