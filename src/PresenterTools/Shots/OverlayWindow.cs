using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PresenterTools.Shots;

internal sealed class OverlayWindow : Window
{
    readonly BitmapSource shot;
    readonly int originX;
    readonly int originY;
    readonly Grid root = new();
    readonly Path mask = new() { Fill = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), IsHitTestVisible = false };
    readonly Rectangle frame = new() { Stroke = Brushes.White, StrokeThickness = 1, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    System.Drawing.Point? anchor;

    public BitmapSource? Result { get; private set; }

    public OverlayWindow(BitmapSource shot, int originX, int originY)
    {
        this.shot = shot;
        this.originX = originX;
        this.originY = originY;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var canvas = new Canvas { IsHitTestVisible = false };
        canvas.Children.Add(mask);
        canvas.Children.Add(frame);
        root.Children.Add(new Image { Source = shot, Stretch = Stretch.Fill, IsHitTestVisible = false });
        root.Children.Add(canvas);
        Content = root;

        SourceInitialized += (_, _) => Cover();
        ContentRendered += (_, _) =>
        {
            Cover();
            Activate();
            UpdateSelection(null);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
    }

    void Cover()
    {
        var handle = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(handle, Native.HWND_TOPMOST, originX, originY, shot.PixelWidth, shot.PixelHeight, 0);
    }

    System.Drawing.Point CursorPixel()
    {
        Native.GetCursorPos(out var p);
        return new System.Drawing.Point(
            Math.Clamp(p.X - originX, 0, shot.PixelWidth - 1),
            Math.Clamp(p.Y - originY, 0, shot.PixelHeight - 1));
    }

    static Int32Rect Normalize(System.Drawing.Point a, System.Drawing.Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new Int32Rect(x, y, Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);
    }

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        anchor = CursorPixel();
        CaptureMouse();
        UpdateSelection(Normalize(anchor.Value, anchor.Value));
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (anchor is { } a)
        {
            UpdateSelection(Normalize(a, CursorPixel()));
        }
    }

    void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (anchor is not { } a)
        {
            return;
        }

        ReleaseMouseCapture();
        anchor = null;
        var rect = Normalize(a, CursorPixel());
        if (rect.Width < 3 || rect.Height < 3)
        {
            UpdateSelection(null);
            return;
        }

        var cropped = new CroppedBitmap(shot, rect);
        cropped.Freeze();
        Result = cropped;
        Close();
    }

    void UpdateSelection(Int32Rect? pixels)
    {
        var full = new RectangleGeometry(new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        if (pixels is not { } p)
        {
            frame.Visibility = Visibility.Collapsed;
            mask.Data = full;
            return;
        }

        var sx = root.ActualWidth / shot.PixelWidth;
        var sy = root.ActualHeight / shot.PixelHeight;
        var rect = new Rect(p.X * sx, p.Y * sy, p.Width * sx, p.Height * sy);
        mask.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(rect));
        frame.Visibility = Visibility.Visible;
        frame.Width = rect.Width;
        frame.Height = rect.Height;
        Canvas.SetLeft(frame, rect.X);
        Canvas.SetTop(frame, rect.Y);
    }
}
