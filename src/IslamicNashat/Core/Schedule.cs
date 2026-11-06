using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IslamicNashat.Core;

public readonly record struct HijriDate(int Day, int Month, int Year)
{
    public const int Ramadan = 9;
    public bool IsRamadan => Month == Ramadan;
}

/// <summary>
/// Umm al-Qura, the same tabular calendar the Android app gets from java.time.HijrahDate, shifted
/// by the user's day offset so it can follow the local moon sighting.
/// </summary>
public static class Hijri
{
    static readonly UmAlQuraCalendar Calendar = new();

    public static HijriDate? For(DateOnly date, int dayOffset)
    {
        try
        {
            var dt = date.ToDateTime(TimeOnly.MinValue).AddDays(dayOffset);
            return new HijriDate(Calendar.GetDayOfMonth(dt), Calendar.GetMonth(dt), Calendar.GetYear(dt));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null; // Outside 1900-2077; the Hijri line is simply hidden.
        }
    }
}

public enum MakruhKind { Sunrise, Zenith, Sunset }

/// <summary>Times in which voluntary prayer is disliked: as the sun rises, at its zenith and as it sets.</summary>
public static class Makruh
{
    public static readonly TimeSpan AfterSunrise = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan BeforeDhuhr = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan BeforeMaghrib = TimeSpan.FromMinutes(15);

    public static IReadOnlyList<(MakruhKind Kind, DateTime Start, DateTime End)> For(DayTimes day) =>
    [
        (MakruhKind.Sunrise, day[Prayer.Sunrise], day[Prayer.Sunrise] + AfterSunrise),
        (MakruhKind.Zenith, day[Prayer.Dhuhr] - BeforeDhuhr, day[Prayer.Dhuhr]),
        (MakruhKind.Sunset, day[Prayer.Maghrib] - BeforeMaghrib, day[Prayer.Maghrib]),
    ];

    public static (MakruhKind Kind, DateTime End)? Current(DayTimes day, DateTime now)
    {
        foreach (var (kind, start, end) in For(day))
            if (now >= start && now < end) return (kind, end);
        return null;
    }
}

public enum AlertKind { Adhan, Reminder, Imsak, Friday }

/// <summary>Something to announce at <see cref="At"/>. <see cref="Target"/> is the prayer time it is about.</summary>
public sealed record Alert(AlertKind Kind, Prayer Prayer, DateTime At, DateTime Target);

public sealed record AlertOptions(int ReminderMinutes, bool RamadanMode, int ImsakMinutes, bool Friday, int HijriOffset)
{
    public static AlertOptions From(AppSettings s) => new(s.ReminderMinutes, s.RamadanMode, s.ImsakMinutes, s.FridayReminder, s.HijriOffset);
}

public static class Alerts
{
    /// <summary>Friday's Al-Kahf reminder, at the hour the Android app uses by default.</summary>
    public static readonly TimeOnly FridayReminderTime = new(8, 0);

    /// <summary>
    /// Alerts due in (<paramref name="after"/>, <paramref name="upTo"/>], oldest first. Only the
    /// day containing <paramref name="upTo"/> and the one before are looked at: a clock jump or a
    /// long sleep must not produce days of backlog.
    /// </summary>
    public static List<Alert> Between(Timetable timetable, AlertOptions o, DateTime after, DateTime upTo)
    {
        var due = new List<Alert>();
        if (after >= upTo) return due;

        var last = DateOnly.FromDateTime(upTo);
        var day = DateOnly.FromDateTime(after);
        if (day < last.AddDays(-1)) day = last.AddDays(-1);
        // Tomorrow's Fajr reminder can fall before midnight only with absurd offsets; one day ahead covers it.
        for (; day <= last.AddDays(1); day = day.AddDays(1))
            foreach (var alert in ForDay(timetable, o, day))
                if (alert.At > after && alert.At <= upTo) due.Add(alert);

        return due.OrderBy(a => a.At).ThenBy(a => a.Kind).ToList();
    }

    /// <summary>Every alert of one day, used both for announcing and for showing what's coming.</summary>
    public static IEnumerable<Alert> ForDay(Timetable timetable, AlertOptions o, DateOnly date)
    {
        var day = timetable.For(date);
        var ramadan = o.RamadanMode && Hijri.For(date, o.HijriOffset) is { IsRamadan: true };

        if (ramadan && o.ImsakMinutes > 0)
            yield return new Alert(AlertKind.Imsak, Prayer.Fajr, day[Prayer.Fajr].AddMinutes(-o.ImsakMinutes), day[Prayer.Fajr]);

        if (o.Friday && date.DayOfWeek == DayOfWeek.Friday)
            yield return new Alert(AlertKind.Friday, Prayer.Dhuhr, date.ToDateTime(FridayReminderTime), day[Prayer.Dhuhr]);

        foreach (var p in Timetable.Announced)
        {
            if (o.ReminderMinutes > 0)
                yield return new Alert(AlertKind.Reminder, p, day[p].AddMinutes(-o.ReminderMinutes), day[p]);
            yield return new Alert(AlertKind.Adhan, p, day[p], day[p]);
        }
    }

    public static bool IsRamadan(DateOnly date, AlertOptions o) =>
        o.RamadanMode && Hijri.For(date, o.HijriOffset) is { IsRamadan: true };
}
