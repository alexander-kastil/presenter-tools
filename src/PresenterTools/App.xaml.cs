using System.Threading;
using System.Windows;

namespace PresenterTools;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private Receiver? _receiver;
    private StatusWindow? _status;
    private TrayIcon? _tray;
    private CaptureHotkey? _hotkey;

    public static App Instance => (App)Current;

    public Session? Session { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\integrations.at.PresenterTools", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        LegacyMigration.Run();

        var session = new Session();
        Session = session;
        foreach (var bounds in NativeMethods.GetMonitorBounds())
        {
            var overlay = new OverlayWindow(session, bounds);
            session.Overlays.Add(overlay);
            overlay.Show();
        }

        _receiver = new Receiver();
        _status = new StatusWindow(_receiver);
        var toolbar = new ToolbarWindow(session, _receiver, _status);
        session.Toolbar = toolbar;
        _tray = new TrayIcon(session, _receiver, _status);
        if (!e.Args.Contains(Autostart.Argument))
        {
            toolbar.Show();
        }
        session.SetMode(Mode.Mouse);

        _hotkey = new CaptureHotkey(() => _ = Shots.ShotCapture.Run(0));
        RegisterHotkey();
    }

    public bool HotkeyRegistered => _hotkey is { IsRegistered: true };

    private void RegisterHotkey()
    {
        var settings = AppSettings.LoadCapture();
        if (_hotkey is { } hotkey && !hotkey.Apply(settings))
        {
            MessageBox.Show(CaptureHotkey.ConflictMessage(settings), "Presenter Tools",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void ApplySettings()
    {
        RegisterHotkey();
        _status?.Refresh();
        if (_receiver is { State: not ReceiverState.Stopped })
        {
            _receiver.Restart();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _receiver?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
