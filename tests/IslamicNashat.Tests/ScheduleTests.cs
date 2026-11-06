using System;
using System.Collections.Generic;
using System.Linq;
using IslamicNashat.Core;
using Xunit;

namespace IslamicNashat.Tests;

public class TimetableTests
{
    static readonly Dictionary<Prayer, int> NoOffsets = Enum.GetValues<Prayer>().ToDictionary(p => p, _ => 0);

    /// <summary>The same row every day: easy to reason about across midnight.</summary>
    static Timetable Fixed(Dictionary<Prayer, int>? offsets = null) =>
        new(Place.Default, null, 0, offsets ?? NoOffsets, (_, _) => new FixedRow("05:00", "06:30", "12:00", "15:30", "18:00", "19:30"), _ => 3);

    [Fact]
    public void Next_is_strictly_after_now()
    {
        var t = Fixed();
        Assert.Equal((Prayer.Dhuhr, new DateTime(2026, 11, 7, 12, 0, 0)), t.Next(new DateTime(2026, 11, 7, 9, 0, 0)));
        // At the very instant of Dhuhr, Dhuhr has arrived: the next one is Asr.
        Assert.Equal(Prayer.Asr, t.Next(new DateTime(2026, 11, 7, 12, 0, 0)).Prayer);
    }

    [Fact]
    public void After_isha_the_countdown_crosses_midnight_into_tomorrows_fajr()
    {
        var (prayer, at) = Fixed().Next(new DateTime(2026, 11, 7, 22, 0, 0));
        Assert.Equal(Prayer.Fajr, prayer);
        Assert.Equal(new DateTime(2026, 11, 8, 5, 0, 0), at);
    }

    [Fact]
    public void Before_fajr_the_previous_prayer_is_yesterdays_isha()
    {
        var (prayer, at) = Fixed().Previous(new DateTime(2026, 11, 7, 3, 0, 0));
        Assert.Equal(Prayer.Isha, prayer);
        Assert.Equal(new DateTime(2026, 11, 6, 19, 30, 0), at);
    }

    [Fact]
    public void Sunrise_is_shown_but_never_counted_down_to()
    {
        Assert.Equal(Prayer.Dhuhr, Fixed().Next(new DateTime(2026, 11, 7, 6, 0, 0)).Prayer);
    }

    [Fact]
    public void Offsets_shift_each_prayer()
    {
        var offsets = new Dictionary<Prayer, int>(NoOffsets) { [Prayer.Maghrib] = 3, [Prayer.Fajr] = -2 };
        var day = Fixed(offsets).For(new DateOnly(2026, 11, 7));
        Assert.Equal(new DateTime(2026, 11, 7, 18, 3, 0), day[Prayer.Maghrib]);
        Assert.Equal(new DateTime(2026, 11, 7, 4, 58, 0), day[Prayer.Fajr]);
    }

    [Fact]
    public void A_fixed_city_without_a_row_falls_back_to_the_calculation()
    {
        var t = new Timetable(Place.Default, null, 0, NoOffsets, (_, _) => null, _ => 3);
        var day = t.For(new DateOnly(2026, 1, 1));
        Assert.False(day.FromTimetable);
        // Golden value for Bardarash (MWL, +3): Fajr 05:45:17.
        Assert.Equal(new DateTime(2026, 1, 1, 5, 45, 17), day[Prayer.Fajr]);
    }
}

public class AlertTests
{
    static readonly Dictionary<Prayer, int> NoOffsets = Enum.GetValues<Prayer>().ToDictionary(p => p, _ => 0);
    static readonly Timetable Table = new(Place.Default, null, 0, NoOffsets, (_, _) => new FixedRow("05:00", "06:30", "12:00", "15:30", "18:00", "19:30"), _ => 3);
    static readonly AlertOptions Plain = new(ReminderMinutes: 10, RamadanMode: false, ImsakMinutes: 10, Friday: false, HijriOffset: 0);

    [Fact]
    public void Announces_the_adhan_and_its_reminder_once()
    {
        var due = Alerts.Between(Table, Plain, new DateTime(2026, 11, 7, 11, 0, 0), new DateTime(2026, 11, 7, 12, 0, 30));
        Assert.Equal([(AlertKind.Reminder, Prayer.Dhuhr), (AlertKind.Adhan, Prayer.Dhuhr)], due.Select(a => (a.Kind, a.Prayer)));
        Assert.Empty(Alerts.Between(Table, Plain, new DateTime(2026, 11, 7, 12, 0, 30), new DateTime(2026, 11, 7, 12, 5, 0)));
    }

    [Fact]
    public void A_restart_just_after_a_prayer_does_not_replay_it()
    {
        // Announced up to 12:00:05; the app restarts at 12:30.
        var due = Alerts.Between(Table, Plain, new DateTime(2026, 11, 7, 12, 0, 5), new DateTime(2026, 11, 7, 12, 30, 0));
        Assert.Empty(due);
    }

    [Fact]
    public void A_long_sleep_yields_at_most_two_days_of_alerts()
    {
        var due = Alerts.Between(Table, Plain, new DateTime(2026, 10, 1), new DateTime(2026, 11, 7, 12, 30, 0));
        Assert.All(due, a => Assert.True(a.At >= new DateTime(2026, 11, 6)));
    }

    [Fact]
    public void Friday_adds_the_kahf_reminder()
    {
        var friday = new DateOnly(2026, 11, 6);
        Assert.Equal(DayOfWeek.Friday, friday.DayOfWeek);
        var alerts = Alerts.ForDay(Table, Plain with { Friday = true }, friday).ToList();
        Assert.Contains(alerts, a => a.Kind == AlertKind.Friday && a.At == friday.ToDateTime(Alerts.FridayReminderTime));
        Assert.DoesNotContain(Alerts.ForDay(Table, Plain with { Friday = true }, friday.AddDays(1)), a => a.Kind == AlertKind.Friday);
    }

    [Fact]
    public void Imsak_only_in_ramadan()
    {
        var ramadan = new DateOnly(2026, 3, 1);
        var options = Plain with { RamadanMode = true };
        Assert.Contains(Alerts.ForDay(Table, options, ramadan), a => a.Kind == AlertKind.Imsak && a.At == ramadan.ToDateTime(new TimeOnly(4, 50)));
        Assert.DoesNotContain(Alerts.ForDay(Table, options, new DateOnly(2026, 11, 7)), a => a.Kind == AlertKind.Imsak);
    }
}

public class CalendarTests
{
    [Fact]
    public void Ramadan_1447_starts_on_18_february_2026()
    {
        Assert.Equal(new HijriDate(1, 9, 1447), Hijri.For(new DateOnly(2026, 2, 18), 0));
        Assert.Equal(8, Hijri.For(new DateOnly(2026, 2, 17), 0)?.Month);
    }

    [Fact]
    public void The_day_offset_follows_the_local_sighting()
    {
        Assert.Equal(new HijriDate(1, 9, 1447), Hijri.For(new DateOnly(2026, 2, 17), 1));
    }

    [Fact]
    public void Makruh_windows_surround_sunrise_zenith_and_sunset()
    {
        var day = new Timetable(Place.Default, null, 0, new Dictionary<Prayer, int>(),
            (_, _) => new FixedRow("05:00", "06:30", "12:00", "15:30", "18:00", "19:30"), _ => 3).For(new DateOnly(2026, 11, 7));
        Assert.Equal(MakruhKind.Sunrise, Makruh.Current(day, new DateTime(2026, 11, 7, 6, 40, 0))?.Kind);
        Assert.Equal(MakruhKind.Zenith, Makruh.Current(day, new DateTime(2026, 11, 7, 11, 55, 0))?.Kind);
        Assert.Equal(MakruhKind.Sunset, Makruh.Current(day, new DateTime(2026, 11, 7, 17, 50, 0))?.Kind);
        Assert.Null(Makruh.Current(day, new DateTime(2026, 11, 7, 9, 0, 0)));
    }
}

public class WidgetStateTests
{
    static AppSettings Settings() => new() { ReminderMinutes = 10, ElapsedMinutes = 20, RamadanMode = false };
    static readonly Timetable Table = new(Place.Default, null, 0, new Dictionary<Prayer, int>(),
        (_, _) => new FixedRow("05:00", "06:30", "12:00", "15:30", "18:00", "19:30"), _ => 3);

    public WidgetStateTests() => Strings.Language = "en";

    [Fact]
    public void Counts_down_with_a_fixed_width()
    {
        var s = WidgetState.Compute(Table, Settings(), new DateTime(2026, 11, 7, 9, 0, 0));
        Assert.Equal(WidgetPhase.Normal, s.Phase);
        Assert.Equal("Dhuhr", s.Name);
        Assert.Equal("03:00:00", s.Countdown);
    }

    [Fact]
    public void Turns_to_soon_inside_the_reminder_window()
    {
        var s = WidgetState.Compute(Table, Settings(), new DateTime(2026, 11, 7, 11, 55, 0));
        Assert.Equal(WidgetPhase.Soon, s.Phase);
        Assert.Equal("00:05:00", s.Countdown);
    }

    [Fact]
    public void Shows_time_since_the_adhan_then_moves_on()
    {
        var elapsed = WidgetState.Compute(Table, Settings(), new DateTime(2026, 11, 7, 12, 7, 30));
        Assert.Equal(WidgetPhase.Elapsed, elapsed.Phase);
        Assert.Equal("Dhuhr", elapsed.Name);
        Assert.Equal("00:07:30", elapsed.Countdown);

        var later = WidgetState.Compute(Table, Settings(), new DateTime(2026, 11, 7, 12, 25, 0));
        Assert.Equal(WidgetPhase.Normal, later.Phase);
        Assert.Equal("Asr", later.Name);
    }

    [Fact]
    public void Friday_dhuhr_is_called_jumuah()
    {
        var s = WidgetState.Compute(Table, Settings(), new DateTime(2026, 11, 6, 10, 0, 0));
        Assert.Equal("Jumu'ah", s.Name);
    }

    [Fact]
    public void Ramadan_counts_down_to_imsak_then_calls_maghrib_iftar()
    {
        var s = Settings();
        s.RamadanMode = true;
        s.ImsakMinutes = 10;
        Assert.Equal("Imsak", WidgetState.Compute(Table, s, new DateTime(2026, 3, 1, 3, 0, 0)).Name);
        Assert.Equal("Iftar", WidgetState.Compute(Table, s, new DateTime(2026, 3, 1, 16, 0, 0)).Name);
    }
}

public class PlacesTests
{
    [Fact]
    public void Bardarash_reads_the_phones_fixed_timetable()
    {
        var row = Places.FixedTimes(Place.Default, new DateOnly(2026, 1, 1));
        Assert.Equal(new FixedRow("05:47", "07:19", "12:15", "14:49", "17:06", "18:22"), row);
    }

    [Fact]
    public void Search_puts_prefix_matches_first()
    {
        var results = Places.Search("Bagh");
        Assert.NotEmpty(results);
        Assert.StartsWith("Bagh", results[0].Name);
    }

    [Fact]
    public void Nearest_prefers_a_fixed_timetable_city_close_by()
    {
        var place = Places.Nearest(36.50, 43.58);
        Assert.NotNull(place);
        Assert.True(place!.HasFixedTimetable);
    }
}
