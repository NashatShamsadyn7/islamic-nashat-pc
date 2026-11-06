using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using IslamicNashat.Core;
using Xunit;

namespace IslamicNashat.Tests;

/// <summary>
/// Pins the C# calculator to what the Android app's Kotlin class returns.
/// calculator_golden.json comes from CalculatorGoldenDumpTest in the Android repository.
/// Any difference fails, down to the second.
/// </summary>
public class CalculatorGoldenTests
{
    static readonly string[] Keys = ["Fajr", "Sunrise", "Dhuhr", "Asr", "Sunset", "Maghrib", "Isha"];

    static JsonElement Golden { get; } =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "calculator_golden.json"))).RootElement;

    public static IEnumerable<object[]> Cases() =>
        Golden.EnumerateObject().SelectMany(city =>
            city.Value.GetProperty("days").EnumerateObject().Select(day => new object[] { city.Name, day.Name }));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matches_the_android_calculator_exactly(string city, string day)
    {
        var c = Golden.GetProperty(city);
        var calculator = new PrayerCalculator
        {
            Method = Enum.Parse<CalculationMethod>(c.GetProperty("method").GetString()!, ignoreCase: true),
            AsrJuristic = c.GetProperty("asrJuristic").GetInt32(),
            AdjustHighLats = 3,
        };

        var produced = calculator.GetPrayerTimes(DateOnly.Parse(day),
            c.GetProperty("lat").GetDouble(), c.GetProperty("lng").GetDouble(), c.GetProperty("tz").GetDouble());

        var expected = c.GetProperty("days").GetProperty(day);
        foreach (var key in Keys)
            Assert.True(expected.GetProperty(key).GetString() == produced[key],
                $"{city} {day} {key}: c#={produced[key]} kotlin={expected.GetProperty(key).GetString()}");
    }

    [Fact]
    public void Golden_dump_covers_every_method()
    {
        var tested = Golden.EnumerateObject()
            .Select(c => Enum.Parse<CalculationMethod>(c.Value.GetProperty("method").GetString()!, ignoreCase: true))
            .ToHashSet();
        Assert.Equal(Enum.GetValues<CalculationMethod>().ToHashSet(), tested);
    }

    [Theory]
    [InlineData("SA", CalculationMethod.Makkah)]
    [InlineData("kw", CalculationMethod.Makkah)]
    [InlineData("EG", CalculationMethod.Egypt)]
    [InlineData("PK", CalculationMethod.Karachi)]
    [InlineData("IR", CalculationMethod.Tehran)]
    [InlineData("US", CalculationMethod.Isna)]
    [InlineData("IQ", CalculationMethod.Mwl)]
    [InlineData("", CalculationMethod.Mwl)]
    public void Auto_method_matches_the_app(string country, CalculationMethod expected) =>
        Assert.Equal(expected, PrayerCalculator.MethodForCountry(country));
}
