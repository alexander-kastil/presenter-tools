using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PresenterTools;

public partial class OverlayWindow : Window
{
    private const double FontSizeDip = 28;

    private readonly Session _session;
    private readonly NativeMethods.RECT _bounds;
    private TextBox? _box;
    private List<Action>? _eraseBatch;
    private bool _silent;

    public OverlayWindow(Session session, NativeMethods.RECT bounds)
    {
        InitializeComponent();
        _session = session;
        _bounds = bounds;

        Left = bounds.Left;
        Top = bounds.Top;

        Canvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = session.Color,
            Width = session.PenWidth,
            Height = session.PenWidth,
            IgnorePressure = true,
            FitToCurve = true
        };

        SourceInitialized += (_, _) => Place();
        Activated += (_, _) => _session.RaiseToolbar();
        Canvas.MouseLeftButtonDown += OnCanvasMouseDown;
        Canvas.PreviewMouseLeftButtonDown += OnPreviewDown;
        Canvas.PreviewMouseMove += OnEraseAt;
        Canvas.Strokes.StrokesChanged += OnStrokesChanged;
    }

    public InkCanvas Ink => Canvas;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(Place);
    }

    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, _bounds.Left, _bounds.Top,
            _bounds.Right - _bounds.Left, _bounds.Bottom - _bounds.Top, NativeMethods.SWP_NOACTIVATE);
    }

    public void ApplyMode(Mode mode)
    {
        Canvas.EditingMode = mode switch
        {
            Mode.Pen => InkCanvasEditingMode.Ink,
            Mode.Eraser => InkCanvasEditingMode.EraseByStroke,
            _ => InkCanvasEditingMode.None
        };
        NativeMethods.SetClickThrough(new WindowInteropHelper(this).Handle, mode == Mode.Mouse);
    }

    public Action? Clear()
    {
        _box = null;
        var strokes = Canvas.Strokes.ToList();
        var items = Canvas.Children.Cast<UIElement>().ToList();
        if (strokes.Count == 0 && items.Count == 0)
        {
            return null;
        }
        _silent = true;
        Canvas.Strokes.Clear();
        _silent = false;
        Canvas.Children.Clear();
        return () =>
        {
            _silent = true;
            Canvas.Strokes.Add(new StrokeCollection(strokes));
            _silent = false;
            foreach (var item in items)
            {
                Canvas.Children.Add(item);
            }
        };
    }

    private void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (_silent)
        {
            return;
        }
        if (e.Added.Count > 0)
        {
            var added = e.Added.ToList();
            if (_session.Mode == Mode.Pen)
            {
                _session.NoteStrokes(this, added);
            }
            _session.Push(() =>
            {
                _silent = true;
                foreach (var stroke in added)
                {
                    Canvas.Strokes.Remove(stroke);
                }
                _silent = false;
            });
        }
        if (e.Removed.Count > 0)
        {
            var removed = e.Removed.ToList();
            RecordErase(() =>
            {
                _silent = true;
                Canvas.Strokes.Add(new StrokeCollection(removed));
                _silent = false;
            });
        }
    }

    private void RecordErase(Action restore)
    {
        if (_eraseBatch is null)
        {
            var batch = new List<Action>();
            _eraseBatch = batch;
            _session.Push(() => batch.ForEach(r => r()));
        }
        _eraseBatch.Add(restore);
    }

    private void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        _eraseBatch = null;
        OnEraseAt(sender, e);
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_session.Mode != Mode.Text)
        {
            return;
        }
        if (_box is not null && e.OriginalSource is DependencyObject source && IsInside(_box, source))
        {
            return;
        }
        var point = e.GetPosition(Canvas);
        _session.CommitText();
        BeginText(point);
    }

    private static bool IsInside(DependencyObject root, DependencyObject node)
    {
        for (DependencyObject? current = node; current is not null; current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current))
        {
            if (current == root)
            {
                return true;
            }
        }
        return false;
    }

    private void BeginText(Point point)
    {
        var brush = new SolidColorBrush(_session.Color);
        var box = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Foreground = brush,
            CaretBrush = brush,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = FontSizeDip,
            FontWeight = FontWeights.SemiBold,
            MinWidth = 40
        };
        InkCanvas.SetLeft(box, point.X);
        InkCanvas.SetTop(box, point.Y);
        box.KeyDown += OnBoxKeyDown;
        box.LostKeyboardFocus += (_, _) => CommitText();
        _box = box;
        Canvas.Children.Add(box);
        Dispatcher.BeginInvoke(() =>
        {
            Activate();
            Keyboard.Focus(box);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitText();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            var box = _box;
            _box = null;
            if (box is not null)
            {
                Canvas.Children.Remove(box);
            }
        }
    }

    public void CommitText()
    {
        var box = _box;
        if (box is null)
        {
            return;
        }
        _box = null;
        var index = Canvas.Children.IndexOf(box);
        if (index < 0)
        {
            return;
        }
        Canvas.Children.RemoveAt(index);
        if (string.IsNullOrWhiteSpace(box.Text))
        {
            return;
        }
        var text = new TextBlock
        {
            Text = box.Text,
            Foreground = box.Foreground,
            FontFamily = box.FontFamily,
            FontSize = box.FontSize,
            FontWeight = box.FontWeight,
            IsHitTestVisible = false
        };
        InkCanvas.SetLeft(text, InkCanvas.GetLeft(box));
        InkCanvas.SetTop(text, InkCanvas.GetTop(box));
        Canvas.Children.Add(text);
        _session.Push(() => Canvas.Children.Remove(text));
        _session.ResetGroup();
    }

    public void Replace(IReadOnlyList<Stroke> old, IReadOnlyList<Stroke> added, TextBlock? text)
    {
        _silent = true;
        Canvas.Strokes.Remove(new StrokeCollection(old));
        Canvas.Strokes.Add(new StrokeCollection(added));
        _silent = false;
        if (text is not null)
        {
            Canvas.Children.Add(text);
        }
        _session.Push(() =>
        {
            _silent = true;
            Canvas.Strokes.Remove(new StrokeCollection(added));
            Canvas.Strokes.Add(new StrokeCollection(old));
            _silent = false;
            if (text is not null)
            {
                Canvas.Children.Remove(text);
            }
        });
    }

    private void OnEraseAt(object sender, MouseEventArgs e)
    {
        if (_session.Mode != Mode.Eraser || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var point = e.GetPosition(Canvas);
        foreach (var text in Canvas.Children.OfType<TextBlock>().ToList())
        {
            var bounds = new Rect(InkCanvas.GetLeft(text), InkCanvas.GetTop(text), text.ActualWidth, text.ActualHeight);
            if (bounds.Contains(point))
            {
                Canvas.Children.Remove(text);
                RecordErase(() => Canvas.Children.Add(text));
            }
        }
    }
}
