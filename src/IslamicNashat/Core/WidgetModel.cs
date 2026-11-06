using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IslamicNashat.Core;

public enum WidgetPhase
{
    /// <summary>Counting down to the next prayer.</summary>
    Normal,
    /// <summary>Within the reminder minutes before the next prayer.</summary>
    Soon,
    /// <summary>A prayer came in a little while ago; showing the time since its adhan.</summary>
    Elapsed,
}

public sealed record TimeRow(string Name, string Time, bool IsNext, bool IsPast);

/// <summary>Everything the widget shows at one instant. Built from the clock each second, so sleep, time changes and midnight need no special handling.</summary>
public sealed record WidgetState(
    string Name,
    string Prefix,
    string Countdown,
    string At,
    WidgetPhase Phase,
    string? MakruhNow,
    string Place,
    string DateLine,
    string? HijriLine,
    IReadOnlyList<TimeRow> Rows,
    IReadOnlyList<TimeRow> MakruhRows,
    string SourceNote)
{
    public static WidgetState Compute(Timetable timetable, AppSettings s, DateTime now)
    {
        var date = DateOnly.FromDateTime(now);
        var today = timetable.For(date);
        var options = AlertOptions.From(s);
        var ramadan = Alerts.IsRamadan(date, options);

        var (next, nextAt) = timetable.Next(now);
        var (prev, prevAt) = timetable.Previous(now);

        // In Ramadan the night ends at Imsak, not Fajr: count down to it while it's still ahead.
        string? targetName = null;
        if (Alerts.IsRamadan(DateOnly.FromDateTime(nextAt), options) && next == Prayer.Fajr && s.ImsakMinutes > 0)
        {
            var imsak = nextAt.AddMinutes(-s.ImsakMinutes);
            if (imsak > now)
            {
                nextAt = imsak;
                targetName = Strings.T("imsak");
            }
        }

        string name, prefix, countdown, at;
        WidgetPhase phase;
        if (s.ElapsedMinutes > 0 && now - prevAt < TimeSpan.FromMinutes(s.ElapsedMinutes) && now >= prevAt)
        {
            phase = WidgetPhase.Elapsed;
            name = Strings.PrayerName(prev, prevAt, Alerts.IsRamadan(DateOnly.FromDateTime(prevAt), options));
            prefix = Strings.T("since_adhan");
            countdown = Strings.Countdown(now - prevAt);
            at = Strings.Clock(prevAt);
        }
        else
        {
            var remaining = nextAt - now;
            phase = s.ReminderMinutes > 0 && remaining <= TimeSpan.FromMinutes(s.ReminderMinutes) ? WidgetPhase.Soon : WidgetPhase.Normal;
            name = targetName ?? Strings.PrayerName(next, nextAt, Alerts.IsRamadan(DateOnly.FromDateTime(nextAt), options));
            prefix = "";
            countdown = Strings.Countdown(remaining);
            at = Strings.Clock(nextAt);
        }

        string? makruhNow = null;
        var makruhRows = new List<TimeRow>();
        if (s.ShowMakruh)
        {
            if (Makruh.Current(today, now) is { } current)
                makruhNow = Strings.F("makruh_now", Strings.Clock(current.End));
            foreach (var (kind, start, end) in Makruh.For(today))
                makruhRows.Add(new TimeRow(Strings.T("makruh_" + kind.ToString().ToLowerInvariant()),
                    $"{Strings.Clock(start)} – {Strings.Clock(end)}", now >= start && now < end, now >= end));
        }

        var rows = new List<TimeRow>();
        var targetAt = phase == WidgetPhase.Elapsed ? (DateTime?)null : nextAt;
        if (ramadan && s.ImsakMinutes > 0)
        {
            var imsak = today[Prayer.Fajr].AddMinutes(-s.ImsakMinutes);
            rows.Add(new TimeRow(Strings.T("imsak"), Strings.Clock(imsak), targetAt == imsak, now >= imsak));
        }
        foreach (var p in Enum.GetValues<Prayer>())
        {
            var t = today[p];
            var isNext = targetAt == t && targetName is null;
            rows.Add(new TimeRow(Strings.PrayerName(p, t, ramadan), Strings.Clock(t), isNext, now >= t));
        }

        var hijri = Hijri.For(date, s.HijriOffset);
        string? hijriLine = hijri is { } h
            ? h.IsRamadan && s.RamadanMode ? $"{Strings.HijriText(h)} · {Strings.F("ramadan_day", Strings.Digits(h.Day))}" : Strings.HijriText(h)
            : null;

        return new WidgetState(name, prefix, countdown, at, phase, makruhNow,
            s.Place.Name, $"{Strings.DayName(now)} {GregorianDate(now)}", hijriLine, rows, makruhRows,
            Strings.T(today.FromTimetable ? "source_fixed" : "source_calc"));
    }

    static string GregorianDate(DateTime d) =>
        Strings.Language == "en" ? d.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("en-GB")) : Strings.Digits(d.ToString("yyyy/M/d", CultureInfo.InvariantCulture));
}
