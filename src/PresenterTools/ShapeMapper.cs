using System.Windows;

namespace PresenterTools;

public static class ShapeMapper
{
    private const int EllipseSegments = 64;
    private const double HeadRatio = 0.15;
    private const double HeadAngle = 25 * Math.PI / 180;
    private const double Low = -0.5;
    private const double High = 1.5;

    public static List<List<Point>> ToPaths(IEnumerable<Shape> shapes, Rect box) =>
        shapes.Select(shape => ToPath(shape, box)).Where(path => path.Count >= 2).ToList();

    private static List<Point> ToPath(Shape shape, Rect box)
    {
        Point Map(double nx, double ny) => new(
            box.X + Math.Clamp(nx, Low, High) * box.Width,
            box.Y + Math.Clamp(ny, Low, High) * box.Height);

        switch (shape.Type)
        {
            case "line":
                return [Map(shape.Points[0].X, shape.Points[0].Y), Map(shape.Points[^1].X, shape.Points[^1].Y)];
            case "arrow":
                return Arrow(Map(shape.Points[0].X, shape.Points[0].Y), Map(shape.Points[^1].X, shape.Points[^1].Y));
            case "polyline":
                var path = shape.Points.Select(p => Map(p.X, p.Y)).ToList();
                if (shape.Closed)
                {
                    path.Add(path[0]);
                }
                return path;
            case "rectangle":
                return
                [
                    Map(shape.X, shape.Y),
                    Map(shape.X + shape.Width, shape.Y),
                    Map(shape.X + shape.Width, shape.Y + shape.Height),
                    Map(shape.X, shape.Y + shape.Height),
                    Map(shape.X, shape.Y)
                ];
            case "ellipse":
                return Enumerable.Range(0, EllipseSegments + 1)
                    .Select(i => 2 * Math.PI * i / EllipseSegments)
                    .Select(t => Map(shape.Cx + shape.Rx * Math.Cos(t), shape.Cy + shape.Ry * Math.Sin(t)))
                    .ToList();
            default:
                return [];
        }
    }

    private static List<Point> Arrow(Point tail, Point head)
    {
        var back = tail - head;
        if (back.Length < 1e-6)
        {
            return [];
        }
        var length = back.Length * HeadRatio;
        back.Normalize();
        return [tail, head, head + Rotate(back, HeadAngle) * length, head, head + Rotate(back, -HeadAngle) * length];
    }

    private static Vector Rotate(Vector v, double angle) =>
        new(v.X * Math.Cos(angle) - v.Y * Math.Sin(angle), v.X * Math.Sin(angle) + v.Y * Math.Cos(angle));
}
