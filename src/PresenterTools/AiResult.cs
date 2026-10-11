using System.Text.Json;
using System.Windows;

namespace PresenterTools;

public sealed record Shape(string Type)
{
    public List<Point> Points { get; init; } = [];
    public bool Closed { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double Cx { get; init; }
    public double Cy { get; init; }
    public double Rx { get; init; }
    public double Ry { get; init; }
}

public sealed record AiResult(string Kind, string? Text, string? Label, List<Shape> Shapes)
{
    public const string TextKind = "text";
    public const string DrawingKind = "drawing";
    public const string UnknownKind = "unknown";

    public static AiResult Unknown { get; } = new(UnknownKind, null, null, []);
}

public static class ResponseParser
{
    public static AiResult? ParseResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content");
            return content.ValueKind == JsonValueKind.String ? ParseContent(content.GetString()!) : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    public static AiResult? ParseContent(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(content[start..(end + 1)],
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            switch (Str(root, "kind")?.Trim().ToLowerInvariant())
            {
                case AiResult.TextKind:
                    var text = Str(root, "text");
                    return string.IsNullOrWhiteSpace(text) ? AiResult.Unknown : new AiResult(AiResult.TextKind, text, null, []);
                case AiResult.DrawingKind:
                    var shapes = new List<Shape>();
                    if (root.TryGetProperty("shapes", out var array) && array.ValueKind == JsonValueKind.Array)
                    {
                        shapes.AddRange(array.EnumerateArray().Select(ParseShape).OfType<Shape>());
                    }
                    return shapes.Count == 0 ? AiResult.Unknown : new AiResult(AiResult.DrawingKind, null, Str(root, "label"), Normalize(shapes));
                default:
                    return AiResult.Unknown;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Shape? ParseShape(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var type = Str(e, "type")?.Trim().ToLowerInvariant();
        switch (type)
        {
            case "line":
            case "arrow":
                var ends = Points(e);
                return ends.Count >= 2 ? new Shape(type) { Points = ends } : null;
            case "polyline":
                var points = Points(e);
                return points.Count >= 2 ? new Shape(type) { Points = points, Closed = Bool(e, "closed") } : null;
            case "rectangle":
                double? x = Num(e, "x"), y = Num(e, "y"), w = Num(e, "width"), h = Num(e, "height");
                return x is not null && y is not null && w > 0 && h > 0
                    ? new Shape(type) { X = x.Value, Y = y.Value, Width = w.Value, Height = h.Value }
                    : null;
            case "ellipse":
                double? cx = Num(e, "cx"), cy = Num(e, "cy"), rx = Num(e, "rx"), ry = Num(e, "ry");
                return cx is not null && cy is not null && rx > 0 && ry > 0
                    ? new Shape(type) { Cx = cx.Value, Cy = cy.Value, Rx = rx.Value, Ry = ry.Value }
                    : null;
            default:
                return null;
        }
    }

    private static List<Point> Points(JsonElement e)
    {
        var flat = new List<double>();
        if (e.TryGetProperty("points", out var array))
        {
            Flatten(array, flat);
        }
        var pairs = new List<Point>();
        for (var i = 0; i + 1 < flat.Count; i += 2)
        {
            pairs.Add(new Point(flat[i], flat[i + 1]));
        }
        return pairs;
    }

    // The model nests points to any depth ([x,y,...], [[x,y],...], [[[x,y],...]]); read them in order.
    private static void Flatten(JsonElement e, List<double> into)
    {
        if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d))
        {
            into.Add(d);
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray())
            {
                Flatten(item, into);
            }
        }
    }

    // The prompt asks for a 0..1000 grid (Qwen-VL's native scale); a reply in 0..1 is accepted too.
    private static List<Shape> Normalize(List<Shape> shapes)
    {
        var max = shapes.SelectMany(s => s.Points.SelectMany(p => new[] { p.X, p.Y })
                .Concat([s.X + s.Width, s.Y + s.Height, s.Cx + s.Rx, s.Cy + s.Ry]))
            .DefaultIfEmpty(0).Max();
        if (max <= 1.5)
        {
            return shapes;
        }
        const double scale = 1000;
        return shapes.Select(s => s with
        {
            Points = s.Points.Select(p => new Point(p.X / scale, p.Y / scale)).ToList(),
            X = s.X / scale, Y = s.Y / scale, Width = s.Width / scale, Height = s.Height / scale,
            Cx = s.Cx / scale, Cy = s.Cy / scale, Rx = s.Rx / scale, Ry = s.Ry / scale
        }).ToList();
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
