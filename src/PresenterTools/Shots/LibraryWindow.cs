using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PresenterTools.Shots;

internal sealed class LibraryWindow : Window
{
    const double ThumbWidth = 200;
    const double ThumbHeight = 130;

    static LibraryWindow? instance;

    readonly WrapPanel strip = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
    readonly EditorView editor = new();
    readonly ScrollViewer gallery = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.Transparent };
    readonly Dictionary<string, BitmapSource> cache = [];
    readonly Dictionary<string, Border> tiles = [];
    string signature = string.Empty;
    string? selectedId;
    string? loadedId = string.Empty;

    public static double ReservedWidth { get; set; } = 176;

    double rowHeight;

    readonly Grid body = new();
    bool maximizedPreview;

    public static event Action? OpenChanged;

    public static FrameworkElement? DockHost => instance?.body;

    public static bool IsOpen => instance is not null;

    public static FrameworkElement? ToolPanel => instance?.editor.ToolPanel;

    public static void HandleShortcut(KeyEventArgs e) => instance?.editor.HandleShortcut(e);

    public static void SetColor(Color color) => instance?.editor.SetColor(color);

    public static void Toggle()
    {
        if (instance is null)
        {
            ShowOrActivate();
        }
        else
        {
            instance.Close();
        }
    }

    public static void ShowOrActivate(string? selectId = null)
    {
        if (instance is null)
        {
            instance = new LibraryWindow { selectedId = selectId };
            instance.Show();
            OpenChanged?.Invoke();
        }

        TrayWindows.Restore(instance);
        instance.WindowState = WindowState.Maximized;
        if (selectId is not null)
        {
            instance.SelectById(selectId);
        }
    }

    void SelectById(string id)
    {
        Reload();
        var entry = LibraryStore.List().FirstOrDefault(e => e.Id == id);
        if (entry is not null)
        {
            Select(entry);
        }
    }

    LibraryWindow()
    {
        Title = "Presenter Tools Library";
        Width = 960;
        Height = 640;
        gallery.Content = strip;
        gallery.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Chrome.Get("BlueScrollBar"));
        gallery.PreviewMouseWheel += (_, e) =>
        {
            if (gallery.ScrollableHeight > 0 && rowHeight > 0)
            {
                var row = (int)Math.Round(gallery.VerticalOffset / rowHeight) + (e.Delta < 0 ? 1 : -1);
                gallery.ScrollToVerticalOffset(Math.Clamp(row * rowHeight, 0, gallery.ScrollableHeight));
                e.Handled = true;
            }
        };
        gallery.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (rowHeight > 0)
            {
                var row = (int)Math.Round(gallery.VerticalOffset / rowHeight);
                gallery.ScrollToVerticalOffset(Math.Clamp(row * rowHeight, 0, gallery.ScrollableHeight));
            }
        };
        gallery.SizeChanged += (_, _) => QueueSnap();
        gallery.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                QueueSnap();
            }
        };
        body.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && source is not TextBox && !IsInsideTextBox(source))
            {
                maximizedPreview = !maximizedPreview;
                gallery.Visibility = maximizedPreview ? Visibility.Collapsed : Visibility.Visible;
                e.Handled = true;
            }
        };
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ReservedWidth) });
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(gallery, 1);
        body.Children.Add(editor);
        body.Children.Add(gallery);
        Content = Chrome.Wrap(this, body);
        Loaded += (_, _) => Reload();
        Activated += (_, _) => Reload();
        Closed += (_, _) =>
        {
            editor.Persist();
            instance = null;
            OpenChanged?.Invoke();
        };
    }

    static bool IsInsideTextBox(DependencyObject source)
    {
        for (var node = source; node is not null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is TextBox)
            {
                return true;
            }
        }

        return false;
    }

    static string Stamp(CaptureEntry entry) =>
        entry.Id + File.GetLastWriteTimeUtc(entry.JsonPath).Ticks;

    void Reload()
    {
        var entries = LibraryStore.List();
        var current = string.Join('|', entries.Select(Stamp));
        if (current == signature)
        {
            return;
        }

        signature = current;
        strip.Children.Clear();
        tiles.Clear();
        foreach (var entry in entries)
        {
            var tile = Tile(entry);
            tiles[entry.Id] = tile;
            strip.Children.Add(tile);
        }

        if (strip.Children.Count > 0)
        {
            strip.Children[0].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            rowHeight = strip.Children[0].DesiredSize.Height;
            gallery.Height = rowHeight + strip.Margin.Top + strip.Margin.Bottom;
        }

        var selected = entries.FirstOrDefault(e => e.Id == selectedId) ?? entries.FirstOrDefault();
        if (selected is null)
        {
            selectedId = null;
            loadedId = string.Empty;
            editor.Load(null);
            return;
        }

        Select(selected);
    }

    void Select(CaptureEntry entry)
    {
        selectedId = entry.Id;
        if (entry.Id != loadedId)
        {
            editor.Persist();
            loadedId = entry.Id;
            editor.Load(entry);
        }

        foreach (var (id, tile) in tiles)
        {
            tile.BorderBrush = id == entry.Id ? Chrome.Accent : Brushes.Transparent;
        }

        SnapToSelected();
    }

    void QueueSnap() =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, SnapToSelected);

    void SnapToSelected()
    {
        if (selectedId is null || rowHeight <= 0 || !tiles.TryGetValue(selectedId, out var tile) || gallery.Visibility != Visibility.Visible || gallery.ActualWidth <= 0)
        {
            return;
        }

        gallery.UpdateLayout();
        var top = tile.TranslatePoint(new Point(0, 0), strip).Y - tile.Margin.Top;
        var row = Math.Max(0, (int)Math.Round(top / rowHeight));
        gallery.ScrollToVerticalOffset(Math.Clamp(row * rowHeight, 0, gallery.ScrollableHeight));
    }

    Border Tile(CaptureEntry entry)
    {
        var panel = new StackPanel();
        panel.Children.Add(new Image { Source = Thumbnail(entry), Width = ThumbWidth, Height = ThumbHeight, Stretch = Stretch.Uniform });
        panel.Children.Add(new TextBlock { Text = entry.Time.ToString("d MMM yyyy  HH:mm:ss"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), Foreground = Brushes.LightGray });
        var tile = new Border
        {
            Child = panel,
            Margin = new Thickness(6),
            Padding = new Thickness(6),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Background = Chrome.Field,
            Cursor = Cursors.Hand,
        };
        tile.MouseLeftButtonDown += (_, _) => Select(entry);
        return tile;
    }

    BitmapSource Thumbnail(CaptureEntry entry)
    {
        var key = Stamp(entry);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var thumb = Render(entry, ThumbWidth, ThumbHeight);
        cache[key] = thumb;
        return thumb;
    }

    static BitmapSource Render(CaptureEntry entry, double maxWidth, double maxHeight)
    {
        var sidecar = LibraryStore.LoadSidecar(entry);
        var canvas = Annotations.Compose(LibraryStore.LoadImage(entry), sidecar.Annotations);
        var area = sidecar.Crop is { } c ? new Rect(c.X, c.Y, c.W, c.H) : new Rect(0, 0, canvas.Width, canvas.Height);
        var scale = Math.Min(1, Math.Min(maxWidth / area.Width, maxHeight / area.Height));
        return Annotations.Render(canvas, Math.Max(1, (int)(area.Width * scale)), Math.Max(1, (int)(area.Height * scale)), sidecar.Crop is null ? null : area);
    }
}
