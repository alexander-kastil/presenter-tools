using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PresenterTools;

public partial class ToolbarWindow : Window
{
    private static readonly (Color Color, string Name)[] Palette =
    [
        (Colors.Black, "Black"), (Colors.White, "White"), (Colors.Red, "Red"), (Colors.LimeGreen, "Green"),
        (Colors.DodgerBlue, "Blue"), (Colors.Yellow, "Yellow"), (Colors.DarkOrange, "Orange")
    ];

    private static readonly Brush Active = new SolidColorBrush(Color.FromRgb(0x00, 0x99, 0xFF));
    private static readonly Brush ActiveFill = new SolidColorBrush(Color.FromRgb(0x0B, 0x3A, 0x5E));
    private static readonly Brush IdleFill = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
    private static readonly Brush IdleRing = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));

    private readonly Session _session;
    private readonly Receiver _receiver;
    private readonly StatusWindow _status;
    private int _statusSequence;
    private static readonly double[] PenWidths = [2, 4, 8, 14];

    private readonly Dictionary<Color, Button> _swatches = [];
    private readonly Dictionary<double, Button> _penSizes = [];
    private readonly Button _customSwatch;
    private Color? _customColor;
    private FrameworkElement? _libraryPanel;
    private FrameworkElement? _dockHost;
    private Shots.Native.RECT? _standalone;
    private Window? _dockOwner;

    public ToolbarWindow(Session session, Receiver receiver, StatusWindow status)
    {
        InitializeComponent();
        _session = session;
        _receiver = receiver;
        _status = status;

        foreach (var (color, name) in Palette)
        {
            var button = new Button
            {
                Style = (Style)FindResource("Swatch"),
                Background = new SolidColorBrush(color),
                ToolTip = name
            };
            button.Click += (_, _) => _session.SetColor(color);
            _swatches[color] = button;
            Swatches.Children.Add(button);
        }

        _customSwatch = new Button
        {
            Style = (Style)FindResource("Swatch"),
            Background = new LinearGradientBrush(Colors.Red, Colors.DodgerBlue, 45),
            ToolTip = "Custom colour",
            Content = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = Brushes.White
            }
        };
        _customSwatch.Click += OnCustomColor;
        Swatches.Children.Add(_customSwatch);

        foreach (var width in PenWidths)
        {
            var button = new Button
            {
                Style = (Style)FindResource("Chip"),
                Height = 30,
                Padding = new Thickness(2),
                ToolTip = $"Pen width {width}",
                Content = new System.Windows.Shapes.Ellipse { Width = width, Height = width, Fill = Brushes.White }
            };
            button.Click += (_, _) => _session.SetPenWidth(width);
            _penSizes[width] = button;
            PenSizes.Children.Add(button);
        }

        PreviewKeyDown += (_, args) =>
        {
            if (Shots.LibraryWindow.IsOpen && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                Shots.LibraryWindow.HandleShortcut(args);
            }
        };
        _session.Changed += Refresh;
        _receiver.Changed += (_, _) => Refresh();
        Shots.LibraryWindow.OpenChanged += SyncLibrary;
        Closed += (_, _) => Shots.LibraryWindow.OpenChanged -= SyncLibrary;
        Loaded += (_, _) =>
        {
            if (!RestorePosition())
            {
                Left = SystemParameters.WorkArea.Left + 10;
                Top = SystemParameters.WorkArea.Top + 10;
            }

            Shots.LibraryWindow.ReservedWidth = ActualWidth;
        };
        Closing += (_, _) => SaveCurrentPosition();
        Refresh();
    }

    private void OnCustomColor(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(_session.Color.R, _session.Color.G, _session.Color.B)
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        var picked = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
        _customColor = picked;
        _customSwatch.Background = new SolidColorBrush(picked);
        _customSwatch.ToolTip = picked.ToString();
        _session.SetColor(picked);
        Refresh();
    }

    private void SyncLibrary()
    {
        var open = Shots.LibraryWindow.IsOpen;
        LibraryButton.Content = open ? "Close Library" : "Show Library";
        LibraryTools.Children.Remove(_libraryPanel);
        _libraryPanel = open ? Shots.LibraryWindow.ToolPanel : null;
        if (_libraryPanel is not null)
        {
            LibraryTools.Children.Add(_libraryPanel);
        }

        LibraryTools.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open && !IsVisible)
        {
            ShowActivated = false;
            Show();
            ShowActivated = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Dock);
        }
        else if (open)
        {
            Dock();
        }
        else
        {
            Undock();
        }

        Refresh();
    }

    private IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    private void Dock()
    {
        if (_dockHost is not null || Shots.LibraryWindow.DockHost is not { } host)
        {
            return;
        }

        Shots.Native.GetWindowRect(Handle, out var rect);
        _standalone = rect;
        _dockHost = host;
        host.LayoutUpdated += OnDockLayout;
        _dockOwner = GetWindow(host);
        if (_dockOwner is not null)
        {
            _dockOwner.LocationChanged += OnOwnerMoved;
            _dockOwner.SizeChanged += OnOwnerSized;
            _dockOwner.StateChanged += OnOwnerState;
            _dockOwner.DpiChanged += OnOwnerDpi;
        }

        DpiChanged += OnOwnDpi;
        OnDockLayout(host, EventArgs.Empty);
    }

    private void OnOwnerMoved(object? sender, EventArgs e) => OnDockLayout(sender, e);

    private bool OwnerMinimized => _dockOwner is { WindowState: WindowState.Minimized };

    private void OnOwnerState(object? sender, EventArgs e)
    {
        if (OwnerMinimized)
        {
            LibraryTools.Visibility = Visibility.Collapsed;
            if (!RestorePosition())
            {
                MoveToStandalone();
            }
        }
        else
        {
            LibraryTools.Visibility = Visibility.Visible;
            OnDockLayout(sender, e);
            DockLater();
        }
    }

    private void MoveToStandalone()
    {
        if (_standalone is { } rect)
        {
            Shots.Native.SetWindowPos(Handle, IntPtr.Zero, rect.Left, rect.Top, 0, 0, NoSize | NoZOrder | NoActivate);
        }
    }

    private void OnOwnerSized(object sender, SizeChangedEventArgs e) => OnDockLayout(sender, e);

    private void OnOwnerDpi(object sender, DpiChangedEventArgs e)
    {
        OnDockLayout(sender, e);
        DockLater();
    }

    private void OnOwnDpi(object sender, DpiChangedEventArgs e)
    {
        OnDockLayout(sender, e);
        DockLater();
    }

    private void DockLater() =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => OnDockLayout(null, EventArgs.Empty));

    private void Undock()
    {
        if (_dockHost is not null)
        {
            _dockHost.LayoutUpdated -= OnDockLayout;
            _dockHost = null;
        }

        if (_dockOwner is not null)
        {
            _dockOwner.LocationChanged -= OnOwnerMoved;
            _dockOwner.SizeChanged -= OnOwnerSized;
            _dockOwner.StateChanged -= OnOwnerState;
            _dockOwner.DpiChanged -= OnOwnerDpi;
            _dockOwner = null;
            DpiChanged -= OnOwnDpi;
        }

        MoveToStandalone();
        _standalone = null;
    }

    private void OnDockLayout(object? sender, EventArgs e)
    {
        if (OwnerMinimized || _dockHost is not { IsLoaded: true } host || PresentationSource.FromVisual(host) is null)
        {
            return;
        }

        var corner = host.PointToScreen(new Point(host.ActualWidth, 0));
        Shots.Native.GetWindowRect(Handle, out var current);
        var width = current.Right - current.Left;
        var height = current.Bottom - current.Top;
        var right = (int)Math.Round(corner.X);
        var x = right - width;
        var y = (int)Math.Round(corner.Y);
        if (PresentationSource.FromVisual(host) is HwndSource { Handle: var owner } && owner != IntPtr.Zero)
        {
            var info = new Shots.Native.MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<Shots.Native.MONITORINFO>() };
            if (Shots.Native.GetMonitorInfo(Shots.Native.MonitorFromWindow(owner, Shots.Native.MONITOR_DEFAULTTONEAREST), ref info))
            {
                right = Math.Min(right, info.Work.Right);
                x = Math.Max(info.Work.Left, right - width);
                y = Math.Max(info.Work.Top, Math.Min(y, info.Work.Bottom - height));
            }
        }

        if (current.Left != x || current.Top != y)
        {
            Shots.Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, NoSize | NoZOrder | NoActivate);
        }
    }

    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;

    private void Refresh()
    {
        Shots.LibraryWindow.SetColor(_session.Color);
        var customSelected = _customColor is { } custom && custom == _session.Color;
        _customSwatch.BorderBrush = customSelected ? Active : IdleRing;
        _customSwatch.BorderThickness = new Thickness(customSelected ? 4 : 2);
        foreach (var (color, button) in _swatches)
        {
            var selected = color == _session.Color;
            button.BorderBrush = selected ? Active : IdleRing;
            button.BorderThickness = new Thickness(selected ? 4 : 2);
        }
        foreach (var (width, button) in _penSizes)
        {
            Mark(button, width == _session.PenWidth);
        }
        Mark(PenButton, _session.Mode == Mode.Pen);
        Mark(TextButton, _session.Mode == Mode.Text);
        Mark(EraserButton, _session.Mode == Mode.Eraser);
        Mark(MouseButton, _session.Mode == Mode.Mouse);
        Mark(AirPlayButton, _receiver.State != ReceiverState.Stopped);
    }

    private static void Mark(Button button, bool active)
    {
        button.Background = active ? ActiveFill : IdleFill;
        button.BorderBrush = active ? Active : Brushes.Transparent;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        Shots.Native.GetWindowRect(Handle, out var before);
        DragMove();
        Shots.Native.GetWindowRect(Handle, out var after);
        if ((_dockHost is null || OwnerMinimized) && (after.Left != before.Left || after.Top != before.Top))
        {
            if (_dockHost is not null)
            {
                _standalone = after;
            }

            AppSettings.SaveToolbarPosition(after.Left, after.Top);
        }
    }

    // Every close path (X button, tray Exit, session end) keeps where the bar was, not only a drag.
    // Docked beside the Library, the bar's own place is the standalone spot it left.
    private void SaveCurrentPosition()
    {
        if (!IsLoaded)
        {
            return;
        }

        if (_dockHost is not null && !OwnerMinimized && _standalone is { } standalone)
        {
            AppSettings.SaveToolbarPosition(standalone.Left, standalone.Top);
        }
        else if (Shots.Native.GetWindowRect(Handle, out var rect))
        {
            AppSettings.SaveToolbarPosition(rect.Left, rect.Top);
        }
    }

    private bool RestorePosition()
    {
        if (AppSettings.LoadToolbarPosition() is not { } saved)
        {
            return false;
        }

        Shots.Native.GetWindowRect(Handle, out var current);
        var rect = new Shots.Native.RECT
        {
            Left = saved.X,
            Top = saved.Y,
            Right = saved.X + current.Right - current.Left,
            Bottom = saved.Y + current.Bottom - current.Top
        };
        if (Shots.Native.MonitorFromRect(ref rect, Shots.Native.MONITOR_DEFAULTTONULL) == IntPtr.Zero)
        {
            return false;
        }

        Shots.Native.SetWindowPos(Handle, IntPtr.Zero, saved.X, saved.Y, 0, 0, NoSize | NoZOrder | NoActivate);
        return true;
    }

    private void OnPen(object sender, RoutedEventArgs e) => _session.SetMode(Mode.Pen);
    private void OnText(object sender, RoutedEventArgs e) => _session.SetMode(Mode.Text);
    private void OnEraser(object sender, RoutedEventArgs e) => _session.SetMode(Mode.Eraser);
    private void OnMouse(object sender, RoutedEventArgs e) => _session.SetMode(Mode.Mouse);
    private void OnUndo(object sender, RoutedEventArgs e) => _session.Undo();
    private void OnClearAll(object sender, RoutedEventArgs e) => _session.ClearAll();
    private void OnToggleCollapse(object sender, RoutedEventArgs e)
    {
        var collapse = Body.Visibility == Visibility.Visible;
        Body.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        TrayButton.Visibility = collapse ? Visibility.Visible : Visibility.Collapsed;
        CollapseButton.Content = collapse ? "\uE70D" : "\uE70E";
        CollapseButton.ToolTip = collapse ? "Expand" : "Collapse";
    }

    private async void OnOptimize(object sender, RoutedEventArgs e)
    {
        var sequence = ++_statusSequence;
        OptimizeButton.IsEnabled = false;
        OptimizeButton.Content = "Optimizing...";
        var status = await _session.OptimizeAsync();
        OptimizeButton.IsEnabled = true;
        if (status is null)
        {
            ResetOptimize();
            return;
        }
        OptimizeButton.Content = status.Label;
        OptimizeButton.ToolTip = status.Tooltip;
        await Task.Delay(2000);
        if (sequence == _statusSequence)
        {
            ResetOptimize();
        }
    }

    private void ResetOptimize()
    {
        OptimizeButton.Content = "Optimize";
        OptimizeButton.ToolTip = "Clean up your last pen drawing";
    }

    private void OnMinimizeToTray(object sender, RoutedEventArgs e)
    {
        _session.SetMode(Mode.Mouse);
        Hide();
    }

    private void OnAirPlay(object sender, RoutedEventArgs e)
    {
        if (_receiver.State == ReceiverState.Stopped)
        {
            _receiver.Start();
            _status.ShowToast(this);
        }
        else
        {
            _receiver.Stop();
        }
    }

    private void OnSettings(object sender, RoutedEventArgs e) => SettingsWindow.Open(this);

    private async void OnCapture(object sender, RoutedEventArgs e) => await Shots.ShotCapture.Run(300);

    private void OnLibrary(object sender, RoutedEventArgs e) => Shots.LibraryWindow.Toggle();

    private void OnClose(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
