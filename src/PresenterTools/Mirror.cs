using System.Runtime.InteropServices;

namespace PresenterTools;

// A device mirrored over AirPlay: the device kind from its model ("iPad13,8" is an iPad), its name, and
// the mirror window's picture on screen in physical pixels.
public sealed record Mirror(string Device, string Name, int Left, int Top, int Width, int Height)
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeMethods.RECT rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

    public static string DeviceOf(string model) => new(model.TakeWhile(char.IsLetter).ToArray());

    public static Mirror? Find(Receiver receiver) =>
        receiver.EngineProcessId is { } id && receiver.ClientModel is { } model && PictureOf(id) is { } picture
            ? new Mirror(DeviceOf(model), receiver.ClientName ?? "", picture.Left, picture.Top, picture.Right - picture.Left, picture.Bottom - picture.Top)
            : null;

    internal static NativeMethods.RECT? PictureOf(int processId)
    {
        foreach (var hwnd in MirrorTitle.FindWindows(processId))
        {
            if (!GetClientRect(hwnd, out var client) || client.Right <= 0 || client.Bottom <= 0)
            {
                continue;
            }
            var origin = new POINT();
            ClientToScreen(hwnd, ref origin);
            return new NativeMethods.RECT { Left = origin.X, Top = origin.Y, Right = origin.X + client.Right, Bottom = origin.Y + client.Bottom };
        }
        return null;
    }
}
