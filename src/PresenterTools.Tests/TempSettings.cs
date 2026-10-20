using System.IO;

namespace PresenterTools.Tests;

// Points AppSettings at a throwaway file so no test reads or writes the real %APPDATA% settings.json.
internal sealed class TempSettings : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pt-tests-" + Guid.NewGuid().ToString("N"));

    public TempSettings(string? json = null)
    {
        Directory.CreateDirectory(_folder);
        File = Path.Combine(_folder, "settings.json");
        if (json is not null)
        {
            System.IO.File.WriteAllText(File, json);
        }

        AppSettings.FilePathOverride = File;
    }

    public string File { get; }

    public void Dispose()
    {
        AppSettings.FilePathOverride = null;
        Directory.Delete(_folder, true);
    }
}
