using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace PresenterTools.Shots;

internal sealed class EditorView : UserControl
{
    enum Tool { Select, Pen, Text, Box, Line, Shape, Fill, Crop, Arrow }

    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    static readonly int[] Thicknesses = [1, 2, 3, 5, 6, 8, 12];
    static readonly int[] FontSizes = [12, 16, 20, 24, 32, 48, 64];

    readonly Canvas overlay = new() { IsHitTestVisible = false };
    readonly Canvas viewport = new() { ClipToBounds = true };
    readonly Grid host = new();
    readonly Grid area = new() { Background = SystemColors.ControlBrush, ClipToBounds = true };
    readonly ScaleTransform zoomScale = new(1, 1);
    readonly TranslateTransform zoomShift = new();
    const double MaxZoom = 16;
    ComboBox thicknessCombo = null!;
    bool panning;
    Point panStart;
    Point panOrigin;
    readonly Stack<(List<Annotation> Items, CropRect? Crop)> history = [];
    readonly Stack<(List<Annotation> Items, CropRect? Crop)> redone = [];
    CaptureEntry? entry;
    BitmapSource image = null!;
    Canvas canvas = null!;
    Grid layer = null!;
    List<Annotation> items = [];
    CropRect? crop;
    bool dirty;
    Window? hooked;
    Tool tool = Tool.Pen;
    ShapeKind shapeKind = ShapeKind.Rectangle;
    bool filled;
    double thickness = Annotations.StrokeWidth;
    double fontSize = Annotations.FontPixels;
    Color color = Annotations.InkColor;
    TextBox? editor;
    UIElement? live;
    Canvas cropLayer = null!;
    Rect cropRect;
    Rectangle[] cropHandles = [];
    (int X, int Y) cropGrip;
    bool cropDragging;
    bool cropDrawing;
    bool cropMoved;
    System.Windows.Shapes.Path? cropDim;
    Rectangle? cropDark;
    Rectangle? cropLight;
    Rectangle? selection;
    List<double>? pen;
    List<double>? polygon;
    UIElement? polygonLive;
    bool dragging;
    bool moving;
    int selected = -1;
    Point origin;

    public CaptureEntry? Entry => entry;

    public FrameworkElement ToolPanel { get; }

    public EditorView()
    {
        ToolPanel = BuildToolPanel();
        host.Children.Add(viewport);
        var fit = new TransformGroup();
        fit.Children.Add(zoomScale);
        fit.Children.Add(zoomShift);
        area.Children.Add(new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = host, RenderTransformOrigin = new Point(0, 0), RenderTransform = fit });
        area.PreviewMouseWheel += OnWheel;
        area.SizeChanged += (_, _) => ClampPan();
        Content = area;
        Loaded += (_, _) =>
        {
            hooked = Window.GetWindow(this);
            if (hooked is not null)
            {
                hooked.PreviewMouseLeftButtonDown += OnWindowDown;
                hooked.PreviewKeyDown += OnKey;
            }

            ApplyDpi(VisualTreeHelper.GetDpi(this));
        };
        Unloaded += (_, _) =>
        {
            if (hooked is not null)
            {
                hooked.PreviewMouseLeftButtonDown -= OnWindowDown;
                hooked.PreviewKeyDown -= OnKey;
                hooked = null;
            }
        };
    }

    void OnWindowDown(object sender, MouseButtonEventArgs e)
    {
        if (editor is not null && !ReferenceEquals(e.OriginalSource, editor) && !editor.IsAncestorOf(e.OriginalSource as DependencyObject))
        {
            CommitText();
        }
    }

    public void Load(CaptureEntry? next)
    {
        Finish();
        Deselect();
        viewport.Children.Clear();
        layer?.Children.Clear();
        overlay.Children.Clear();
        history.Clear();
        redone.Clear();
        live = null;
        pen = null;
        dragging = false;
        moving = false;
        cropDrawing = false;
        dirty = false;
        entry = next;
        ResetZoom();
        if (next is null)
        {
            items = [];
            crop = null;
            viewport.Width = 0;
            viewport.Height = 0;
            return;
        }

        image = LibraryStore.LoadImage(next);
        var sidecar = LibraryStore.LoadSidecar(next);
        items = sidecar.Annotations;
        crop = sidecar.Crop;
        canvas = Annotations.Compose(image, items);
        canvas.MouseLeftButtonDown += OnDown;
        canvas.MouseMove += OnMove;
        canvas.MouseLeftButtonUp += OnUp;
        layer = new Grid { Width = image.PixelWidth, Height = image.PixelHeight };
        layer.Children.Add(canvas);
        layer.Children.Add(overlay);
        BuildCropUi();
        viewport.Children.Add(layer);
        ApplyCrop();
    }

    public void Persist()
    {
        Finish();
        if (entry is null || !dirty)
        {
            return;
        }

        LibraryStore.SaveSidecar(entry, new Sidecar(items, crop));
        dirty = false;
    }

    public void SetColor(Color value) => color = value;

    StackPanel BuildToolPanel()
    {
        var panel = new StackPanel();
        var tools = new UniformGrid { Columns = 4 };
        var style = Chrome.Get("ToolChip");
        var defs = new[]
        {
            (Tool.Select, "", "Select"),
            (Tool.Pen, "", "Pen"),
            (Tool.Text, "", "Text"),
            (Tool.Box, "", "Box"),
            (Tool.Line, "", "Line"),
            (Tool.Shape, "", "Shape"),
            (Tool.Fill, "", "Fill"),
            (Tool.Crop, "", "Crop"),
        };
        foreach (var (value, glyph, name) in defs)
        {
            tools.Children.Add(ToolButton(value, glyph, name, style));
        }

        panel.Children.Add(tools);
        var quick = new UniformGrid { Columns = 4 };
        var copy = ActionButton("", "Copy image", Copy);
        copy.Click += async (_, _) =>
        {
            if (entry is null)
            {
                return;
            }

            copy.BorderBrush = Chrome.Accent;
            await System.Threading.Tasks.Task.Delay(600);
            copy.ClearValue(Control.BorderBrushProperty);
        };
        quick.Children.Add(copy);
        quick.Children.Add(ToolButton(Tool.Arrow, "", "Arrow", style));
        panel.Children.Add(quick);
        thicknessCombo = Preset(Thicknesses, (int)thickness, "Thickness", v => thickness = v);
        panel.Children.Add(Labelled("", "Thickness", thicknessCombo));
        panel.Children.Add(Labelled("", "Font size", Preset(FontSizes, (int)fontSize, "Font size", v => fontSize = v)));
        panel.Children.Add(Choice(["Rectangle", "Rounded rectangle", "Ellipse", "Polygon"], 0, "Shape kind", i => shapeKind = (ShapeKind)i));
        panel.Children.Add(Choice(["Outline", "Filled"], 0, "Shape style", i => filled = i == 1));
        var actions = new UniformGrid { Columns = 4 };
        actions.Children.Add(ActionButton("", "Undo", Undo));
        actions.Children.Add(ActionButton("", "Redo", Redo));
        actions.Children.Add(ActionButton("", "Save", Save));
        panel.Children.Add(actions);
        return panel;
    }

    RadioButton ToolButton(Tool value, string glyph, string name, Style style)
    {
        var button = new RadioButton { Content = Glyph(glyph), ToolTip = name, GroupName = "tools", Style = style, IsChecked = value == tool, Padding = new Thickness(2, 4, 2, 4) };
        button.Checked += (_, _) =>
        {
            Finish();
            tool = value;
            Deselect();
            if (value == Tool.Arrow)
            {
                thicknessCombo.SelectedItem = (int)Annotations.ArrowWidth;
            }

            if (entry is not null)
            {
                ApplyCrop();
            }
        };
        return button;
    }

    void ResetZoom()
    {
        panning = false;
        zoomScale.ScaleX = 1;
        zoomScale.ScaleY = 1;
        zoomShift.X = 0;
        zoomShift.Y = 0;
    }

    void ClampPan()
    {
        var z = zoomScale.ScaleX;
        zoomShift.X = Math.Clamp(zoomShift.X, area.ActualWidth * (1 - z), 0);
        zoomShift.Y = Math.Clamp(zoomShift.Y, area.ActualHeight * (1 - z), 0);
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (tool != Tool.Select || entry is null || editor is not null || !viewport.IsMouseOver)
        {
            return;
        }

        var z = zoomScale.ScaleX;
        var next = Math.Clamp(z * Math.Pow(1.2, e.Delta / 120.0), 1, MaxZoom);
        e.Handled = true;
        if (next == z)
        {
            return;
        }

        var m = e.GetPosition(area);
        zoomShift.X = m.X - (m.X - zoomShift.X) * next / z;
        zoomShift.Y = m.Y - (m.Y - zoomShift.Y) * next / z;
        zoomScale.ScaleX = next;
        zoomScale.ScaleY = next;
        ClampPan();
    }

    static DockPanel Labelled(string glyph, string tip, ComboBox combo)
    {
        var row = new DockPanel();
        var label = IconLabel(glyph, tip);
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(combo);
        return row;
    }

    static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = IconFont,
        FontSize = 16,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    static TextBlock IconLabel(string glyph, string tip)
    {
        var label = Glyph(glyph);
        label.ToolTip = tip;
        label.Margin = new Thickness(6, 0, 4, 0);
        label.Foreground = Brushes.LightGray;
        return label;
    }

    static ComboBox Preset(int[] values, int current, string tip, Action<int> apply)
    {
        var combo = new ComboBox { ItemsSource = values, SelectedItem = current, ToolTip = tip, Style = Chrome.Get("Combo") };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is int value)
            {
                apply(value);
            }
        };
        return combo;
    }

    static ComboBox Choice(string[] names, int current, string tip, Action<int> apply)
    {
        var combo = new ComboBox { ItemsSource = names, SelectedIndex = current, ToolTip = tip, Style = Chrome.Get("Combo") };
        combo.SelectionChanged += (_, _) => apply(combo.SelectedIndex);
        return combo;
    }

    static Button ActionButton(string glyph, string tip, Action action)
    {
        var button = new Button { Content = Glyph(glyph), ToolTip = tip, Style = Chrome.Get("Chip"), Padding = new Thickness(2, 4, 2, 4) };
        button.Click += (_, _) => action();
        return button;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyDpi(newDpi);
    }

    void ApplyDpi(DpiScale dpi) =>
        host.LayoutTransform = new ScaleTransform(1 / dpi.DpiScaleX, 1 / dpi.DpiScaleY);

    Rect Visible => crop is { } c ? new Rect(c.X, c.Y, c.W, c.H) : new Rect(0, 0, image.PixelWidth, image.PixelHeight);

    void ApplyCrop()
    {
        var r = tool == Tool.Crop ? new Rect(0, 0, image.PixelWidth, image.PixelHeight) : Visible;
        viewport.Width = r.Width;
        viewport.Height = r.Height;
        Canvas.SetLeft(layer, -r.X);
        Canvas.SetTop(layer, -r.Y);
        cropRect = Visible;
        UpdateCropUi();
    }

    const double MinCrop = 16;

    void BuildCropUi()
    {
        cropLayer = new Canvas();
        cropDim = new System.Windows.Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), IsHitTestVisible = false };
        cropDark = new Rectangle { Stroke = Brushes.Black, IsHitTestVisible = false };
        cropLight = new Rectangle { Stroke = Brushes.White, StrokeDashArray = [4, 3], IsHitTestVisible = false };
        cropLayer.Children.Add(cropDim);
        cropLayer.Children.Add(cropDark);
        cropLayer.Children.Add(cropLight);
        var handles = new List<Rectangle>();
        for (var gy = -1; gy <= 1; gy++)
        {
            for (var gx = -1; gx <= 1; gx++)
            {
                if (gx == 0 && gy == 0)
                {
                    continue;
                }

                var grip = (gx, gy);
                var handle = new Rectangle
                {
                    Fill = Brushes.White,
                    Stroke = Brushes.Black,
                    Cursor = gx == 0 ? Cursors.SizeNS : gy == 0 ? Cursors.SizeWE : gx == gy ? Cursors.SizeNWSE : Cursors.SizeNESW,
                    Tag = grip,
                };
                handle.MouseLeftButtonDown += (_, e) =>
                {
                    cropGrip = grip;
                    cropDragging = true;
                    handle.CaptureMouse();
                    e.Handled = true;
                };
                handle.MouseMove += (_, e) =>
                {
                    if (cropDragging)
                    {
                        DragCrop(e.GetPosition(cropLayer));
                    }
                };
                handle.MouseLeftButtonUp += (_, e) =>
                {
                    if (!cropDragging)
                    {
                        return;
                    }

                    cropDragging = false;
                    handle.ReleaseMouseCapture();
                    EndCrop();
                    e.Handled = true;
                };
                handles.Add(handle);
                cropLayer.Children.Add(handle);
            }
        }

        cropHandles = [.. handles];
        layer.Children.Add(cropLayer);
        SizeChanged -= OnCropResize;
        SizeChanged += OnCropResize;
    }

    void OnCropResize(object sender, SizeChangedEventArgs e) => UpdateCropUi();

    void DragCrop(Point p)
    {
        var w = image.PixelWidth;
        var h = image.PixelHeight;
        var left = cropRect.Left;
        var right = cropRect.Right;
        var top = cropRect.Top;
        var bottom = cropRect.Bottom;
        var minW = Math.Min(MinCrop, w);
        var minH = Math.Min(MinCrop, h);
        if (cropGrip.X < 0)
        {
            left = Math.Clamp(p.X, 0, right - minW);
        }
        else if (cropGrip.X > 0)
        {
            right = Math.Clamp(p.X, left + minW, w);
        }

        if (cropGrip.Y < 0)
        {
            top = Math.Clamp(p.Y, 0, bottom - minH);
        }
        else if (cropGrip.Y > 0)
        {
            bottom = Math.Clamp(p.Y, top + minH, h);
        }

        cropRect = new Rect(new Point(left, top), new Point(right, bottom));
        UpdateCropUi();
    }

    Rect DrawnCrop(Point p, bool enforceMin)
    {
        var w = image.PixelWidth;
        var h = image.PixelHeight;
        var (x0, x1) = Span(origin.X, Math.Clamp(p.X, 0, w), w, enforceMin ? Math.Min(MinCrop, w) : 0);
        var (y0, y1) = Span(origin.Y, Math.Clamp(p.Y, 0, h), h, enforceMin ? Math.Min(MinCrop, h) : 0);
        return new Rect(new Point(x0, y0), new Point(x1, y1));
    }

    static (double Low, double High) Span(double anchor, double end, double max, double min)
    {
        var low = Math.Min(anchor, end);
        var high = Math.Max(anchor, end);
        if (high - low >= min)
        {
            return (low, high);
        }

        if (end >= anchor)
        {
            high = Math.Min(max, low + min);
            low = high - min;
        }
        else
        {
            low = Math.Max(0, high - min);
            high = low + min;
        }

        return (low, high);
    }

    void EndCrop()
    {
        var x = Math.Floor(cropRect.X);
        var y = Math.Floor(cropRect.Y);
        var next = new CropRect(x, y, Math.Ceiling(cropRect.Right) - x, Math.Ceiling(cropRect.Bottom) - y);
        var full = next.X <= 0 && next.Y <= 0 && next.W >= image.PixelWidth && next.H >= image.PixelHeight;
        var target = full ? null : next;
        if (!Equals(target, crop))
        {
            Push();
            crop = target;
        }

        ApplyCrop();
    }

    double ViewScale()
    {
        if (!IsLoaded || layer is null)
        {
            return 1;
        }

        UpdateLayout();
        var m = layer.TransformToAncestor(this);
        var scale = (m.Transform(new Point(1, 0)) - m.Transform(new Point(0, 0))).Length;
        return scale > 0.0001 ? scale : 1;
    }

    void UpdateCropUi()
    {
        if (cropDim is null || cropDark is null || cropLight is null || layer is null)
        {
            return;
        }

        var show = tool == Tool.Crop && entry is not null;
        cropLayer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            return;
        }

        var w = image.PixelWidth;
        var h = image.PixelHeight;
        var px = 1 / ViewScale();
        var r = cropRect;
        cropDim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, w, h)), new RectangleGeometry(r));
        var inset = Math.Min(2 * px, Math.Min(r.Width, r.Height) / 2);
        var b = new Rect(r.X + inset / 2, r.Y + inset / 2, Math.Max(0, r.Width - inset), Math.Max(0, r.Height - inset));
        Place(cropDark, b, 3 * px);
        Place(cropLight, b, 1.5 * px);
        var size = Math.Min(14 * px, Math.Min(w, h) / 3.0);
        foreach (var handle in cropHandles)
        {
            var (gx, gy) = ((int, int))handle.Tag;
            var cx = gx < 0 ? r.Left : gx > 0 ? r.Right : r.Left + r.Width / 2;
            var cy = gy < 0 ? r.Top : gy > 0 ? r.Bottom : r.Top + r.Height / 2;
            cx = Math.Clamp(cx, size / 2, w - size / 2);
            cy = Math.Clamp(cy, size / 2, h - size / 2);
            handle.Width = size;
            handle.Height = size;
            handle.StrokeThickness = 1.5 * px;
            Canvas.SetLeft(handle, cx - size / 2);
            Canvas.SetTop(handle, cy - size / 2);
        }
    }

    static void Place(Rectangle rect, Rect b, double stroke)
    {
        rect.Width = b.Width;
        rect.Height = b.Height;
        rect.StrokeThickness = stroke;
        Canvas.SetLeft(rect, b.X);
        Canvas.SetTop(rect, b.Y);
    }

    public void HandleShortcut(KeyEventArgs e) => OnKey(this, e);

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && polygon is not null)
        {
            FinishPolygon();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z && Keyboard.FocusedElement is not TextBox)
        {
            Redo();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.S)
        {
            Save();
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.FocusedElement is not TextBox)
        {
            Copy();
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.FocusedElement is not TextBox)
        {
            Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y && Keyboard.FocusedElement is not TextBox)
        {
            Redo();
            e.Handled = true;
        }
    }

    void Push()
    {
        dirty = true;
        history.Push((items.ToList(), crop));
        redone.Clear();
    }

    void Commit(Annotation a)
    {
        Push();
        items.Add(a);
        Annotations.Append(canvas, a);
    }

    void Undo()
    {
        Finish();
        if (history.Count == 0)
        {
            return;
        }

        redone.Push((items.ToList(), crop));
        Restore(history.Pop());
    }

    void Redo()
    {
        Finish();
        if (redone.Count == 0)
        {
            return;
        }

        history.Push((items.ToList(), crop));
        Restore(redone.Pop());
    }

    void Restore((List<Annotation> Items, CropRect? Crop) state)
    {
        (items, crop) = state;
        dirty = true;
        selected = -1;
        Annotations.Rebuild(canvas, items);
        ApplyCrop();
        DrawSelection();
    }

    void Finish()
    {
        CommitText();
        FinishPolygon();
    }

    Annotation Draft(Point p)
    {
        var hex = color.ToString();
        return tool switch
        {
            Tool.Pen => new Annotation("pen", 0, 0, 0, 0, null, pen!.ToArray(), thickness, 0, hex),
            Tool.Box => new Annotation("box", origin.X, origin.Y, p.X, p.Y, null, null, thickness, 0, hex),
            Tool.Line => new Annotation("line", origin.X, origin.Y, p.X, p.Y, null, null, thickness, 0, hex),
            Tool.Arrow => new Annotation("arrow", origin.X, origin.Y, p.X, p.Y, null, null, thickness, 0, hex),
            _ => new Annotation("shape", origin.X, origin.Y, p.X, p.Y, null, null, thickness, 0, hex, shapeKind.ToString(), filled),
        };
    }

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        origin = e.GetPosition(canvas);
        switch (tool)
        {
            case Tool.Text:
                BeginText(origin);
                e.Handled = true;
                return;
            case Tool.Fill:
                Commit(new Annotation("fill", origin.X, origin.Y, 0, 0, null, Color: color.ToString()));
                return;
            case Tool.Select:
                BeginSelect();
                if (selected < 0 && zoomScale.ScaleX > 1)
                {
                    panning = true;
                    panStart = e.GetPosition(area);
                    panOrigin = new Point(zoomShift.X, zoomShift.Y);
                    canvas.CaptureMouse();
                    e.Handled = true;
                }

                return;
            case Tool.Shape when shapeKind == ShapeKind.Polygon:
                PolygonClick(origin, e.ClickCount);
                return;
            case Tool.Crop:
                origin = new Point(Math.Clamp(origin.X, 0, image.PixelWidth), Math.Clamp(origin.Y, 0, image.PixelHeight));
                cropDrawing = true;
                cropMoved = false;
                canvas.CaptureMouse();
                e.Handled = true;
                return;
            case Tool.Pen:
                pen = [origin.X, origin.Y];
                live = Annotations.Create(Draft(origin));
                canvas.Children.Add(live);
                break;
            default:
                live = Annotations.Create(Draft(origin));
                canvas.Children.Add(live);
                break;
        }

        dragging = true;
        canvas.CaptureMouse();
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (panning)
        {
            var pos = e.GetPosition(area);
            zoomShift.X = panOrigin.X + pos.X - panStart.X;
            zoomShift.Y = panOrigin.Y + pos.Y - panStart.Y;
            ClampPan();
            return;
        }

        var p = e.GetPosition(canvas);
        if (polygon is not null && !dragging)
        {
            PreviewPolygon(p);
            return;
        }

        if (cropDrawing)
        {
            if (cropMoved || Math.Abs(p.X - origin.X) >= 3 || Math.Abs(p.Y - origin.Y) >= 3)
            {
                cropMoved = true;
                cropRect = DrawnCrop(p, false);
                UpdateCropUi();
            }

            return;
        }

        if (moving)
        {
            var move = new TranslateTransform(p.X - origin.X, p.Y - origin.Y);
            canvas.Children[selected + 1].RenderTransform = move;
            if (selection is not null)
            {
                selection.RenderTransform = move;
            }

            return;
        }

        if (!dragging)
        {
            return;
        }

        if (tool == Tool.Pen)
        {
            var n = pen!.Count;
            if (Math.Abs(p.X - pen[n - 2]) + Math.Abs(p.Y - pen[n - 1]) < 1.5)
            {
                return;
            }

            pen.Add(p.X);
            pen.Add(p.Y);
        }

        var next = Annotations.Create(Draft(p));
        var at = canvas.Children.IndexOf(live);
        canvas.Children.RemoveAt(at);
        canvas.Children.Insert(at, next);
        live = next;
    }

    void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (panning)
        {
            panning = false;
            canvas.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        var p = e.GetPosition(canvas);
        if (cropDrawing)
        {
            cropDrawing = false;
            canvas.ReleaseMouseCapture();
            if (cropMoved)
            {
                cropRect = DrawnCrop(p, true);
                EndCrop();
            }
            else
            {
                ApplyCrop();
            }

            e.Handled = true;
            return;
        }

        if (moving)
        {
            EndMove(p);
            return;
        }

        if (!dragging)
        {
            return;
        }

        dragging = false;
        canvas.ReleaseMouseCapture();
        var small = Math.Abs(p.X - origin.X) < 3 && Math.Abs(p.Y - origin.Y) < 3;
        canvas.Children.Remove(live);
        live = null;
        if (tool == Tool.Pen ? pen!.Count >= 4 : !small)
        {
            Commit(Draft(p));
        }

        pen = null;
    }

    void BeginSelect()
    {
        selected = -1;
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (Annotations.Hit(items[i], canvas.Children[i + 1], origin))
            {
                selected = i;
                break;
            }
        }

        DrawSelection();
        if (selected >= 0)
        {
            moving = true;
            canvas.CaptureMouse();
        }
    }

    void EndMove(Point p)
    {
        moving = false;
        canvas.ReleaseMouseCapture();
        var dx = p.X - origin.X;
        var dy = p.Y - origin.Y;
        if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1)
        {
            canvas.Children[selected + 1].RenderTransform = Transform.Identity;
            DrawSelection();
            return;
        }

        Push();
        items[selected] = Annotations.Offset(items[selected], dx, dy);
        Annotations.Rebuild(canvas, items);
        DrawSelection();
    }

    void Deselect()
    {
        selected = -1;
        DrawSelection();
    }

    void DrawSelection()
    {
        overlay.Children.Remove(selection);
        selection = null;
        if (selected < 0 || selected >= items.Count)
        {
            return;
        }

        canvas.UpdateLayout();
        var b = Annotations.Bounds(items[selected], canvas.Children[selected + 1]);
        if (b.IsEmpty)
        {
            return;
        }

        b.Inflate(3, 3);
        selection = new Rectangle
        {
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 1.5,
            StrokeDashArray = [4, 3],
            Width = b.Width,
            Height = b.Height,
        };
        Canvas.SetLeft(selection, b.X);
        Canvas.SetTop(selection, b.Y);
        overlay.Children.Add(selection);
    }

    void PolygonClick(Point p, int clicks)
    {
        if (clicks >= 2)
        {
            FinishPolygon();
            return;
        }

        polygon ??= [];
        polygon.Add(p.X);
        polygon.Add(p.Y);
        PreviewPolygon(p);
    }

    Annotation PolygonDraft(double[] points) =>
        new("shape", 0, 0, 0, 0, null, points, thickness, 0, color.ToString(), nameof(ShapeKind.Polygon), filled);

    void PreviewPolygon(Point p)
    {
        if (polygon is null)
        {
            return;
        }

        canvas.Children.Remove(polygonLive);
        polygonLive = Annotations.Create(PolygonDraft([.. polygon, p.X, p.Y]));
        polygonLive.IsHitTestVisible = false;
        canvas.Children.Add(polygonLive);
    }

    void FinishPolygon()
    {
        if (polygon is null)
        {
            return;
        }

        var points = polygon;
        polygon = null;
        canvas.Children.Remove(polygonLive);
        polygonLive = null;
        if (points.Count >= 6)
        {
            Commit(PolygonDraft(points.ToArray()));
        }
    }

    void BeginText(Point at)
    {
        CommitText();
        var ink = Annotations.BrushOf(color);
        var box = new TextBox
        {
            Foreground = ink,
            Background = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FontSize = fontSize,
            FontFamily = Annotations.Font,
            MinWidth = 60,
            CaretBrush = ink,
            Tag = color,
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitText();
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => CommitText();
        Canvas.SetLeft(box, at.X);
        Canvas.SetTop(box, at.Y);
        canvas.Children.Add(box);
        editor = box;
        Dispatcher.BeginInvoke(() => box.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    void CommitText()
    {
        if (editor is not { } box)
        {
            return;
        }

        editor = null;
        var text = box.Text;
        var left = Canvas.GetLeft(box);
        var top = Canvas.GetTop(box);
        canvas.Children.Remove(box);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Commit(new Annotation("text", left, top, left, top, text, null, 0, box.FontSize, ((Color)box.Tag).ToString()));
    }

    BitmapSource Flatten()
    {
        Finish();
        var r = Visible;
        return Annotations.Render(canvas, (int)r.Width, (int)r.Height, crop is null ? null : r);
    }

    void Copy()
    {
        if (entry is not null)
        {
            Clipboard.SetImage(Flatten());
        }
    }

    void Save()
    {
        if (entry is null)
        {
            return;
        }

        var flat = Flatten();
        var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            InitialDirectory = LibraryStore.Root,
            FileName = $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(flat));
        using var stream = File.Create(dialog.FileName);
        encoder.Save(stream);
    }
}
