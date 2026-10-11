using System.Windows.Input;
using System.Windows.Interop;

namespace PresenterTools;

public sealed class CaptureHotkey : IDisposable
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    private const int HotkeyId = 1;
    private const uint ModNoRepeat = 0x4000;
    private const int WmHotkey = 0x0312;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _sink;
    private readonly Action _onPressed;
    private bool _registered;

    public CaptureHotkey(Action onPressed)
    {
        _onPressed = onPressed;
        _sink = new HwndSource(new HwndSourceParameters("PresenterToolsHotkey") { ParentWindow = HwndMessage, Width = 0, Height = 0 });
        _sink.AddHook(OnMessage);
    }

    public bool IsRegistered => _registered;

    public bool Apply(CaptureSettings settings)
    {
        Unregister();
        _registered = RegisterHotKey(_sink.Handle, HotkeyId, settings.Modifiers | ModNoRepeat, settings.VirtualKey);
        return _registered;
    }

    public static string Describe(CaptureSettings settings)
    {
        var parts = new List<string>();
        if ((settings.Modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((settings.Modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((settings.Modifiers & ModShift) != 0) parts.Add("Shift");
        if ((settings.Modifiers & ModWin) != 0) parts.Add("Win");
        var key = KeyInterop.KeyFromVirtualKey((int)settings.VirtualKey);
        parts.Add(key == Key.Snapshot ? "Print Screen" : key.ToString());
        return string.Join("+", parts);
    }

    public static string ConflictMessage(CaptureSettings settings) =>
        $"The capture shortcut {Describe(settings)} could not be registered: it is held by another application (for example another screenshot tool), which must release it, or the shortcut can be changed in Settings > Capture.";

    public void Dispose()
    {
        Unregister();
        _sink.Dispose();
    }

    private void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_sink.Handle, HotkeyId);
            _registered = false;
        }
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _onPressed();
        }
        return IntPtr.Zero;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
