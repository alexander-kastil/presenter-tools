using System.Windows.Interop;
using FluentAssertions;
using Xunit;

namespace PresenterTools.Tests;

public class ToolbarPositionTests
{
    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;

    // A spot well inside the right-most monitor, in physical pixels like the saved value.
    private static (int X, int Y) SpotOnLastMonitor()
    {
        var monitor = NativeMethods.GetMonitorBounds().OrderByDescending(m => m.Left).First();
        return (monitor.Left + 300, monitor.Top + 300);
    }

    private sealed class Bar : IDisposable
    {
        private readonly Receiver _receiver = new();
        private readonly StatusWindow _status;

        public Bar()
        {
            _status = new StatusWindow(_receiver);
            Window = new ToolbarWindow(new Session(), _receiver, _status) { ShowActivated = false };
        }

        public ToolbarWindow Window { get; }

        private nint Handle => new WindowInteropHelper(Window).Handle;

        public async Task ShowAsync()
        {
            Window.Show();
            await Sta.Settle();
        }

        public (int X, int Y) Position
        {
            get
            {
                Shots.Native.GetWindowRect(Handle, out var rect);
                return (rect.Left, rect.Top);
            }
        }

        public async Task MoveToAsync((int X, int Y) spot)
        {
            Shots.Native.SetWindowPos(Handle, 0, spot.X, spot.Y, 0, 0, NoSize | NoZOrder | NoActivate);
            await Sta.Settle(300);
        }

        public void Dispose()
        {
            if (Window.IsLoaded)
            {
                Window.Close();
            }

            _status.Close();
            _receiver.Dispose();
        }
    }

    [Fact]
    public void Saved_position_round_trips_through_the_settings_file()
    {
        using var settings = new TempSettings();

        AppSettings.LoadToolbarPosition().Should().BeNull();
        AppSettings.SaveToolbarPosition(7263, 995);

        AppSettings.LoadToolbarPosition().Should().Be((7263, 995));
    }

    [Fact]
    public void Bar_starts_at_the_saved_position()
    {
        var spot = SpotOnLastMonitor();
        using var settings = new TempSettings("{\"Toolbar\":{\"X\":" + spot.X + ",\"Y\":" + spot.Y + "}}");

        Sta.Run(async () =>
        {
            using var bar = new Bar();
            await bar.ShowAsync();

            bar.Position.Should().Be(spot);
        });
    }

    [Fact]
    public void Bar_comes_back_where_it_was_when_the_app_was_closed()
    {
        var spot = SpotOnLastMonitor();
        using var settings = new TempSettings();

        Sta.Run(async () =>
        {
            using (var first = new Bar())
            {
                await first.ShowAsync();
                await first.MoveToAsync(spot);
                first.Window.Close();
            }

            AppSettings.LoadToolbarPosition().Should().Be(spot);

            using var second = new Bar();
            await second.ShowAsync();
            second.Position.Should().Be(spot);
        });
    }
}
