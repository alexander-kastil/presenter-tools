using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using PresenterTools.Shots;
using Xunit;

namespace PresenterTools.Tests;

public class EditorViewTests
{
    public static TheoryData<string, Cursor> ToolCursors => new()
    {
        { "Pen", Cursors.Pen },
        { "Text", Cursors.IBeam },
        { "Box", Cursors.Cross },
        { "Line", Cursors.Cross },
        { "Shape", Cursors.Cross },
        { "Fill", Cursors.Cross },
        { "Crop", Cursors.Cross },
        { "Arrow", Cursors.Cross },
    };

    public static TheoryData<string> Tools => new() { "Select", "Pen", "Text", "Box", "Line", "Shape", "Fill", "Crop", "Arrow" };

    // Hosts an EditorView with a small image in a window, so the image covers only part of the drawing area.
    private static async Task WithEditor(Func<EditorView, Task> body)
    {
        var folder = Path.Combine(Path.GetTempPath(), "pt-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var previous = LibraryStore.Root;
        try
        {
            var picture = new WriteableBitmap(400, 300, 96, 96, PixelFormats.Bgra32, null);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(picture));
            using (var file = File.Create(Path.Combine(folder, "20260101-000000-000.png")))
            {
                encoder.Save(file);
            }

            LibraryStore.UseFolder(folder);
            var view = new EditorView();
            var window = new Window { Width = 900, Height = 700, Left = 100, Top = 100, Content = view, ShowActivated = false };
            window.Show();
            view.Load(LibraryStore.List()[0]);
            await Sta.Settle();
            try
            {
                await body(view);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            LibraryStore.UseFolder(previous);
            Directory.Delete(folder, true);
        }
    }

    private static void Pick(EditorView view, string tool)
    {
        var button = Descendants(view.ToolPanel).OfType<RadioButton>().Single(b => (string)b.ToolTip == tool);
        button.IsChecked = true;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    // What WPF asks the element under the pointer: the QueryCursor event bubbles up from the hit element.
    private static Cursor? CursorAt(EditorView view, Point point)
    {
        var hit = (UIElement)view.InputHitTest(point);
        var args = new QueryCursorEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.QueryCursorEvent };
        hit.RaiseEvent(args);
        return args.Cursor;
    }

    private static double Zoom(EditorView view) =>
        ((ScaleTransform)((TransformGroup)((Viewbox)((Grid)view.Content).Children[0]).RenderTransform).Children[0]).ScaleX;

    private static void Wheel(EditorView view, int delta)
    {
        var area = (Grid)view.Content;
        area.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
            Source = area,
        });
    }

    [Theory]
    [MemberData(nameof(ToolCursors))]
    public void Tool_cursor_shows_over_the_empty_part_of_the_drawing_area(string tool, Cursor expected) =>
        Sta.Run(() => WithEditor(view =>
        {
            Pick(view, tool);

            CursorAt(view, new Point(6, 6)).Should().BeSameAs(expected);
            return Task.CompletedTask;
        }));

    [Theory]
    [MemberData(nameof(ToolCursors))]
    public void Tool_cursor_shows_over_the_image(string tool, Cursor expected) =>
        Sta.Run(() => WithEditor(view =>
        {
            Pick(view, tool);

            CursorAt(view, new Point(view.ActualWidth / 2, view.ActualHeight / 2)).Should().BeSameAs(expected);
            return Task.CompletedTask;
        }));

    [Theory]
    [MemberData(nameof(Tools))]
    public void Mouse_wheel_zooms_in_and_out_with_every_tool(string tool) =>
        Sta.Run(() => WithEditor(view =>
        {
            Pick(view, tool);
            Zoom(view).Should().Be(1);

            Wheel(view, 120);
            var zoomedIn = Zoom(view);
            Wheel(view, 120);

            zoomedIn.Should().BeGreaterThan(1);
            Zoom(view).Should().BeGreaterThan(zoomedIn);

            Wheel(view, -120);
            Wheel(view, -120);
            Zoom(view).Should().BeApproximately(1, 1e-9);
            return Task.CompletedTask;
        }));
}
