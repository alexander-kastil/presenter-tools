using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Xunit;

namespace PresenterTools.Tests;

public class ScriptCommandTests
{
    private const string Folder = @"C:\takes\1.1-t1";

    [Fact]
    public void Draw_step_parses_every_field()
    {
        var (request, error) = ScriptCommand.Parse(
            ["--cmd", "draw", "--shape", "ellipse", "--rect", "120,80,220,60", "--color", "#FF8800", "--width", "5", "--label", "1.1.2", "--size", "22"], Folder);

        error.Should().BeNull();
        request!.Cmd.Should().Be("draw");
        request.Shape.Should().Be("ellipse");
        request.Rect.Should().Equal(120, 80, 220, 60);
        request.Color.Should().Be("#FF8800");
        request.Width.Should().Be(5);
        request.Label.Should().Be("1.1.2");
        request.Size.Should().Be(22);
    }

    [Fact]
    public void Capture_resolves_a_relative_out_against_the_callers_folder()
    {
        var (request, _) = ScriptCommand.Parse(["--cmd", "capture", "--out", @"evidence\1.1.3.png"], Folder);

        request!.Out.Should().Be(@"C:\takes\1.1-t1\evidence\1.1.3.png");
    }

    [Theory]
    [InlineData("missing --cmd")]
    [InlineData("unknown command jump", "--cmd", "jump")]
    [InlineData("unknown option --colour", "--cmd", "clear", "--colour", "red")]
    [InlineData("missing value for --text", "--cmd", "write", "--text")]
    [InlineData("missing --shape", "--cmd", "draw", "--rect", "1,2,3,4")]
    [InlineData("unknown shape star", "--cmd", "draw", "--shape", "star", "--rect", "1,2,3,4")]
    [InlineData("missing --rect", "--cmd", "draw", "--shape", "arrow")]
    [InlineData("bad --rect", "--cmd", "draw", "--shape", "arrow", "--rect", "1,2,3")]
    [InlineData("empty --rect", "--cmd", "draw", "--shape", "line", "--rect", "1,2,0,0")]
    [InlineData("bad --color", "--cmd", "draw", "--shape", "line", "--rect", "1,2,3,4", "--color", "nope")]
    [InlineData("bad --width", "--cmd", "draw", "--shape", "line", "--rect", "1,2,3,4", "--width", "0")]
    [InlineData("missing --text", "--cmd", "write", "--at", "1,2")]
    [InlineData("missing --at", "--cmd", "write", "--text", "Hi")]
    [InlineData("missing --out", "--cmd", "capture")]
    [InlineData("empty --rect", "--cmd", "capture", "--out", "a.png", "--rect", "0,0,0,10")]
    [InlineData("unknown target screen", "--cmd", "capture", "--out", "a.png", "--target", "screen")]
    [InlineData("use --rect or --target, not both", "--cmd", "capture", "--out", "a.png", "--target", "mirror", "--rect", "1,2,3,4")]
    public void Bad_arguments_name_the_reason(string reason, params string[] args)
    {
        var (request, error) = ScriptCommand.Parse(args, Folder);

        request.Should().BeNull();
        error.Should().Be(reason);
    }

    [Fact]
    public async Task Nothing_listening_reports_not_running()
    {
        var reply = await ScriptChannel.SendAsync(new ScriptMessage(Folder, ["--cmd", "clear"]), "PresenterTools-test-" + Guid.NewGuid().ToString("N"));

        reply.Should().Be("error=not running");
    }

    [Fact]
    public void Commands_over_the_pipe_draw_write_capture_and_clear_on_the_overlays()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pt-script-" + Guid.NewGuid().ToString("N"));
        var pipe = "PresenterTools-test-" + Guid.NewGuid().ToString("N");
        var monitor = NativeMethods.GetMonitorBounds()[0];
        var (x, y) = (monitor.Left + 200, monitor.Top + 200);
        Sta.Run(async () =>
        {
            var session = new Session();
            foreach (var bounds in NativeMethods.GetMonitorBounds())
            {
                var overlay = new OverlayWindow(session, bounds) { ShowActivated = false };
                session.Overlays.Add(overlay);
                overlay.Show();
            }
            session.SetMode(Mode.Mouse);
            using var channel = new ScriptChannel(System.Windows.Threading.Dispatcher.CurrentDispatcher,
                message => ScriptCommand.Run(session, () => null, message.Args, message.Folder), pipe);
            Task<string> Send(params string[] args) => ScriptChannel.SendAsync(new ScriptMessage(folder, args), pipe);
            var ink = session.Overlays.Single(o => o.Bounds.Left == monitor.Left && o.Bounds.Top == monitor.Top).Ink;
            try
            {
                await Sta.Settle(300);

                (await Send("--cmd", "draw", "--shape", "rectangle", "--rect", $"{x},{y},300,150", "--color", "#FF0000", "--width", "8", "--label", "1.1.2")).Should().Be("ok");
                (await Send("--cmd", "write", "--text", "Hello", "--at", $"{x},{y + 300}")).Should().Be("ok");
                ink.Strokes.Should().HaveCount(1);
                ink.Children.OfType<TextBlock>().Select(t => t.Text).Should().Equal("1.1.2", "Hello");
                await Sta.Settle();

                (await Send("--cmd", "capture", "--out", @"evidence\1.1.3.png", "--rect", $"{x - 50},{y - 50},400,250")).Should().Be("ok");
                var shot = BitmapFrame.Create(new Uri(Path.Combine(folder, @"evidence\1.1.3.png")), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                shot.PixelWidth.Should().Be(400);
                shot.PixelHeight.Should().Be(250);
                var pixel = new byte[4];
                shot.CopyPixels(new System.Windows.Int32Rect(200, 50, 1, 1), pixel, 4, 0);
                pixel[2].Should().BeGreaterThan(200, "the red stroke is on screen when captured");
                pixel[1].Should().BeLessThan(60);

                (await Send("--cmd", "draw", "--shape", "arrow", "--rect", "-100000,-100000,10,10")).Should().Be("error=off screen");
                (await Send("--cmd", "clear")).Should().Be("ok");
                ink.Strokes.Should().BeEmpty();
                ink.Children.Count.Should().Be(0);

                session.Undo();
                ink.Strokes.Should().HaveCount(1, "a cleared drawing comes back with Undo like one drawn by hand");
            }
            finally
            {
                foreach (var overlay in session.Overlays)
                {
                    overlay.Close();
                }
            }
        });
        Directory.Delete(folder, true);
    }
}
