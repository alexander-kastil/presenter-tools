using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace PresenterTools;

public enum Mode { Mouse, Pen, Text, Eraser }

public sealed record OptimizeStatus(string Label, string Tooltip);

public sealed class Session
{
    private readonly Stack<Action> _undo = new();
    private readonly List<(OverlayWindow Overlay, Stroke Stroke)> _group = [];
    private int _groupVersion;

    public List<OverlayWindow> Overlays { get; } = [];
    public ToolbarWindow? Toolbar { get; set; }
    public Mode Mode { get; private set; } = Mode.Mouse;
    public Color Color { get; private set; } = Colors.Red;
    public double PenWidth { get; private set; } = 4;
    public event Action? Changed;

    public void SetMode(Mode mode)
    {
        CommitText();
        if (Mode != mode)
        {
            ResetGroup();
        }
        Mode = mode;
        foreach (var overlay in Overlays)
        {
            overlay.ApplyMode(mode);
        }
        Changed?.Invoke();
    }

    public void SetColor(Color color)
    {
        Color = color;
        foreach (var overlay in Overlays)
        {
            overlay.Ink.DefaultDrawingAttributes.Color = color;
        }
        Changed?.Invoke();
    }

    public void SetPenWidth(double width)
    {
        PenWidth = width;
        foreach (var overlay in Overlays)
        {
            overlay.Ink.DefaultDrawingAttributes.Width = width;
            overlay.Ink.DefaultDrawingAttributes.Height = width;
        }
        Changed?.Invoke();
    }

    public void CommitText()
    {
        foreach (var overlay in Overlays)
        {
            overlay.CommitText();
        }
    }

    public void NoteStrokes(OverlayWindow overlay, IEnumerable<Stroke> strokes)
    {
        foreach (var stroke in strokes)
        {
            _group.Add((overlay, stroke));
        }
    }

    public void ResetGroup()
    {
        _group.Clear();
        _groupVersion++;
    }

    public async Task<OptimizeStatus?> OptimizeAsync()
    {
        var present = _group.Where(g => g.Overlay.Ink.Strokes.Contains(g.Stroke)).ToList();
        if (present.Count == 0)
        {
            return new OptimizeStatus("Nothing to optimize", "Nothing to optimize: draw with the Pen first");
        }
        var overlay = present[^1].Overlay;
        var strokes = present.Where(g => g.Overlay == overlay).Select(g => g.Stroke).ToList();

        var apiKey = AppSettings.ResolveApiKey();
        if (apiKey is null)
        {
            SettingsWindow.Open(null);
            apiKey = AppSettings.ResolveApiKey();
            if (apiKey is null)
            {
                return new OptimizeStatus("No API key", "No API key: set it in Settings");
            }
        }
        var settings = AppSettings.LoadProvider();

        var collection = new StrokeCollection(strokes);
        var bounds = collection.GetBounds();
        var attributes = strokes[^1].DrawingAttributes;
        var color = attributes.Color;
        var width = attributes.Width;
        var (png, box) = StrokeRenderer.Render(collection);
        var version = _groupVersion;

        AiResult? result;
        try
        {
            result = await AiClient.RecogniseAsync(png, settings, apiKey);
        }
        catch (TaskCanceledException)
        {
            return new OptimizeStatus("Timed out", "The request timed out after 60 seconds");
        }
        catch (Exception ex)
        {
            return new OptimizeStatus("Error", ex.Message);
        }

        if (result is null || result.Kind == AiResult.UnknownKind)
        {
            return new OptimizeStatus("Not recognized", "Not recognized as text or a drawing");
        }
        if (version != _groupVersion || strokes.Any(s => !overlay.Ink.Strokes.Contains(s)))
        {
            return new OptimizeStatus("Drawing changed", "The drawing changed while optimizing");
        }

        if (result.Kind == AiResult.TextKind)
        {
            overlay.Replace(strokes, [], CreateText(result.Text!, color, bounds));
        }
        else
        {
            var attrs = new DrawingAttributes
            {
                Color = color,
                Width = width,
                Height = width,
                IgnorePressure = true,
                FitToCurve = false
            };
            var drawn = ShapeMapper.ToPaths(result.Shapes, box)
                .Select(path => new Stroke(new StylusPointCollection(path.Select(p => new StylusPoint(p.X, p.Y))), attrs.Clone()))
                .ToList();
            if (drawn.Count == 0)
            {
                return new OptimizeStatus("Not recognized", "Not recognized as text or a drawing");
            }
            overlay.Replace(strokes, drawn, null);
        }
        ResetGroup();
        return null;
    }

    private static TextBlock CreateText(string text, Color color, Rect bounds)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(color),
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 100,
            IsHitTestVisible = false
        };
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = block.DesiredSize.Height > 0 ? bounds.Height / block.DesiredSize.Height * 100 : 12;
        block.FontSize = Math.Clamp(size, 12, 200);
        InkCanvas.SetLeft(block, bounds.X);
        InkCanvas.SetTop(block, bounds.Y);
        return block;
    }

    public void Push(Action undo) => _undo.Push(undo);

    public void Undo()
    {
        CommitText();
        ResetGroup();
        if (_undo.Count > 0)
        {
            _undo.Pop()();
        }
    }

    public void ClearAll()
    {
        ResetGroup();
        var restores = Overlays.Select(o => o.Clear()).OfType<Action>().ToList();
        if (restores.Count > 0)
        {
            Push(() => restores.ForEach(restore => restore()));
        }
    }

    public Action HideForCapture()
    {
        var windows = new List<Window>(Overlays);
        if (Toolbar is not null)
        {
            windows.Add(Toolbar);
        }

        var visible = windows.Where(w => w.IsVisible).ToList();
        foreach (var window in visible)
        {
            window.Hide();
        }

        return () =>
        {
            foreach (var window in visible)
            {
                var activated = window.ShowActivated;
                window.ShowActivated = false;
                window.Show();
                window.ShowActivated = activated;
            }

            RaiseToolbar();
        };
    }

    public void RaiseToolbar()
    {
        if (Toolbar is null)
        {
            return;
        }
        Toolbar.Topmost = false;
        Toolbar.Topmost = true;
    }
}
