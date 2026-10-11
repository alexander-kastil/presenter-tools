using System.Windows;
using Forms = System.Windows.Forms;

namespace PresenterTools;

public sealed class TrayIcon : IDisposable
{
    private const string Name = "Presenter Tools";

    private readonly Session _session;
    private readonly Receiver _receiver;
    private readonly StatusWindow _status;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _toolbarItem;
    private readonly Forms.ToolStripMenuItem _receiverItem;

    public TrayIcon(Session session, Receiver receiver, StatusWindow status)
    {
        _session = session;
        _receiver = receiver;
        _status = status;

        _toolbarItem = new Forms.ToolStripMenuItem("Show toolbar", null, (_, _) => ToggleToolbar());
        _receiverItem = new Forms.ToolStripMenuItem("Start receiver", null, (_, _) => ToggleReceiver());
        var autostartItem = new Forms.ToolStripMenuItem("Start with Windows (minimized)")
        {
            CheckOnClick = true,
            Checked = Autostart.IsEnabled
        };
        autostartItem.CheckedChanged += (_, _) => Autostart.SetEnabled(autostartItem.Checked);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_toolbarItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("Show receiver status", null, (_, _) => _status.ShowAndActivate()));
        menu.Items.Add(_receiverItem);
        menu.Items.Add(autostartItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("Settings...", null, (_, _) => SettingsWindow.Open(null)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Exit", null, (_, _) => Application.Current.Shutdown()));
        menu.Opening += (_, _) =>
        {
            _toolbarItem.Text = IsToolbarVisible ? "Hide toolbar" : "Show toolbar";
            _receiverItem.Text = _receiver.State == ReceiverState.Stopped ? "Start receiver" : "Stop receiver";
            autostartItem.Checked = Autostart.IsEnabled;
        };

        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/PresenterTools.ico")).Stream;
        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream),
            Text = Name,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                ToggleToolbar();
            }
        };
        _receiver.Changed += (_, _) =>
        {
            var text = _receiver.State == ReceiverState.Running ? $"{Name}: {AppSettings.LoadReceiver().Name}" : Name;
            _icon.Text = text.Length > 63 ? text[..63] : text;
        };
    }

    private bool IsToolbarVisible => _session.Toolbar?.IsVisible == true;

    private void ToggleToolbar()
    {
        var toolbar = _session.Toolbar;
        if (toolbar is null)
        {
            return;
        }
        if (toolbar.IsVisible)
        {
            _session.SetMode(Mode.Mouse);
            toolbar.Hide();
        }
        else
        {
            toolbar.Show();
            toolbar.Activate();
        }
    }

    private void ToggleReceiver()
    {
        if (_receiver.State == ReceiverState.Stopped)
        {
            _receiver.Start();
            _status.ShowToast(_session.Toolbar);
        }
        else
        {
            _receiver.Stop();
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
