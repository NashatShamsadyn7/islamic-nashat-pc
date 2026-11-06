using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace IslamicNashat.Core;

public sealed record Reciter(string Id, string ArabicName, string EnglishName, string? FileName)
{
    public bool IsBundled => FileName is null;
    public string Name(string language) => language == "en" ? EnglishName : ArabicName;
}

/// <summary>The same recitations as the Android app (AthanDownloadManager), from the same GitHub folder.</summary>
public static class AdhanCatalog
{
    public const string BundledId = "makkah";
    const string BaseUrl = "https://raw.githubusercontent.com/NashatShamsadyn7/islamic-app-assets/main/azan/";

    public static IReadOnlyList<Reciter> All { get; } =
    [
        new(BundledId, "أذان مكة", "Makkah", null),
        new("islam_sobhi", "إسلام صبحي", "Islam Sobhi", "Islam-Sobhi.mp3"),
        new("peshawa_qader", "بيشوا قادر الكردي", "Peshawa Qader Kurdi", "Peshawa-Qader-Kurdi.mp3"),
        new("hassan_awnan", "حسن العونان", "Hassan Al-Awnan", "Hassan-Al-Awnan.mp3"),
        new("raad_kurdi", "رعد الكردي", "Raad Kurdi", "Raad-Kurdi.mp3"),
        new("omar_hisham", "عمر هشام", "Omar Hisham", "Omar-Hisham.mp3"),
        new("muhammad_afaq_lone", "محمد آفاق لون", "Muhammad Afaq Lone", "Muhammad-Afaq-Lone.mp3"),
        new("muhammad_haidan", "محمد الهيدان", "Muhammad Al-Haidan", "Muhammad-Al-Haidan.mp3"),
        new("muhammad_abdullah", "محمد عبدالله", "Muhammad Abdullah", "Muhammad-Abdullah.mp3"),
        new("muhammad_marwan_qasir", "محمد مروان قصير", "Muhammad Marwan Qasir", "Muhammad-Marwan-Qasir.mp3"),
        new("muhammad_marwan", "محمد مروان", "Muhammad Marwan", "Muhammad-Marwan.mp3"),
        new("nasser_qatami", "ناصر القطامي", "Nasser Al-Qatami", "Nasser-Al-Qatami.mp3"),
    ];

    public static Reciter? Find(string id) => All.FirstOrDefault(r => r.Id == id);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    static readonly SemaphoreSlim DownloadGate = new(1, 1);

    /// <summary>Local file of a recitation, or null if it has not been downloaded yet.</summary>
    public static string? LocalPath(Reciter r)
    {
        if (r.IsBundled) return AppPaths.DefaultAdhan;
        var path = Path.Combine(AppPaths.SoundsDir, r.FileName!);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Downloads a recitation once; later calls return the cached file.</summary>
    public static async Task<string> EnsureAsync(Reciter r, CancellationToken ct = default)
    {
        if (LocalPath(r) is { } existing) return existing;
        await DownloadGate.WaitAsync(ct);
        try
        {
            if (LocalPath(r) is { } raced) return raced;
            Directory.CreateDirectory(AppPaths.SoundsDir);
            var target = Path.Combine(AppPaths.SoundsDir, r.FileName!);
            var tmp = target + ".part";
            await using (var source = await Http.GetStreamAsync(BaseUrl + r.FileName, ct))
            await using (var file = File.Create(tmp))
                await source.CopyToAsync(file, ct);
            if (new FileInfo(tmp).Length < 10_000) throw new IOException("The recitation download was too small to be audio.");
            File.Move(tmp, target, overwrite: true);
            return target;
        }
        finally
        {
            DownloadGate.Release();
        }
    }
}

/// <summary>Plays the adhan, the short tone and previews. Must be created and used on the UI thread.</summary>
public sealed class AdhanPlayer
{
    readonly MediaPlayer _player = new();
    public event Action? PlayingChanged;
    public bool IsPlaying { get; private set; }
    /// <summary>What is playing: a prayer's adhan, or null for a preview or a tone.</summary>
    public Prayer? PlayingFor { get; private set; }

    public AdhanPlayer()
    {
        _player.MediaEnded += (_, _) => SetPlaying(false, null);
        _player.MediaFailed += (_, e) =>
        {
            Log.Error("Audio playback failed", e.ErrorException);
            SetPlaying(false, null);
        };
    }

    public void Play(string file, int volume, Prayer? forPrayer = null)
    {
        try
        {
            _player.Stop();
            _player.Open(new Uri(file));
            _player.Volume = Math.Clamp(volume, 0, 100) / 100.0;
            _player.Play();
            SetPlaying(true, forPrayer);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not play {file}", ex);
            SetPlaying(false, null);
        }
    }

    /// <summary>The adhan for a prayer: its reciter if downloaded, otherwise the bundled Makkah adhan.</summary>
    public void PlayAdhan(Prayer prayer, AppSettings s)
    {
        switch (s.SoundFor(prayer))
        {
            case AdhanMode.Silent:
                return;
            case AdhanMode.Short:
                PlayTone(s.Volume);
                return;
        }
        var reciter = AdhanCatalog.Find(prayer == Prayer.Fajr ? s.FajrReciter : s.Reciter) ?? AdhanCatalog.All[0];
        Play(AdhanCatalog.LocalPath(reciter) ?? AppPaths.DefaultAdhan, s.Volume, prayer);
    }

    public void PlayTone(int volume) => Play(Tone.File, volume);

    public void Stop()
    {
        _player.Stop();
        _player.Close();
        SetPlaying(false, null);
    }

    void SetPlaying(bool playing, Prayer? prayer)
    {
        if (IsPlaying == playing && PlayingFor == prayer) return;
        IsPlaying = playing;
        PlayingFor = prayer;
        PlayingChanged?.Invoke();
    }
}

/// <summary>A soft two-note chime, synthesised once into a WAV so it plays through the same player and volume.</summary>
static class Tone
{
    static string? _file;

    public static string File => _file ??= Create();

    static string Create()
    {
        var path = Path.Combine(AppPaths.SoundsDir, "chime.wav");
        if (System.IO.File.Exists(path)) return path;
        Directory.CreateDirectory(AppPaths.SoundsDir);

        const int rate = 44100;
        (double Hz, double Start, double Length)[] notes = [(659.25, 0.0, 1.1), (987.77, 0.28, 1.5)];
        var samples = new short[(int)(rate * 1.9)];
        foreach (var (hz, start, length) in notes)
        {
            var first = (int)(start * rate);
            for (var i = 0; i < (int)(length * rate) && first + i < samples.Length; i++)
            {
                var t = (double)i / rate;
                var envelope = Math.Min(1, t / 0.02) * Math.Exp(-3.2 * t); // quick fade in, bell-like decay
                var value = envelope * (Math.Sin(2 * Math.PI * hz * t) + 0.25 * Math.Sin(4 * Math.PI * hz * t)) * 0.35;
                samples[first + i] = (short)Math.Clamp(samples[first + i] + value * short.MaxValue, short.MinValue, short.MaxValue);
            }
        }

        using var w = new BinaryWriter(System.IO.File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + samples.Length * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples.Length * 2);
        foreach (var s in samples) w.Write(s);
        return path;
    }
}
