using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PresenterTools.Shots;

using Shape = System.Windows.Shapes.Shape;

internal sealed record Annotation(string Type, double X1, double Y1, double X2, double Y2, string? Text, double[]? Points = null, double Thickness = 0, double FontSize = 0, string? Color = null, string? Kind = null, bool Filled = false);

internal enum ShapeKind { Rectangle, RoundedRectangle, Ellipse, Polygon }

internal sealed record CropRect(double X, double Y, double W, double H);

internal sealed record Sidecar(List<Annotation> Annotations, CropRect? Crop = null);

internal static class Annotations
{
    public static readonly Color InkColor = Colors.Red;
    public const double StrokeWidth = 3;
    public const double ArrowWidth = 6;
    public const double FontPixels = 24;
    public const int FillTolerance = 48;
    public static readonly FontFamily Font = new("Segoe UI");

    public static Color ColorOf(string? color) =>
        color is not null && ColorConverter.ConvertFromString(color) is Color c ? c : InkColor;

    public static Brush BrushOf(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static UIElement Create(Annotation a)
    {
        var width = a.Thickness > 0 ? a.Thickness : StrokeWidth;
        var ink = BrushOf(ColorOf(a.Color));
        switch (a.Type)
        {
            case "pen":
                return NewPen(a.Points ?? [], width, ink);
            case "shape":
                return NewShape(a, width, ink);
            case "box":
                var box = new Rectangle { Stroke = ink, StrokeThickness = width, Width = Math.Abs(a.X2 - a.X1), Height = Math.Abs(a.Y2 - a.Y1) };
                Canvas.SetLeft(box, Math.Min(a.X1, a.X2));
                Canvas.SetTop(box, Math.Min(a.Y1, a.Y2));
                return box;
            case "line":
                return NewLine(a.X1, a.Y1, a.X2, a.Y2, width, ink);
            case "arrow":
                return NewArrow(a.X1, a.Y1, a.X2, a.Y2, width, ink);
            default:
                var label = NewText(a.Text ?? string.Empty, a.FontSize > 0 ? a.FontSize : FontPixels, ink);
                Canvas.SetLeft(label, a.X1);
                Canvas.SetTop(label, a.Y1);
                return label;
        }
    }

    public static Line NewLine(double x1, double y1, double x2, double y2, double width, Brush ink) => new()
    {
        Stroke = ink,
        StrokeThickness = width,
        X1 = x1,
        Y1 = y1,
        X2 = x2,
        Y2 = y2,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    public static double HeadLength(double width) => Math.Max(14, width * 3.5);

    public static Path NewArrow(double x1, double y1, double x2, double y2, double width, Brush ink)
    {
        var from = new Point(x1, y1);
        var tip = new Point(x2, y2);
        var delta = tip - from;
        var length = delta.Length;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            if (length < 0.5)
            {
                context.BeginFigure(from, false, false);
                context.LineTo(tip, true, true);
            }
            else
            {
                var u = delta / length;
                var n = new Vector(-u.Y, u.X);
                var head = Math.Min(length, HeadLength(width));
                var half = head * 0.45;
                var back = tip - u * head;
                context.BeginFigure(from, false, false);
                context.LineTo(tip - u * head * 0.8, true, true);
                context.BeginFigure(tip, true, true);
                context.LineTo(back + n * half, true, true);
                context.LineTo(back - n * half, true, true);
            }
        }

        geometry.Freeze();
        return new Path
        {
            Stroke = ink,
            Fill = ink,
            StrokeThickness = width,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Data = geometry,
        };
    }

    public static Path NewPen(IReadOnlyList<double> points, double width, Brush ink) => new()
    {
        Stroke = ink,
        StrokeThickness = width,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Data = PenGeometry(points),
    };

    public static Geometry PenGeometry(IReadOnlyList<double> p)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var n = p.Count / 2;
            if (n > 0)
            {
                context.BeginFigure(new Point(p[0], p[1]), false, false);
                for (var i = 1; i < n - 1; i++)
                {
                    var mid = new Point((p[2 * i] + p[2 * i + 2]) / 2, (p[2 * i + 1] + p[2 * i + 3]) / 2);
                    context.QuadraticBezierTo(new Point(p[2 * i], p[2 * i + 1]), mid, true, true);
                }

                if (n > 1)
                {
                    context.LineTo(new Point(p[2 * n - 2], p[2 * n - 1]), true, true);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    public static Shape NewShape(Annotation a, double width, Brush ink)
    {
        var filled = a.Filled ? ink : null;
        var kind = Enum.TryParse<ShapeKind>(a.Kind, out var k) ? k : ShapeKind.Rectangle;
        if (kind == ShapeKind.Polygon)
        {
            var points = new PointCollection();
            var p = a.Points ?? [];
            for (var i = 0; i + 1 < p.Length; i += 2)
            {
                points.Add(new Point(p[i], p[i + 1]));
            }

            return new Polygon { Points = points, Stroke = ink, Fill = filled, StrokeThickness = width, StrokeLineJoin = PenLineJoin.Round };
        }

        var w = Math.Abs(a.X2 - a.X1);
        var h = Math.Abs(a.Y2 - a.Y1);
        var radius = kind == ShapeKind.RoundedRectangle ? Math.Min(w, h) / 4 : 0;
        Shape shape = kind == ShapeKind.Ellipse ? new Ellipse() : new Rectangle { RadiusX = radius, RadiusY = radius };
        shape.Stroke = ink;
        shape.Fill = filled;
        shape.StrokeThickness = width;
        shape.Width = w;
        shape.Height = h;
        Canvas.SetLeft(shape, Math.Min(a.X1, a.X2));
        Canvas.SetTop(shape, Math.Min(a.Y1, a.Y2));
        return shape;
    }

    public static TextBlock NewText(string text, double size, Brush ink) => new()
    {
        Text = text,
        Foreground = ink,
        FontSize = size,
        FontFamily = Font,
        IsHitTestVisible = false,
    };

    public static Image NewFill(Canvas canvas, double seedX, double seedY, double dx, double dy, Color ink)
    {
        var w = (int)canvas.Width;
        var h = (int)canvas.Height;
        var fill = new Image { Width = w, Height = h, Stretch = Stretch.Fill, IsHitTestVisible = false, Tag = Rect.Empty };
        Canvas.SetLeft(fill, dx);
        Canvas.SetTop(fill, dy);
        RenderOptions.SetBitmapScalingMode(fill, BitmapScalingMode.NearestNeighbor);
        var sx = (int)Math.Floor(seedX);
        var sy = (int)Math.Floor(seedY);
        if (sx < 0 || sy < 0 || sx >= w || sy >= h)
        {
            return fill;
        }

        var stride = w * 4;
        var px = new byte[stride * h];
        Render(canvas, w, h).CopyPixels(px, stride, 0);
        var seed = sy * stride + sx * 4;
        byte b0 = px[seed], g0 = px[seed + 1], r0 = px[seed + 2];
        var mask = new byte[px.Length];
        var seen = new bool[w * h];
        var stack = new Stack<int>();
        stack.Push(sy * w + sx);
        seen[sy * w + sx] = true;
        int minX = w, minY = h, maxX = -1, maxY = -1;
        while (stack.Count > 0)
        {
            var i = stack.Pop();
            var o = i * 4;
            if (Math.Abs(px[o] - b0) > FillTolerance || Math.Abs(px[o + 1] - g0) > FillTolerance || Math.Abs(px[o + 2] - r0) > FillTolerance)
            {
                continue;
            }

            mask[o] = ink.B;
            mask[o + 1] = ink.G;
            mask[o + 2] = ink.R;
            mask[o + 3] = 255;
            var x = i % w;
            var y = i / w;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
            if (x > 0 && !seen[i - 1])
            {
                seen[i - 1] = true;
                stack.Push(i - 1);
            }

            if (x < w - 1 && !seen[i + 1])
            {
                seen[i + 1] = true;
                stack.Push(i + 1);
            }

            if (y > 0 && !seen[i - w])
            {
                seen[i - w] = true;
                stack.Push(i - w);
            }

            if (y < h - 1 && !seen[i + w])
            {
                seen[i + w] = true;
                stack.Push(i + w);
            }
        }

        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, mask, stride);
        bitmap.Freeze();
        fill.Source = bitmap;
        fill.Tag = new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        return fill;
    }

    public static Annotation Offset(Annotation a, double dx, double dy) => a.Type switch
    {
        "fill" => a with { X2 = a.X2 + dx, Y2 = a.Y2 + dy },
        _ => a with
        {
            X1 = a.X1 + dx,
            Y1 = a.Y1 + dy,
            X2 = a.X2 + dx,
            Y2 = a.Y2 + dy,
            Points = a.Points?.Select((v, i) => v + (i % 2 == 0 ? dx : dy)).ToArray(),
        },
    };

    public static Rect Bounds(Annotation a, UIElement element)
    {
        var half = (a.Thickness > 0 ? a.Thickness : StrokeWidth) / 2;
        switch (a.Type)
        {
            case "pen":
            case "shape" when a.Points is not null:
                var p = a.Points ?? [];
                if (p.Length < 2)
                {
                    return Rect.Empty;
                }

                var xs = p.Where((_, i) => i % 2 == 0).ToArray();
                var ys = p.Where((_, i) => i % 2 == 1).ToArray();
                var pen = new Rect(new Point(xs.Min(), ys.Min()), new Point(xs.Max(), ys.Max()));
                pen.Inflate(half, half);
                return pen;
            case "box":
            case "shape":
            case "line":
            case "arrow":
                var shape = new Rect(new Point(a.X1, a.Y1), new Point(a.X2, a.Y2));
                var reach = a.Type == "arrow" ? half + HeadLength(a.Thickness > 0 ? a.Thickness : StrokeWidth) : half;
                shape.Inflate(reach, reach);
                return shape;
            case "fill":
                if (element is Image { Tag: Rect r } && !r.IsEmpty)
                {
                    r.Offset(a.X2, a.Y2);
                    return r;
                }

                return Rect.Empty;
            default:
                return element is FrameworkElement f ? new Rect(a.X1, a.Y1, f.ActualWidth, f.ActualHeight) : Rect.Empty;
        }
    }

    public static bool Hit(Annotation a, UIElement element, Point at)
    {
        var tolerance = (a.Thickness > 0 ? a.Thickness : StrokeWidth) / 2 + 4;
        switch (a.Type)
        {
            case "shape" when a.Points is not null:
                var poly = a.Points;
                var n = poly.Length / 2;
                for (var i = 0; i < n; i++)
                {
                    var j = (i + 1) % n;
                    if (Distance(at, new Point(poly[2 * i], poly[2 * i + 1]), new Point(poly[2 * j], poly[2 * j + 1])) <= tolerance)
                    {
                        return true;
                    }
                }

                return a.Filled && Inside(poly, at);
            case "shape" when a.Kind == nameof(ShapeKind.Ellipse):
                var cx = (a.X1 + a.X2) / 2;
                var cy = (a.Y1 + a.Y2) / 2;
                var rx = Math.Abs(a.X2 - a.X1) / 2;
                var ry = Math.Abs(a.Y2 - a.Y1) / 2;
                var dOuter = Math.Pow((at.X - cx) / (rx + tolerance), 2) + Math.Pow((at.Y - cy) / (ry + tolerance), 2);
                var thin = rx - tolerance <= 0 || ry - tolerance <= 0;
                var dInner = thin ? 0 : Math.Pow((at.X - cx) / (rx - tolerance), 2) + Math.Pow((at.Y - cy) / (ry - tolerance), 2);
                return dOuter <= 1 && (a.Filled || thin || dInner >= 1);
            case "pen":
                var p = a.Points ?? [];
                if (p.Length == 2)
                {
                    return Distance(at, new Point(p[0], p[1]), new Point(p[0], p[1])) <= tolerance;
                }

                for (var i = 0; i + 3 < p.Length; i += 2)
                {
                    if (Distance(at, new Point(p[i], p[i + 1]), new Point(p[i + 2], p[i + 3])) <= tolerance)
                    {
                        return true;
                    }
                }

                return false;
            case "line":
            case "arrow":
                return Distance(at, new Point(a.X1, a.Y1), new Point(a.X2, a.Y2)) <= tolerance;
            case "box":
            case "shape":
                var rect = new Rect(new Point(a.X1, a.Y1), new Point(a.X2, a.Y2));
                var outer = rect;
                outer.Inflate(tolerance, tolerance);
                var inner = rect;
                inner.Inflate(-tolerance, -tolerance);
                return outer.Contains(at) && (a.Filled || inner.IsEmpty || !inner.Contains(at));
            case "fill":
                if (element is not Image { Source: BitmapSource source })
                {
                    return false;
                }

                var x = (int)Math.Floor(at.X - a.X2);
                var y = (int)Math.Floor(at.Y - a.Y2);
                if (x < 0 || y < 0 || x >= source.PixelWidth || y >= source.PixelHeight)
                {
                    return false;
                }

                var pixel = new byte[4];
                source.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
                return pixel[3] > 0;
            default:
                return element is FrameworkElement f && new Rect(a.X1, a.Y1, f.ActualWidth, f.ActualHeight).Contains(at);
        }
    }

    static bool Inside(double[] poly, Point at)
    {
        var inside = false;
        var n = poly.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = poly[2 * i], yi = poly[2 * i + 1], xj = poly[2 * j], yj = poly[2 * j + 1];
            if ((yi > at.Y) != (yj > at.Y) && at.X < (xj - xi) * (at.Y - yi) / (yj - yi) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    static double Distance(Point p, Point a, Point b)
    {
        var d = b - a;
        var len = d.LengthSquared;
        var t = len == 0 ? 0 : Math.Clamp(((p - a) * d) / len, 0, 1);
        return (p - (a + d * t)).Length;
    }

    public static void Append(Canvas canvas, Annotation a) =>
        canvas.Children.Add(a.Type == "fill" ? NewFill(canvas, a.X1, a.Y1, a.X2, a.Y2, ColorOf(a.Color)) : Create(a));

    public static void Rebuild(Canvas canvas, IEnumerable<Annotation> annotations)
    {
        if (canvas.Children.Count > 1)
        {
            canvas.Children.RemoveRange(1, canvas.Children.Count - 1);
        }

        foreach (var a in annotations)
        {
            Append(canvas, a);
        }
    }

    public static Canvas Compose(BitmapSource image, IEnumerable<Annotation> annotations)
    {
        var canvas = new Canvas
        {
            Width = image.PixelWidth,
            Height = image.PixelHeight,
            Background = Brushes.Transparent,
            ClipToBounds = true,
        };
        var picture = new Image { Source = image, Width = image.PixelWidth, Height = image.PixelHeight, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
        canvas.Children.Add(picture);
        Rebuild(canvas, annotations);
        return canvas;
    }

    public static BitmapSource Render(Canvas canvas, int outWidth, int outHeight, Rect? source = null)
    {
        var w = (int)canvas.Width;
        var h = (int)canvas.Height;
        if (canvas.Parent is null)
        {
            canvas.Measure(new Size(w, h));
            canvas.Arrange(new Rect(0, 0, w, h));
        }

        canvas.UpdateLayout();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new VisualBrush(canvas)
            {
                Viewbox = source ?? new Rect(0, 0, w, h),
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill,
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, outWidth, outHeight));
        }

        var target = new RenderTargetBitmap(outWidth, outHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
