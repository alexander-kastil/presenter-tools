using System.Runtime.InteropServices;
using System.Text;

namespace PresenterTools;

public static class MirrorTitle
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private const int StableChecks = 3;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetWindowTextW(IntPtr hWnd, string text);

    // Polls until the process's visible top-level windows carry the title for several checks in a row,
    // the process exits, or the timeout passes. Windows owned by other processes are never touched.
    public static async Task ApplyAsync(int processId, string title, Func<bool> processAlive, CancellationToken token)
    {
        var stable = 0;
        var deadline = DateTime.UtcNow + Timeout;
        try
        {
            while (!token.IsCancellationRequested && processAlive() && DateTime.UtcNow < deadline)
            {
                var windows = FindWindows(processId);
                var changed = false;
                foreach (var hwnd in windows)
                {
                    if (GetTitle(hwnd) != title)
                    {
                        SetWindowTextW(hwnd, title);
                        changed = true;
                    }
                }
                stable = windows.Count > 0 && !changed ? stable + 1 : 0;
                if (stable >= StableChecks)
                {
                    return;
                }
                await Task.Delay(PollInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static List<IntPtr> FindWindows(int processId)
    {
        var result = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner == (uint)processId && IsWindowVisible(hwnd))
            {
                result.Add(hwnd);
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var text = new StringBuilder(512);
        GetWindowTextW(hwnd, text, text.Capacity);
        return text.ToString();
    }
}
