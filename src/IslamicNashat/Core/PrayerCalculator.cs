using System;
using System.Collections.Generic;
using System.Globalization;

namespace IslamicNashat.Core;

public enum CalculationMethod { Makkah, Mwl, Isna, Karachi, Egypt, Tehran, Jafari, Custom }

/// <summary>
/// Line-by-line port of the Android app's MuslimDataCalculator.kt (itself a port of Prayer.swift
/// from MuslimData). It is pinned to output produced by the Kotlin class (calculator_golden.json),
/// so the widget and the phone agree to the second. Change it only together with the Android app.
/// </summary>
public sealed class PrayerCalculator
{
    // Fajr angle, Maghrib is minutes (1) or angle (0), Maghrib value, Isha is minutes (1) or angle (0), Isha value.
    static readonly Dictionary<CalculationMethod, double[]> MethodParams = new()
    {
        [CalculationMethod.Makkah] = [18.5, 1.0, 0.0, 1.0, 90.0],
        [CalculationMethod.Mwl] = [18.0, 1.0, 0.0, 0.0, 17.0],
        [CalculationMethod.Isna] = [15.0, 1.0, 0.0, 0.0, 15.0],
        [CalculationMethod.Karachi] = [18.0, 1.0, 0.0, 0.0, 18.0],
        [CalculationMethod.Egypt] = [18.0, 1.0, 0.0, 0.0, 17.0],
        [CalculationMethod.Jafari] = [16.0, 0.0, 4.0, 0.0, 14.0],
        [CalculationMethod.Tehran] = [17.7, 0.0, 4.5, 0.0, 14.0],
        [CalculationMethod.Custom] = [19.5, 1.0, 0.0, 0.0, 17.5],
    };

    // Java's Math.toRadians / toDegrees multiply by these exact constants; matching them keeps
    // results identical to the Kotlin class down to the last bit.
    const double DegreesToRadians = 0.017453292519943295;
    const double RadiansToDegrees = 57.29577951308232;

    public CalculationMethod Method { get; init; } = CalculationMethod.Makkah;
    /// <summary>0: Shafi'i, 1: Hanafi.</summary>
    public int AsrJuristic { get; init; }
    public int DhuhrMinutes { get; init; }
    /// <summary>0: none, 1: midnight, 2: one seventh, 3: angle based (what the app uses).</summary>
    public int AdjustHighLats { get; init; } = 3;

    double _lat, _lng, _timeZone, _jDate;

    /// <summary>Times as "HH:mm:ss" keyed Fajr, Sunrise, Dhuhr, Asr, Sunset, Maghrib, Isha.</summary>
    public IReadOnlyDictionary<string, string> GetPrayerTimes(DateOnly date, double latitude, double longitude, double timeZone)
    {
        _lat = latitude;
        _lng = longitude;
        _timeZone = timeZone;
        _jDate = JulianDate(date.Year, date.Month, date.Day) - longitude / (15.0 * 24.0);
        return ComputeDayTimes();
    }

    /// <summary>The method the Android app picks on "Auto" (PrayerManager.autoMethodForCountry).</summary>
    public static CalculationMethod MethodForCountry(string? countryCode) => (countryCode ?? "").ToUpperInvariant() switch
    {
        "SA" or "AE" or "QA" or "KW" or "BH" => CalculationMethod.Makkah,
        "EG" => CalculationMethod.Egypt,
        "PK" or "IN" or "BD" or "AF" => CalculationMethod.Karachi,
        "IR" => CalculationMethod.Tehran,
        "US" or "CA" => CalculationMethod.Isna,
        _ => CalculationMethod.Mwl,
    };

    static double JulianDate(int year, int month, int day)
    {
        if (month <= 2)
        {
            year -= 1;
            month += 12;
        }
        var a = Math.Floor(year / 100.0);
        var b = 2 - a + Math.Floor(a / 4.0);
        var jd1 = Math.Floor(365.25 * (year + 4716));
        var jd2 = Math.Floor(30.6001 * (month + 1));
        return jd1 + jd2 + day + b - 1524.5;
    }

    static (double Declination, double EquationOfTime) SunPosition(double jd)
    {
        var d = jd - 2451545.0;
        var g = FixAngle(357.529 + 0.98560028 * d);
        var q = FixAngle(280.459 + 0.98564736 * d);
        var l = FixAngle(q + 1.915 * DSin(g) + 0.020 * DSin(2 * g));

        var e = 23.439 - (0.00000036 * d);
        var declination = DArcSin(DSin(e) * DSin(l));
        var ra = FixHour(DArcTan2(DCos(e) * DSin(l), DCos(l)) / 15.0);
        return (declination, q / 15.0 - ra);
    }

    double ComputeMidDay(double t) => FixHour(12 - SunPosition(_jDate + t).EquationOfTime);

    double ComputeTime(double g, double t)
    {
        var d = SunPosition(_jDate + t).Declination;
        var z = ComputeMidDay(t);
        var v = 1.0 / 15.0 * DArcCos((-DSin(g) - DSin(d) * DSin(_lat)) / (DCos(d) * DCos(_lat)));
        return z + (g > 90 ? -v : v);
    }

    double ComputeAsr(double step, double t)
    {
        var d = SunPosition(_jDate + t).Declination;
        var g = -DArcCot(step + DTan(Math.Abs(_lat - d)));
        return ComputeTime(g, t);
    }

    double[] ComputeTimes(double[] times)
    {
        var t = new double[times.Length];
        for (var i = 0; i < times.Length; i++) t[i] = times[i] / 24.0;
        var parm = MethodParams[Method];

        return
        [
            ComputeTime(180 - parm[0], t[0]),
            ComputeTime(180 - 0.833, t[1]),
            ComputeMidDay(t[2]),
            ComputeAsr(1.0 + AsrJuristic, t[3]),
            ComputeTime(0.833, t[4]),
            ComputeTime(parm[2], t[5]),
            ComputeTime(parm[4], t[6]),
        ];
    }

    IReadOnlyDictionary<string, string> ComputeDayTimes()
    {
        double[] times = [5.0, 6.0, 12.0, 13.0, 18.0, 18.0, 18.0];
        times = ComputeTimes(times); // One iteration, as in the Swift original.
        times = AdjustTimes(times);
        return new Dictionary<string, string>
        {
            ["Fajr"] = FloatToTime24(times[0]),
            ["Sunrise"] = FloatToTime24(times[1]),
            ["Dhuhr"] = FloatToTime24(times[2]),
            ["Asr"] = FloatToTime24(times[3]),
            ["Sunset"] = FloatToTime24(times[4]),
            ["Maghrib"] = FloatToTime24(times[5]),
            ["Isha"] = FloatToTime24(times[6]),
        };
    }

    double[] AdjustTimes(double[] times)
    {
        var a = new double[times.Length];
        for (var i = 0; i < times.Length; i++) a[i] = times[i] + _timeZone - _lng / 15.0;

        var parm = MethodParams[Method];
        a[2] += DhuhrMinutes / 60.0;
        if (parm[1] == 1.0) a[5] = a[4] + parm[2] / 60.0;
        if (parm[3] == 1.0) a[6] = a[5] + parm[4] / 60.0;
        return AdjustHighLats != 0 ? AdjustHighLatTimes(a) : a;
    }

    double[] AdjustHighLatTimes(double[] times)
    {
        var nightTime = TimeDiff(times[4], times[1]);
        var parm = MethodParams[Method];

        var fajrDiff = NightPortion(parm[0]) * nightTime;
        if (double.IsNaN(times[0]) || TimeDiff(times[0], times[1]) > fajrDiff)
            times[0] = times[1] - fajrDiff;

        var ishaAngle = parm[3] == 0.0 ? parm[4] : 18.0;
        var ishaDiff = NightPortion(ishaAngle) * nightTime;
        if (double.IsNaN(times[6]) || TimeDiff(times[4], times[6]) > ishaDiff)
            times[6] = times[4] + ishaDiff;

        var maghribAngle = parm[1] == 0.0 ? parm[2] : 4.0;
        var maghribDiff = NightPortion(maghribAngle) * nightTime;
        if (double.IsNaN(times[5]) || TimeDiff(times[4], times[5]) > maghribDiff)
            times[5] = times[4] + maghribDiff;

        return times;
    }

    double NightPortion(double angle) => AdjustHighLats switch
    {
        3 => angle / 60.0,
        1 => 0.5,
        2 => 0.14286,
        _ => 0.0,
    };

    static double TimeDiff(double time1, double time2) => FixHour(time2 - time1);

    static double FixAngle(double a)
    {
        var angle = a - 360 * Math.Floor(a / 360.0);
        return angle < 0 ? angle + 360 : angle;
    }

    static double FixHour(double a)
    {
        var hour = a - 24.0 * Math.Floor(a / 24.0);
        return hour < 0 ? hour + 24 : hour;
    }

    static double DSin(double d) => Math.Sin(d * DegreesToRadians);
    static double DCos(double d) => Math.Cos(d * DegreesToRadians);
    static double DTan(double d) => Math.Tan(d * DegreesToRadians);
    static double DArcSin(double x) => Math.Asin(Math.Clamp(x, -1.0, 1.0)) * RadiansToDegrees;
    static double DArcCos(double x) => Math.Acos(Math.Clamp(x, -1.0, 1.0)) * RadiansToDegrees;
    static double DArcTan2(double y, double x) => Math.Atan2(y, x) * RadiansToDegrees;
    static double DArcCot(double x) => Math.Atan2(1.0, x) * RadiansToDegrees;

    /// <summary>Seconds are truncated, not rounded, exactly as the Kotlin class does.</summary>
    static string FloatToTime24(double time)
    {
        if (double.IsNaN(time)) return "00:00:00";
        var totalSeconds = (int)Math.Floor(FixHour(time) * 3600.0);
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}",
            totalSeconds / 3600, totalSeconds % 3600 / 60, totalSeconds % 60);
    }
}
