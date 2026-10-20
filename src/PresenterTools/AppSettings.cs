using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PresenterTools;

public enum Access
{
    Open,
    Pin,
    Password
}

public sealed record ProviderSettings(string Name, string BaseUrl, string ApiKey, string Model);

public sealed record ReceiverSettings(string Name, Access Access, string Pin, string Password, bool FullScreen);

public sealed record CaptureSettings(uint Modifiers, uint VirtualKey, string SaveFolder)
{
    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "ScreenShot");

    public static CaptureSettings Default { get; } = new(0, 0x2C, DefaultFolder);
}

public static class AppSettings
{
    public const string DefaultName = "DeepInfra";
    public const string DefaultBaseUrl = "https://api.deepinfra.com/v1/openai";
    public const string DefaultModel = "Qwen/Qwen3-VL-30B-A3B-Instruct";
    public const string KeyVariable = "DEEPINFRA_API_KEY";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PresenterTools");

    // Tests point this at a temp file so they never touch the real settings.json.
    internal static string? FilePathOverride { get; set; }

    private static string FilePath => FilePathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PresenterTools", "settings.json");

    public static ProviderSettings LoadProvider()
    {
        var root = ReadRoot();
        var provider = root["Provider"] as JsonObject;
        return new ProviderSettings(
            Text(provider?["Name"]) ?? DefaultName,
            Text(provider?["BaseUrl"]) ?? DefaultBaseUrl,
            Unprotect(Text(provider?["ApiKey"])) ?? "",
            Text(root["Model"]) ?? DefaultModel);
    }

    public static string? ResolveApiKey()
    {
        var key = LoadProvider().ApiKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }
        var env = Environment.GetEnvironmentVariable(KeyVariable);
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }

    public static void SaveProvider(ProviderSettings data)
    {
        var root = ReadRoot();
        root["Provider"] = new JsonObject
        {
            ["Name"] = data.Name,
            ["BaseUrl"] = data.BaseUrl,
            ["ApiKey"] = Protect(data.ApiKey)
        };
        root["Model"] = data.Model;
        WriteRoot(root);
    }

    public static ReceiverSettings LoadReceiver()
    {
        var receiver = ReadRoot()["Receiver"] as JsonObject;
        var access = Enum.TryParse<Access>(Text(receiver?["Access"]), ignoreCase: true, out var parsed) ? parsed : Access.Open;
        var pin = Unprotect(Text(receiver?["Pin"]));
        var fullScreen = receiver?["FullScreen"] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
        return new ReceiverSettings(
            Text(receiver?["Name"]) ?? Environment.MachineName,
            access,
            IsValidPin(pin) ? pin! : NewPin(),
            Unprotect(receiver?["Password"] is JsonValue password && password.TryGetValue<string>(out var text) ? text : null) ?? "",
            fullScreen);
    }

    public static void SaveReceiver(ReceiverSettings data)
    {
        var root = ReadRoot();
        root["Receiver"] = new JsonObject
        {
            ["Name"] = data.Name,
            ["Access"] = data.Access.ToString(),
            ["Pin"] = Protect(data.Pin),
            ["Password"] = Protect(data.Password),
            ["FullScreen"] = data.FullScreen
        };
        WriteRoot(root);
    }

    public static CaptureSettings LoadCapture()
    {
        var capture = ReadRoot()["Capture"] as JsonObject;
        var modifiers = Number(capture?["Modifiers"]);
        var key = Number(capture?["VirtualKey"]);
        var folder = ValidFolder(Text(capture?["SaveFolder"])) ?? CaptureSettings.DefaultFolder;
        return key is > 0 and < 256 && modifiers is { } mods && mods <= 0xF
            ? new CaptureSettings(mods, key.Value, folder)
            : CaptureSettings.Default with { SaveFolder = folder };
    }

    public static void SaveCapture(CaptureSettings data)
    {
        var root = ReadRoot();
        root["Capture"] = new JsonObject
        {
            ["Modifiers"] = data.Modifiers,
            ["VirtualKey"] = data.VirtualKey,
            ["SaveFolder"] = ValidFolder(data.SaveFolder) ?? CaptureSettings.DefaultFolder
        };
        WriteRoot(root);
    }

    public static (int X, int Y)? LoadToolbarPosition()
    {
        var toolbar = ReadRoot()["Toolbar"] as JsonObject;
        return Integer(toolbar?["X"]) is { } x && Integer(toolbar?["Y"]) is { } y ? (x, y) : null;
    }

    public static void SaveToolbarPosition(int x, int y)
    {
        var root = ReadRoot();
        root["Toolbar"] = new JsonObject { ["X"] = x, ["Y"] = y };
        WriteRoot(root);
    }

    public static string? ValidFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
        {
            return null;
        }
        try
        {
            var full = Path.GetFullPath(path.Trim());
            var root = Path.GetPathRoot(full);
            return full == root ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static bool IsValidPin(string? pin) => pin is { Length: 4 } && pin.All(char.IsAsciiDigit);

    private const string Marker = "dpapi:";

    private static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }
        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        return Marker + Convert.ToBase64String(cipher);
    }

    private static string? Unprotect(string? stored)
    {
        if (stored is null || !stored.StartsWith(Marker, StringComparison.Ordinal))
        {
            return stored;
        }
        try
        {
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(stored[Marker.Length..]), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
    }

    private static string NewPin() => Random.Shared.Next(0, 10000).ToString("D4");

    private static void WriteRoot(JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, root.ToJsonString(Indented));
    }

    private static JsonObject ReadRoot()
    {
        try
        {
            return File.Exists(FilePath) ? JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static uint? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<uint>(out var number) ? number : null;

    private static int? Integer(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
}
