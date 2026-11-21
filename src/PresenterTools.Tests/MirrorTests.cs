using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Xunit;

namespace PresenterTools.Tests;

public class MirrorTests
{
    [Theory]
    [InlineData("connection request from Alex's iPad (iPad13,8) with deviceID = 5A:1B:2C:3D:4E:5F", "Alex's iPad", "iPad13,8", "iPad")]
    [InlineData("connection request from iPhone (iPhone15,2) with deviceID = 6A:1B:2C:3D:4E:5F", "iPhone", "iPhone15,2", "iPhone")]
    public void The_engines_connection_line_names_the_device(string line, string name, string model, string device)
    {
        Receiver.ParseClient(line).Should().Be((name, model));
        Mirror.DeviceOf(model).Should().Be(device);
    }

    [Fact]
    public void Other_engine_lines_name_no_device()
    {
        Receiver.ParseClient("Initialized server socket(s)").Should().BeNull();
    }

    [Theory]
    [InlineData("ok", true)]
    [InlineData("ok device=iPad size=1640x2360 rect=0,0,1640,2360 name=iPad", true)]
    [InlineData("okay", false)]
    [InlineData("error=not mirrored", false)]
    public void Exit_code_follows_the_reply(string reply, bool ok)
    {
        ScriptCommand.IsOk(reply).Should().Be(ok);
    }

    [Fact]
    public void The_mirror_window_is_reported_and_captured_over_the_pipe()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pt-mirror-" + Guid.NewGuid().ToString("N"));
        var pipe = "PresenterTools-test-" + Guid.NewGuid().ToString("N");
        Sta.Run(async () =>
        {
            var monitor = NativeMethods.GetMonitorBounds()[0];
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Background = Brushes.Lime,
                Width = 300,
                Height = 200
            };
            var session = new Session();
            Mirror? mirrored = null;
            using var channel = new ScriptChannel(System.Windows.Threading.Dispatcher.CurrentDispatcher,
                message => ScriptCommand.Run(session, () => mirrored, message.Args, message.Folder), pipe);
            Task<string> Send(params string[] args) => ScriptChannel.SendAsync(new ScriptMessage(folder, args), pipe);
            try
            {
                (await Send("--cmd", "mirror")).Should().Be("error=not mirrored");
                (await Send("--cmd", "capture", "--target", "mirror", "--out", "phone.png")).Should().Be("error=not mirrored");

                window.Show();
                Shots.Native.SetWindowPos(new WindowInteropHelper(window).Handle, 0, monitor.Left + 100, monitor.Top + 100, 0, 0, 0x0001 | 0x0004 | 0x0010);
                await Sta.Settle();
                Shots.Native.GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds);
                var picture = Mirror.PictureOf(Environment.ProcessId);
                picture.Should().NotBeNull();
                (picture!.Value.Left, picture.Value.Top, picture.Value.Right, picture.Value.Bottom)
                    .Should().Be((bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), "the window has no frame, so its picture is the whole window");

                var (width, height) = (bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                mirrored = new Mirror("iPhone", "Alex's iPhone", bounds.Left, bounds.Top, width, height);
                (await Send("--cmd", "mirror")).Should().Be(
                    $"ok device=iPhone size={width}x{height} rect={bounds.Left},{bounds.Top},{width},{height} name=Alex's iPhone");

                (await Send("--cmd", "capture", "--target", "mirror", "--out", @"takes\4.1-t1\frames\4.1.2.png")).Should().Be("ok");
                var shot = BitmapFrame.Create(new Uri(Path.Combine(folder, @"takes\4.1-t1\frames\4.1.2.png")), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                shot.PixelWidth.Should().Be(width);
                shot.PixelHeight.Should().Be(height);
                var pixel = new byte[4];
                shot.CopyPixels(new Int32Rect(width / 2, height / 2, 1, 1), pixel, 4, 0);
                (pixel[2], pixel[1], pixel[0]).Should().Be(((byte)0, (byte)255, (byte)0), "the mirror window is on screen when captured");
            }
            finally
            {
                window.Close();
            }
        });
        Directory.Delete(folder, true);
    }
}
