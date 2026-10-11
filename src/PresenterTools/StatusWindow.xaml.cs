using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PresenterTools;

public partial class StatusWindow : Window
{
    private const int ToastMargin = 16;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;

    private readonly Receiver _receiver;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _toastActive;
    private bool _toastPlaced;

    public StatusWindow(Receiver receiver)
    {
        _receiver = receiver;
        _toastTimer.Tick += (_, _) => EndToast();
        InitializeComponent();
        _receiver.Changed += (_, _) => Refresh();
        Refresh();
    }

    public void ShowToast(Window? anchor)
    {
        if (IsVisible && !_toastActive)
        {
            return;
        }

        _toastTimer.Stop();
        _toastActive = true;
        _toastPlaced = true;
        if (!IsVisible)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowActivated = false;
            Topmost = true;
            Show();
        }
        PlaceToast(anchor);
        _toastTimer.Start();
    }

    private void PlaceToast(Window? anchor)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var anchorHandle = anchor is null ? IntPtr.Zero : new WindowInteropHelper(anchor).Handle;
        var info = new Shots.Native.MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<Shots.Native.MONITORINFO>() };
        var monitor = Shots.Native.MonitorFromWindow(anchorHandle, Shots.Native.MONITOR_DEFAULTTONEAREST);
        if (handle == IntPtr.Zero || !Shots.Native.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        for (var pass = 0; pass < 2; pass++)
        {
            Shots.Native.GetWindowRect(handle, out var rect);
            var x = info.Work.Left + ToastMargin;
            var y = info.Work.Bottom - (rect.Bottom - rect.Top) - ToastMargin;
            Shots.Native.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0001 | NoZOrder | NoActivate);
        }
    }

    private void EndToast()
    {
        _toastTimer.Stop();
        _toastActive = false;
        Topmost = false;
        Hide();
    }

    public void ShowAndActivate()
    {
        _toastTimer.Stop();
        if (_toastActive || _toastPlaced)
        {
            _toastActive = false;
            _toastPlaced = false;
            Topmost = false;
            ShowActivated = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var work = SystemParameters.WorkArea;
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Top + (work.Height - ActualHeight) / 2;
        }
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    public void Refresh()
    {
        var settings = AppSettings.LoadReceiver();
        NameText.Text = settings.Name;
        AccessText.Text = settings.Access switch
        {
            Access.Pin => $"PIN for new devices: {settings.Pin}",
            Access.Password => "Password required",
            _ => "Any AirPlay device on your network can connect"
        };

        var color = _receiver.State switch
        {
            ReceiverState.Running => Color.FromRgb(0x00, 0xB8, 0x94),
            ReceiverState.Starting => Color.FromRgb(0xFD, 0xCB, 0x6E),
            ReceiverState.Failed => Color.FromRgb(0xFF, 0x76, 0x75),
            _ => Color.FromRgb(0x99, 0x99, 0x99)
        };
        StateDot.Fill = new SolidColorBrush(color);
        ToggleButton.Content = _receiver.State == ReceiverState.Stopped ? "Start" : "Stop";

        ClientText.Text = _receiver.LastClient is { } client ? $"Last connection: {client}" : "";
        ErrorText.Text = _receiver.Error ?? "";
        ErrorText.Visibility = _receiver.Error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (_receiver.State == ReceiverState.Stopped)
        {
            _receiver.Start();
        }
        else
        {
            _receiver.Stop();
        }
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        SettingsWindow.Open(this);
    }

    private void OnLog(object sender, RoutedEventArgs e)
    {
        if (File.Exists(Receiver.LogPath))
        {
            Process.Start(new ProcessStartInfo(Receiver.LogPath) { UseShellExecute = true });
        }
    }

    // Closing the window keeps the receiver running in the tray.
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        _toastTimer.Stop();
        _toastActive = false;
        Topmost = false;
        Hide();
    }
}
