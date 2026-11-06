using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace IslamicNashat.Core;

/// <summary>A place from the Android app's muslim-data database.</summary>
public sealed record Place(
    int Id,
    string Name,
    string CountryCode,
    string CountryName,
    double Latitude,
    double Longitude,
    bool HasFixedTimetable,
    int? TimetableId = null)
{
    /// <summary>Some cities share a neighbour's timetable; this is whose rows to read.</summary>
    public int SourceId => TimetableId ?? Id;

    public string Label => string.IsNullOrEmpty(CountryName) ? Name : $"{Name}, {CountryName}";

    /// <summary>Where the app starts before anyone chooses anything (the user's city).</summary>
    public static Place Default { get; } = new(77353, "Bardarash", "IQ", "Iraq", 36.501899, 43.584857, true);
}

/// <summary>Raw times of one day from the fixed timetable, as "HH:mm".</summary>
public sealed record FixedRow(string Fajr, string Sunrise, string Dhuhr, string Asr, string Maghrib, string Isha);

/// <summary>
/// Read-only access to Assets\locations.db: the country, location and prayer_time tables of the
/// Android app's muslim_db, so both apps resolve a city's times from the same rows.
/// </summary>
public static class Places
{
    const string Select = """
        SELECT l._id, l.name, c.code, c.name, l.latitude, l.longitude, l.has_fixed_prayer_time, l.prayer_dependent_id
        FROM location l JOIN country c ON c._id = l.country_id
        """;

    static string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = AppPaths.LocationsDb,
        Mode = SqliteOpenMode.ReadOnly,
        Cache = SqliteCacheMode.Shared,
    }.ToString();

    static SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Prefix matches first, then cities with a fixed timetable (they agree with the phone).</summary>
    public static IReadOnlyList<Place> Search(string query, int limit = 40)
    {
        query = query.Trim();
        if (query.Length < 2) return [];
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = Select + """

            WHERE l.name LIKE $contains COLLATE NOCASE OR c.name LIKE $prefix COLLATE NOCASE
            ORDER BY
              CASE WHEN l.name LIKE $prefix COLLATE NOCASE THEN 0 WHEN l.name LIKE $contains COLLATE NOCASE THEN 1 ELSE 2 END,
              l.has_fixed_prayer_time DESC, LENGTH(l.name), l.name
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$contains", $"%{query}%");
        cmd.Parameters.AddWithValue("$prefix", $"{query}%");
        cmd.Parameters.AddWithValue("$limit", limit);
        return Read(cmd);
    }

    public static Place? ById(int id)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = Select + " WHERE l._id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return Read(cmd).FirstOrDefault();
    }

    /// <summary>
    /// The closest known place. A city with a fixed timetable within 40 km wins over a merely
    /// closer one without: agreeing with the phone matters more than a few kilometres.
    /// </summary>
    public static Place? Nearest(double latitude, double longitude)
    {
        const double span = 1.5;
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = Select + " WHERE l.latitude BETWEEN $lat0 AND $lat1 AND l.longitude BETWEEN $lng0 AND $lng1";
        cmd.Parameters.AddWithValue("$lat0", latitude - span);
        cmd.Parameters.AddWithValue("$lat1", latitude + span);
        cmd.Parameters.AddWithValue("$lng0", longitude - span);
        cmd.Parameters.AddWithValue("$lng1", longitude + span);
        var sorted = Read(cmd).OrderBy(p => DistanceKm(latitude, longitude, p.Latitude, p.Longitude)).ToList();
        if (sorted.Count == 0) return null;

        var firstFixed = sorted.FirstOrDefault(p => p.HasFixedTimetable);
        if (firstFixed is not null && DistanceKm(latitude, longitude, firstFixed.Latitude, firstFixed.Longitude) <= 40)
            return firstFixed;
        return sorted[0];
    }

    public static FixedRow? FixedTimes(Place place, DateOnly date)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT fajr, sunrise, dhuhr, asr, maghrib, isha FROM prayer_time WHERE location_id = $id AND date = $date";
        cmd.Parameters.AddWithValue("$id", place.SourceId);
        cmd.Parameters.AddWithValue("$date", date.ToString("MM-dd", CultureInfo.InvariantCulture));
        using var r = cmd.ExecuteReader();
        return r.Read() ? new FixedRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)) : null;
    }

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * 6371.0 * Math.Asin(Math.Sqrt(a));
    }

    static List<Place> Read(SqliteCommand cmd)
    {
        var list = new List<Place>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Place(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDouble(4), r.GetDouble(5),
                r.GetInt32(6) != 0, r.IsDBNull(7) ? null : r.GetInt32(7)));
        return list;
    }

    static readonly HttpClient Http = CreateClient();

    /// <summary>
    /// A desktop has no GPS, so this is IP geolocation: right to the city, wrong behind a VPN.
    /// Returns the matched place and how far the reported point is from it.
    /// </summary>
    public static async Task<(Place Place, double DistanceKm)?> DetectAsync(CancellationToken ct = default)
    {
        (string Url, string Lat, string Lng)[] providers =
        [
            ("https://ipapi.co/json/", "latitude", "longitude"),
            ("https://ipwho.is/", "latitude", "longitude"),
            ("http://ip-api.com/json/?fields=status,lat,lon", "lat", "lon"),
        ];
        foreach (var (url, latKey, lngKey) in providers)
        {
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
                if (!doc.RootElement.TryGetProperty(latKey, out var lat) || !doc.RootElement.TryGetProperty(lngKey, out var lng)) continue;
                if (lat.ValueKind != JsonValueKind.Number || lng.ValueKind != JsonValueKind.Number) continue;
                var place = Nearest(lat.GetDouble(), lng.GetDouble());
                if (place is null) continue;
                return (place, DistanceKm(lat.GetDouble(), lng.GetDouble(), place.Latitude, place.Longitude));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Log.Info($"Geolocation via {url} failed: {ex.Message}");
            }
        }
        return null;
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IslamicNashat-Windows");
        return client;
    }
}
