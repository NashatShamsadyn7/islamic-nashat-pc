using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IslamicNashat.Core;

public sealed record UpdateInfo(Version Version, string DownloadUrl, long Bytes);

/// <summary>Checks the GitHub release feed and installs a newer version on request.</summary>
public static class Updater
{
    const string LatestRelease = "https://api.github.com/repos/NashatShamsadyn7/islamic-nashat-pc/releases/latest";
    static readonly HttpClient Http = CreateClient();

    public static Version Current { get; } = Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <returns>The newer release, or null when this is the latest.</returns>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(LatestRelease, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no release published yet
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
        latest = Normalize(latest);
        if (latest <= Current) return null;

        var setup = root.GetProperty("assets").EnumerateArray().FirstOrDefault(a =>
        {
            var name = a.GetProperty("name").GetString() ?? "";
            return name.StartsWith("IslamicNashat-Setup", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        });
        if (setup.ValueKind == JsonValueKind.Undefined) return null;
        return new UpdateInfo(latest, setup.GetProperty("browser_download_url").GetString()!, setup.GetProperty("size").GetInt64());
    }

    /// <returns>Path of the downloaded installer.</returns>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double> progress, CancellationToken ct)
    {
        var target = Path.Combine(Path.GetTempPath(), $"IslamicNashat-Setup-{update.Version}.exe");
        using var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var file = File.Create(target))
        {
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                progress.Report((double)done / update.Bytes);
            }
        }
        if (new FileInfo(target).Length != update.Bytes)
            throw new IOException("The update download was incomplete.");
        return target;
    }

    /// <summary>Runs the installer silently; it closes this app, upgrades, and starts it again.</summary>
    public static void Install(string installerPath) =>
        Process.Start(new ProcessStartInfo(installerPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH") { UseShellExecute = true });

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IslamicNashat-Updater");
        return client;
    }
}
