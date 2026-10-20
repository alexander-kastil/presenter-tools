using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;
using Xunit.Abstractions;

namespace PresenterTools.Tests;

public class Diag2(ITestOutputHelper output)
{
    [Fact]
    public void Cursor()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var bmp = new WriteableBitmap(400, 300, 96, 96, PixelFormats.Bgra32, null);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var f = File.Create(Path.Combine(dir, "20260101-000000-000.png"))) enc.Save(f);
        Shots.LibraryStore.UseFolder(dir);
        Sta.Run(async () =>
        {
            var view = new Shots.EditorView();
            var win = new Window { Width = 900, Height = 700, Content = view, Left = 100, Top = 100 };
            win.Show();
            view.Load(Shots.LibraryStore.List()[0]);
            await Sta.Settle();
            var pt = new Point(view.ActualWidth / 2, view.ActualHeight / 2);
            var hit = view.InputHitTest(pt) as DependencyObject;
            output.WriteLine($"hit {hit?.GetType().Name} at {pt}");
            for (DependencyObject? d = hit; d != null; d = VisualTreeHelper.GetParent(d))
                output.WriteLine($"  {d.GetType().Name} cursor={(d as FrameworkElement)?.Cursor}");
            var args = new QueryCursorEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.QueryCursorEvent };
            (hit as UIElement)?.RaiseEvent(args);
            output.WriteLine($"query cursor = {args.Cursor}");
            win.Close();
        });
        Directory.Delete(dir, true);
    }
}
