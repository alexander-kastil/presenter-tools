using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PresenterTools.Shots;

internal static class ScreenGrabber
{
    public static (BitmapSource Image, int X, int Y) Grab()
    {
        var x = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
        var y = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        var w = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
        var h = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);

        var screen = Native.GetDC(IntPtr.Zero);
        var memory = Native.CreateCompatibleDC(screen);
        var bitmap = Native.CreateCompatibleBitmap(screen, w, h);
        var previous = Native.SelectObject(memory, bitmap);
        Native.BitBlt(memory, 0, 0, w, h, screen, x, y, Native.SRCCOPY | Native.CAPTUREBLT);
        Native.SelectObject(memory, previous);

        var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        var opaque = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
        opaque.Freeze();

        Native.DeleteObject(bitmap);
        Native.DeleteDC(memory);
        Native.ReleaseDC(IntPtr.Zero, screen);
        return (opaque, x, y);
    }
}
