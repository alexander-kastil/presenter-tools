using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace PresenterTools.Tests;

public class Diag(ITestOutputHelper output)
{
    [Fact]
    public void Toolbar()
    {
        var file = Path.Combine(Path.GetTempPath(), $"pt-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, "{\"Toolbar\":{\"X\":7263,\"Y\":995}}");
        AppSettings.FilePathOverride = file;
        Sta.Run(async () =>
        {
            var session = new Session();
            foreach (var bounds in NativeMethods.GetMonitorBounds())
            {
                var overlay = new OverlayWindow(session, bounds);
                session.Overlays.Add(overlay);
                overlay.Show();
            }
            var receiver = new Receiver();
            var w = new ToolbarWindow(session, receiver, new StatusWindow(receiver));
            session.Toolbar = w; w.Show(); session.SetMode(Mode.Mouse);
            await Sta.Settle();
            Shots.Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(w).Handle, out var r);
            output.WriteLine($"workarea {System.Windows.SystemParameters.WorkArea} dpi {System.Windows.Media.VisualTreeHelper.GetDpi(w).DpiScaleX}"); output.WriteLine($"rect {r.Left},{r.Top},{r.Right},{r.Bottom} left={w.Left} top={w.Top}");
            var undo = session.HideForCapture(); undo(); await Sta.Settle();
            Shots.Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(w).Handle, out r);
            output.WriteLine($"after hide/show {r.Left},{r.Top}");
            Shots.LibraryWindow.ShowOrActivate(); await Sta.Settle(1500);
            Shots.Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(w).Handle, out r);
            output.WriteLine($"docked {r.Left},{r.Top}");
            Shots.LibraryWindow.Toggle(); await Sta.Settle(1500);
            Shots.Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(w).Handle, out r);
            output.WriteLine($"after library close {r.Left},{r.Top}");
            foreach (var o in session.Overlays) o.Close();
            w.Close();
        });
        File.Delete(file);
    }
}
