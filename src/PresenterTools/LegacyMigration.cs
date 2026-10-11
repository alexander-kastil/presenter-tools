using System.IO;
using Microsoft.Win32;

namespace PresenterTools;

public static class LegacyMigration
{
    private const string OldName = "ScreenTools";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Run()
    {
        CopyFolder(Environment.SpecialFolder.ApplicationData);
        CopyFolder(Environment.SpecialFolder.LocalApplicationData);
        MoveAutostart();
    }

    private static void CopyFolder(Environment.SpecialFolder root)
    {
        var parent = Environment.GetFolderPath(root);
        var oldFolder = Path.Combine(parent, OldName);
        var newFolder = Path.Combine(parent, "PresenterTools");
        if (Directory.Exists(newFolder) || !Directory.Exists(oldFolder))
        {
            return;
        }
        Copy(oldFolder, newFolder);
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.GetDirectories(source))
        {
            Copy(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    private static void MoveAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(OldName) is not string)
        {
            return;
        }
        key.SetValue("PresenterTools", $"\"{Environment.ProcessPath}\" {Autostart.Argument}");
        key.DeleteValue(OldName, throwOnMissingValue: false);
    }
}
