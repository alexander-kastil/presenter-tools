using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentAssertions;
using PresenterTools.Shots;
using Xunit;
using Xunit.Abstractions;

namespace PresenterTools.Tests;

public class LibraryThumbnailTests(ITestOutputHelper output)
{
    private const int Shots = 40;

    private static string MakeLibrary(int count)
    {
        var folder = Path.Combine(Path.GetTempPath(), "pt-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var width = 3000;
        var height = 1700;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var random = new Random(7);
        random.NextBytes(pixels);
        for (var i = 0; i < count; i++)
        {
            var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(picture));
            using var file = File.Create(Path.Combine(folder, $"20260101-0000{i:00}-000.png"));
            encoder.Save(file);
        }

        return folder;
    }

    [Fact]
    public void Opening_the_library_does_not_block_the_ui_thread_on_decoding_every_shot()
    {
        var folder = MakeLibrary(Shots);
        var previous = LibraryStore.Root;
        try
        {
            LibraryStore.UseFolder(folder);
            Sta.Run(async () =>
            {
                var stopwatch = Stopwatch.StartNew();
                var dispatcher = Dispatcher.CurrentDispatcher;
                LibraryWindow.ShowOrActivate();
                var latency = new TaskCompletionSource<long>();
                _ = dispatcher.BeginInvoke(DispatcherPriority.Input,() => latency.SetResult(stopwatch.ElapsedMilliseconds));
                var stall = await latency.Task;
                output.WriteLine($"input handled after {stall} ms with {Shots} shots");
                LibraryWindow.Toggle();
                await Sta.Settle(100);
                stall.Should().BeLessThan(1500);
            });
        }
        finally
        {
            LibraryStore.UseFolder(previous);
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Plain_shot_thumbnail_is_decoded_at_thumbnail_size()
    {
        var folder = MakeLibrary(1);
        var previous = LibraryStore.Root;
        try
        {
            LibraryStore.UseFolder(folder);
            Sta.Run(() =>
            {
                var entry = LibraryStore.List()[0];
                var stopwatch = Stopwatch.StartNew();
                var thumb = LibraryWindow.Render(entry, 200, 130);
                output.WriteLine($"thumbnail {thumb.PixelWidth}x{thumb.PixelHeight} in {stopwatch.ElapsedMilliseconds} ms");
                thumb.PixelWidth.Should().BeLessOrEqualTo(200);
                thumb.PixelHeight.Should().BeLessOrEqualTo(130);
            });
        }
        finally
        {
            LibraryStore.UseFolder(previous);
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Empty_sidecar_still_renders_the_shot()
    {
        var folder = MakeLibrary(1);
        var previous = LibraryStore.Root;
        try
        {
            LibraryStore.UseFolder(folder);
            Sta.Run(() =>
            {
                var entry = LibraryStore.List()[0];
                File.WriteAllText(entry.JsonPath, string.Empty);
                LibraryStore.LoadSidecar(entry).Annotations.Should().BeEmpty();
                LibraryWindow.Render(entry, 200, 130).PixelWidth.Should().BeGreaterThan(0);
            });
        }
        finally
        {
            LibraryStore.UseFolder(previous);
            Directory.Delete(folder, true);
        }
    }
}
