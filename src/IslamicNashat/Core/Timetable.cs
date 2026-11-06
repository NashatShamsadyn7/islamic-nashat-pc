using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IslamicNashat.Core;

/// <summary>One day's six times, as local date-times, after the user's offsets.</summary>
public sealed record DayTimes(DateOnly Date, IReadOnlyDictionary<Prayer, DateTime> Times, bool FromTimetable)
{
    public DateTime this[Prayer p] => Times[p];
}

/// <summary>
/// Resolves a day's times the way the Android app does (PrayerManager.getPrayerTimesForDate):
/// the city's fixed timetable when it has one, the astronomical calculation otherwise,
/// then the per-prayer offsets on top. Pure apart from the injected lookups, so it is tested directly.
/// </summary>
public sealed class Timetable
{
    /// <summary>The five prayers that are counted down to and announced; sunrise is shown but never announced.</summary>
    public static readonly Prayer[] Announced = [Prayer.Fajr, Prayer.Dhuhr, Prayer.Asr, Prayer.Maghrib, Prayer.Isha];

    readonly Func<Place, DateOnly, FixedRow?> _fixedLookup;
    readonly Func<DateOnly, double> _utcOffsetHours;
    readonly Dictionary<DateOnly, DayTimes> _cache = [];

    public Place Place { get; }
    public CalculationMethod? Method { get; }
    public int AsrJuristic { get; }
    public IReadOnlyDictionary<Prayer, int> Offsets { get; }

    public Timetable(Place place, CalculationMethod? method, int asrJuristic, IReadOnlyDictionary<Prayer, int> offsets,
        Func<Place, DateOnly, FixedRow?>? fixedLookup = null, Func<DateOnly, double>? utcOffsetHours = null)
    {
        Place = place;
        Method = method;
        AsrJuristic = asrJuristic;
        Offsets = offsets;
        _fixedLookup = fixedLookup ?? Places.FixedTimes;
        _utcOffsetHours = utcOffsetHours ?? LocalUtcOffsetHours;
    }

    public static Timetable From(AppSettings s) => new(s.Place, s.Method, s.AsrJuristic, s.Offsets);

    public DayTimes For(DateOnly date)
    {
        if (_cache.TryGetValue(date, out var cached)) return cached;
        if (_cache.Count > 8) _cache.Clear();

        var raw = Place.HasFixedTimetable ? FromFixed(date) : null;
        var fromTimetable = raw is not null;
        // A city flagged fixed but missing a row falls through to the calculation rather than going blank.
        raw ??= Calculate(date);

        var times = raw.ToDictionary(kv => kv.Key, kv => date.ToDateTime(kv.Value).AddMinutes(Offsets.GetValueOrDefault(kv.Key)));
        return _cache[date] = new DayTimes(date, times, fromTimetable);
    }

    /// <summary>The next of the five prayers strictly after <paramref name="now"/>; after Isha that is tomorrow's Fajr.</summary>
    public (Prayer Prayer, DateTime At) Next(DateTime now)
    {
        var today = For(DateOnly.FromDateTime(now));
        foreach (var p in Announced)
            if (today[p] > now) return (p, today[p]);
        var tomorrow = For(DateOnly.FromDateTime(now).AddDays(1));
        return (Prayer.Fajr, tomorrow[Prayer.Fajr]);
    }

    /// <summary>The most recent of the five prayers at or before <paramref name="now"/>; before Fajr that is yesterday's Isha.</summary>
    public (Prayer Prayer, DateTime At) Previous(DateTime now)
    {
        var today = For(DateOnly.FromDateTime(now));
        foreach (var p in Announced.Reverse())
            if (today[p] <= now) return (p, today[p]);
        var yesterday = For(DateOnly.FromDateTime(now).AddDays(-1));
        return (Prayer.Isha, yesterday[Prayer.Isha]);
    }

    Dictionary<Prayer, TimeOnly>? FromFixed(DateOnly date)
    {
        var row = _fixedLookup(Place, date);
        if (row is null) return null;
        return new()
        {
            [Prayer.Fajr] = Parse(row.Fajr),
            [Prayer.Sunrise] = Parse(row.Sunrise),
            [Prayer.Dhuhr] = Parse(row.Dhuhr),
            [Prayer.Asr] = Parse(row.Asr),
            [Prayer.Maghrib] = Parse(row.Maghrib),
            [Prayer.Isha] = Parse(row.Isha),
        };
    }

    Dictionary<Prayer, TimeOnly> Calculate(DateOnly date)
    {
        var calc = new PrayerCalculator
        {
            Method = Method ?? PrayerCalculator.MethodForCountry(Place.CountryCode),
            AsrJuristic = AsrJuristic,
            AdjustHighLats = 3,
        };
        var t = calc.GetPrayerTimes(date, Place.Latitude, Place.Longitude, _utcOffsetHours(date));
        return new()
        {
            [Prayer.Fajr] = Parse(t["Fajr"]),
            [Prayer.Sunrise] = Parse(t["Sunrise"]),
            [Prayer.Dhuhr] = Parse(t["Dhuhr"]),
            [Prayer.Asr] = Parse(t["Asr"]),
            [Prayer.Maghrib] = Parse(t["Maghrib"]),
            [Prayer.Isha] = Parse(t["Isha"]),
        };
    }

    /// <summary>
    /// "HH:mm" from the database, "HH:mm:ss" from the calculator. The seconds are kept, as the
    /// phone keeps them, so the adhan fires at the same instant on both.
    /// </summary>
    static TimeOnly Parse(string value)
    {
        var parts = value.Split(':');
        int Part(int i) => i < parts.Length ? int.Parse(parts[i], CultureInfo.InvariantCulture) : 0;
        return new TimeOnly(Part(0), Part(1), Part(2));
    }

    /// <summary>The machine's UTC offset on that day, so a date past a daylight-saving change is still right.</summary>
    static double LocalUtcOffsetHours(DateOnly date) =>
        TimeZoneInfo.Local.GetUtcOffset(date.ToDateTime(new TimeOnly(12, 0))).TotalHours;
}
