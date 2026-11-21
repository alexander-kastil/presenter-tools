using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PresenterTools;

public sealed record ScriptRequest(string Cmd)
{
    public string? Shape { get; init; }
    public double[]? Rect { get; init; }
    public double[]? At { get; init; }
    public string? Text { get; init; }
    public string? Color { get; init; }
    public double? Width { get; init; }
    public double? Size { get; init; }
    public string? Label { get; init; }
    public string? Out { get; init; }
    public string? Target { get; init; }
}

// `PresenterTools.exe --cmd <command> --<option> <value> ...`: coordinates are physical screen pixels,
// widths and sizes are device-independent pixels like the pen bar.
public static class ScriptCommand
{
    public const string Flag = "--cmd";
    public const string Ok = "ok";
    public const string MirrorTarget = "mirror";
    private const double DefaultSize = 28;

    private static readonly string[] Options = ["--cmd", "--shape", "--rect", "--at", "--text", "--color", "--width", "--size", "--label", "--out", "--target"];
    private static readonly string[] Commands = ["draw", "write", "clear", "capture", "mirror"];
    private static readonly string[] Shapes = ["rectangle", "ellipse", "arrow", "line"];

    public static bool IsOk(string reply) => reply == Ok || reply.StartsWith(Ok + " ", StringComparison.Ordinal);

    public static string Run(Session session, Func<Mirror?> mirror, IReadOnlyList<string> args, string folder)
    {
        var (request, error) = Parse(args, folder);
        return request is null ? $"error={error}" : Execute(session, mirror, request);
    }

    public static (ScriptRequest? Request, string? Error) Parse(IReadOnlyList<string> args, string folder)
    {
        var values = new Dictionary<string, string>();
        for (var i = 0; i < args.Count; i += 2)
        {
            if (!Options.Contains(args[i]))
            {
                return (null, $"unknown option {args[i]}");
            }
            if (i + 1 == args.Count)
            {
                return (null, $"missing value for {args[i]}");
            }
            values[args[i][2..]] = args[i + 1];
        }

        var cmd = values.GetValueOrDefault("cmd");
        if (cmd is null || !Commands.Contains(cmd))
        {
            return (null, cmd is null ? "missing --cmd" : $"unknown command {cmd}");
        }
        if (!TryNumbers(values.GetValueOrDefault("rect"), 4, out var rect))
        {
            return (null, "bad --rect");
        }
        if (!TryNumbers(values.GetValueOrDefault("at"), 2, out var at))
        {
            return (null, "bad --at");
        }
        if (!TryNumbers(values.GetValueOrDefault("width"), 1, out var width) || width is [<= 0])
        {
            return (null, "bad --width");
        }
        if (!TryNumbers(values.GetValueOrDefault("size"), 1, out var size) || size is [<= 0])
        {
            return (null, "bad --size");
        }
        var color = values.GetValueOrDefault("color");
        if (color is not null && ColorOf(color) is null)
        {
            return (null, "bad --color");
        }

        var shape = values.GetValueOrDefault("shape");
        var text = values.GetValueOrDefault("text");
        var output = values.GetValueOrDefault("out");
        var target = values.GetValueOrDefault("target");
        var missing = cmd switch
        {
            "draw" when shape is null => "missing --shape",
            "draw" when !Shapes.Contains(shape) => $"unknown shape {shape}",
            "draw" when rect is null => "missing --rect",
            "draw" when rect is [_, _, 0, 0] => "empty --rect",
            "write" when string.IsNullOrEmpty(text) => "missing --text",
            "write" when at is null => "missing --at",
            "capture" when string.IsNullOrEmpty(output) => "missing --out",
            "capture" when rect is [_, _, <= 0, _] or [_, _, _, <= 0] => "empty --rect",
            "capture" when target is not null && target != MirrorTarget => $"unknown target {target}",
            "capture" when target is not null && rect is not null => "use --rect or --target, not both",
            _ => null
        };
        if (missing is not null)
        {
            return (null, missing);
        }

        return (new ScriptRequest(cmd)
        {
            Shape = shape,
            Rect = rect,
            At = at,
            Text = text,
            Color = color,
            Width = width?[0],
            Size = size?[0],
            Label = values.GetValueOrDefault("label"),
            Out = string.IsNullOrEmpty(output) ? null : Path.GetFullPath(output, folder),
            Target = target
        }, null);
    }

    private static bool TryNumbers(string? value, int count, out double[]? numbers)
    {
        numbers = null;
        if (value is null)
        {
            return true;
        }
        var parsed = value.Split(',', StringSplitOptions.TrimEntries)
            .Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : (double?)null)
            .ToList();
        if (parsed.Count != count || parsed.Any(n => n is null))
        {
            return false;
        }
        numbers = parsed.Select(n => n!.Value).ToArray();
        return true;
    }

    private static Color? ColorOf(string value)
    {
        try
        {
            return ColorConverter.ConvertFromString(value) as Color?;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string Execute(Session session, Func<Mirror?> mirror, ScriptRequest request) => request.Cmd switch
    {
        "draw" => Draw(session, request),
        "write" => Write(session, request),
        "clear" => Clear(session),
        "capture" when request.Target == MirrorTarget => mirror() is { } m ? Capture(request.Out!, [m.Left, m.Top, m.Width, m.Height]) : "error=not mirrored",
        "capture" => Capture(request.Out!, request.Rect),
        "mirror" => mirror() is { } m ? MirrorLine(m) : "error=not mirrored",
        _ => $"error=unknown command {request.Cmd}"
    };

    private static string Draw(Session session, ScriptRequest request)
    {
        var r = request.Rect!;
        var overlay = OverlayAt(session, r[0] + r[2] / 2, r[1] + r[3] / 2);
        if (overlay is null)
        {
            return "error=off screen";
        }
        var tail = overlay.PointFromScreen(new Point(r[0], r[1]));
        var head = overlay.PointFromScreen(new Point(r[0] + r[2], r[1] + r[3]));
        var box = new Rect(tail, head);
        var color = request.Color is { } name ? ColorOf(name)!.Value : session.Color;
        var width = request.Width ?? session.PenWidth;
        var attributes = new DrawingAttributes
        {
            Color = color,
            Width = width,
            Height = width,
            IgnorePressure = true,
            FitToCurve = false
        };
        var strokes = ShapeMapper.ToPaths([ShapeOf(request.Shape!, tail, head, box)], box)
            .Select(path => new Stroke(new StylusPointCollection(path.Select(p => new StylusPoint(p.X, p.Y))), attributes.Clone()))
            .ToList();
        var label = request.Label is { } text ? LabelOf(text, color, request.Size ?? DefaultSize, box, width) : null;
        overlay.Replace([], strokes, label);
        return Ok;
    }

    private static Shape ShapeOf(string type, Point tail, Point head, Rect box)
    {
        Point Normal(Point p) => new(
            box.Width > 0 ? (p.X - box.X) / box.Width : 0,
            box.Height > 0 ? (p.Y - box.Y) / box.Height : 0);

        return type switch
        {
            "rectangle" => new Shape(type) { Width = 1, Height = 1 },
            "ellipse" => new Shape(type) { Cx = 0.5, Cy = 0.5, Rx = 0.5, Ry = 0.5 },
            _ => new Shape(type) { Points = [Normal(tail), Normal(head)] }
        };
    }

    private static TextBlock LabelOf(string text, Color color, double size, Rect box, double width)
    {
        var block = TextOf(text, color, size);
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var above = box.Top - width / 2 - block.DesiredSize.Height;
        InkCanvas.SetLeft(block, box.Left);
        InkCanvas.SetTop(block, above >= 0 ? above : box.Bottom + width / 2);
        return block;
    }

    private static string Write(Session session, ScriptRequest request)
    {
        var at = request.At!;
        var overlay = OverlayAt(session, at[0], at[1]);
        if (overlay is null)
        {
            return "error=off screen";
        }
        var point = overlay.PointFromScreen(new Point(at[0], at[1]));
        var color = request.Color is { } name ? ColorOf(name)!.Value : session.Color;
        var block = TextOf(request.Text!, color, request.Size ?? DefaultSize);
        InkCanvas.SetLeft(block, point.X);
        InkCanvas.SetTop(block, point.Y);
        overlay.Replace([], [], block);
        return Ok;
    }

    private static TextBlock TextOf(string text, Color color, double size) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(color),
        FontFamily = new FontFamily("Segoe UI"),
        FontWeight = FontWeights.SemiBold,
        FontSize = size,
        IsHitTestVisible = false
    };

    private static string Clear(Session session)
    {
        session.ClearAll();
        return Ok;
    }

    internal static string MirrorLine(Mirror m) => $"{Ok} device={m.Device} size={m.Width}x{m.Height} rect={m.Left},{m.Top},{m.Width},{m.Height} name={m.Name}";

    internal static byte[]? Png(double[]? rect)
    {
        var (shot, x, y) = Shots.ScreenGrabber.Grab();
        BitmapSource image = shot;
        if (rect is { } r)
        {
            var crop = new Int32Rect((int)Math.Round(r[0]) - x, (int)Math.Round(r[1]) - y, (int)Math.Round(r[2]), (int)Math.Round(r[3]));
            if (crop.X < 0 || crop.Y < 0 || crop.X + crop.Width > shot.PixelWidth || crop.Y + crop.Height > shot.PixelHeight)
            {
                return null;
            }
            image = new CroppedBitmap(shot, crop);
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string Capture(string file, double[]? rect)
    {
        if (Png(rect) is not { } bytes)
        {
            return "error=off screen";
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes);
        return Ok;
    }

    private static OverlayWindow? OverlayAt(Session session, double x, double y) =>
        session.Overlays.FirstOrDefault(o => x >= o.Bounds.Left && x < o.Bounds.Right && y >= o.Bounds.Top && y < o.Bounds.Bottom);
}
