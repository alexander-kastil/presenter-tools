using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PresenterTools;

public static class StrokeRenderer
{
    private const double MaxSide = 1024;
    private const double PaddingRatio = 0.1;
    private const double MinPadding = 8;

    public static (byte[] Png, Rect Box) Render(StrokeCollection strokes)
    {
        var bounds = strokes.GetBounds();
        var pad = Math.Max(MinPadding, PaddingRatio * Math.Max(bounds.Width, bounds.Height));
        var box = new Rect(bounds.X - pad, bounds.Y - pad, bounds.Width + 2 * pad, bounds.Height + 2 * pad);
        var scale = Math.Min(1, MaxSide / Math.Max(box.Width, box.Height));
        var width = Math.Max(1, (int)Math.Ceiling(box.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(box.Height * scale));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.PushTransform(new ScaleTransform(width / box.Width, height / box.Height));
            dc.PushTransform(new TranslateTransform(-box.X, -box.Y));
            foreach (var stroke in strokes)
            {
                var black = stroke.Clone();
                black.DrawingAttributes.Color = Colors.Black;
                black.Draw(dc);
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return (stream.ToArray(), box);
    }
}
