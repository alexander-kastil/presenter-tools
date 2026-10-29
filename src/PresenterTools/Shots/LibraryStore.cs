using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace PresenterTools.Shots;

internal sealed record CaptureEntry(string Id, DateTime Time)
{
    public string PngPath => Path.Combine(LibraryStore.Root, Id + ".png");
    public string JsonPath => Path.Combine(LibraryStore.Root, Id + ".json");
}

internal static class LibraryStore
{
    const string IdFormat = "yyyyMMdd-HHmmss-fff";
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Root { get; private set; } = AppSettings.LoadCapture().SaveFolder;

    public static void UseFolder(string folder) => Root = folder;

    public static (int Moved, List<string> Skipped) MoveAll(string from, string to)
    {
        var moved = 0;
        var skipped = new List<string>();
        Directory.CreateDirectory(to);
        foreach (var entry in List(from))
        {
            var png = Path.Combine(from, entry.Id + ".png");
            var json = Path.Combine(from, entry.Id + ".json");
            var targetPng = Path.Combine(to, entry.Id + ".png");
            var targetJson = Path.Combine(to, entry.Id + ".json");
            var hasJson = File.Exists(json);
            if (File.Exists(targetPng) || (hasJson && File.Exists(targetJson)))
            {
                skipped.Add(entry.Id);
                continue;
            }

            File.Move(png, targetPng);
            if (hasJson)
            {
                File.Move(json, targetJson);
            }

            moved++;
        }

        return (moved, skipped);
    }

    public static CaptureEntry Add(BitmapSource image)
    {
        Directory.CreateDirectory(Root);
        var now = DateTime.Now;
        var entry = new CaptureEntry(now.ToString(IdFormat, CultureInfo.InvariantCulture), now);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(entry.PngPath))
        {
            encoder.Save(stream);
        }

        SaveSidecar(entry, new Sidecar([]));
        return entry;
    }

    public static List<CaptureEntry> List() => List(Root);

    public static List<CaptureEntry> List(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var entries = new List<CaptureEntry>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.png"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (DateTime.TryParseExact(id, IdFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                entries.Add(new CaptureEntry(id, time));
            }
        }

        return entries.OrderByDescending(e => e.Time).ToList();
    }

    public static BitmapSource LoadImage(CaptureEntry entry, int decodeWidth = 0)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.UriSource = new Uri(entry.PngPath);
        if (decodeWidth > 0)
        {
            bitmap.DecodePixelWidth = decodeWidth;
        }

        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public static Sidecar LoadSidecar(CaptureEntry entry)
    {
        if (!File.Exists(entry.JsonPath))
        {
            return new Sidecar([]);
        }

        try
        {
            return JsonSerializer.Deserialize<Sidecar>(File.ReadAllText(entry.JsonPath), Options) ?? new Sidecar([]);
        }
        catch (JsonException)
        {
            return new Sidecar([]);
        }
    }

    public static void SaveSidecar(CaptureEntry entry, Sidecar sidecar)
    {
        var temp = entry.JsonPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(sidecar, Options));
        File.Move(temp, entry.JsonPath, true);
    }
}
