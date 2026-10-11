using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace PresenterTools;

public sealed class SettingsWindow : Window
{
    private static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(0x2D, 0x34, 0x36));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x00, 0x99, 0xFF));
    private static readonly Brush Field = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));

    private readonly TextBox _name = Input();
    private readonly TextBox _baseUrl = Input();
    private readonly PasswordBox _apiKey = Secret();
    private readonly TextBox _model = Input();
    private readonly TextBox _receiverName = Input();
    private readonly RadioButton _open = Choice("Open: any device on the network can connect");
    private readonly RadioButton _pin = Choice("PIN: new devices enter a 4-digit PIN once");
    private readonly RadioButton _password = Choice("Password: devices enter a password");
    private readonly TextBox _pinText = Input();
    private readonly PasswordBox _passwordText = Secret();
    private readonly CheckBox _fullScreen = Check("Show mirrored screens full screen");
    private readonly CheckBox _autostart = Check("Start with Windows (minimized in the system tray)");
    private readonly TextBlock _error = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x76, 0x75)), Margin = new Thickness(0, 0, 0, 8) };

    private readonly TextBox _shortcutBox = new()
    {
        IsReadOnly = true,
        IsReadOnlyCaretVisible = false,
        Background = Field,
        Foreground = Brushes.White,
        BorderBrush = Accent,
        Padding = new Thickness(4),
        Margin = new Thickness(0, 0, 0, 8)
    };
    private readonly TextBlock _shortcutStatus = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBox _saveFolderBox = new()
    {
        IsReadOnly = true,
        Background = Field,
        Foreground = Brushes.White,
        BorderBrush = Accent,
        Padding = new Thickness(4),
        Margin = new Thickness(0, 0, 0, 8)
    };
    private readonly TabControl _tabs = new() { Height = 340, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TabItem _airPlayTab;
    private CaptureSettings _shortcut = AppSettings.LoadCapture();

    public SettingsWindow()
    {
        Title = "Presenter Tools Settings";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Panel;
        Foreground = Brushes.White;

        var provider = AppSettings.LoadProvider();
        _name.Text = provider.Name;
        _baseUrl.Text = provider.BaseUrl;
        _apiKey.Password = provider.ApiKey;
        _model.Text = provider.Model;

        var receiver = AppSettings.LoadReceiver();
        _receiverName.Text = receiver.Name;
        _open.IsChecked = receiver.Access == Access.Open;
        _pin.IsChecked = receiver.Access == Access.Pin;
        _password.IsChecked = receiver.Access == Access.Password;
        _pinText.Text = receiver.Pin;
        _pinText.MaxLength = 4;
        _passwordText.Password = receiver.Password;
        _fullScreen.IsChecked = receiver.FullScreen;
        _autostart.IsChecked = Autostart.IsEnabled;
        ShowShortcut();
        _shortcutBox.PreviewKeyDown += OnShortcutKeyDown;
        _shortcutBox.PreviewKeyUp += OnShortcutKeyUp;
        var reset = MakeButton("Reset to Print Screen", isDefault: false);
        reset.Width = 160;
        reset.Margin = new Thickness(0);
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Click += (_, _) =>
        {
            _shortcut = _shortcut with { Modifiers = CaptureSettings.Default.Modifiers, VirtualKey = CaptureSettings.Default.VirtualKey };
            ShowShortcut();
        };

        _saveFolderBox.Text = _shortcut.SaveFolder;
        var browse = MakeButton("Browse...", isDefault: false);
        browse.Margin = new Thickness(0, 0, 6, 0);
        browse.Click += (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Save location", InitialDirectory = _shortcut.SaveFolder };
            if (picker.ShowDialog(this) == true && AppSettings.ValidFolder(picker.FolderName) is { } chosen)
            {
                _shortcut = _shortcut with { SaveFolder = chosen };
                _saveFolderBox.Text = chosen;
            }
        };
        var resetFolder = MakeButton("Reset to default", isDefault: false);
        resetFolder.Width = 120;
        resetFolder.Margin = new Thickness(0);
        resetFolder.Click += (_, _) =>
        {
            _shortcut = _shortcut with { SaveFolder = CaptureSettings.DefaultFolder };
            _saveFolderBox.Text = CaptureSettings.DefaultFolder;
        };
        var folderButtons = new StackPanel { Orientation = Orientation.Horizontal };
        folderButtons.Children.Add(browse);
        folderButtons.Children.Add(resetFolder);

        var link = new Hyperlink(new Run("deepinfra.com > Dashboard > API keys"))
        {
            NavigateUri = new Uri("https://deepinfra.com/dash/api_keys"),
            Foreground = Accent
        };
        link.RequestNavigate += (_, e) =>
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        var hint = new TextBlock { Margin = new Thickness(0, -4, 0, 8) };
        hint.Inlines.Add(new Run("Get a key at ") { Foreground = Brushes.LightGray });
        hint.Inlines.Add(link);

        var save = MakeButton("Save", isDefault: true);
        save.Click += (_, _) => OnSave();
        var cancel = MakeButton("Cancel", isDefault: false);
        cancel.IsCancel = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var ai = new StackPanel();
        ai.Children.Add(MakeLabel("Provider name"));
        ai.Children.Add(_name);
        ai.Children.Add(MakeLabel("Base URL (requests go to {Base URL}/chat/completions)"));
        ai.Children.Add(_baseUrl);
        ai.Children.Add(MakeLabel("API key"));
        ai.Children.Add(_apiKey);
        ai.Children.Add(hint);
        ai.Children.Add(MakeLabel("Model id"));
        ai.Children.Add(_model);

        var airPlay = new StackPanel();
        airPlay.Children.Add(MakeLabel("Receiver name (shown in Screen Mirroring on your devices)"));
        airPlay.Children.Add(_receiverName);
        airPlay.Children.Add(MakeLabel("Who can connect"));
        airPlay.Children.Add(_open);
        airPlay.Children.Add(_pin);
        airPlay.Children.Add(_password);
        airPlay.Children.Add(MakeLabel("PIN"));
        airPlay.Children.Add(_pinText);
        airPlay.Children.Add(MakeLabel("Password"));
        airPlay.Children.Add(_passwordText);
        airPlay.Children.Add(_fullScreen);

        var capture = new StackPanel();
        capture.Children.Add(MakeLabel("Capture shortcut (click the field, then press the keys)"));
        capture.Children.Add(_shortcutBox);
        capture.Children.Add(reset);
        capture.Children.Add(new TextBlock { Margin = new Thickness(0, 8, 0, 0) });
        capture.Children.Add(_shortcutStatus);
        capture.Children.Add(MakeLabel("Save location (captures and the Save dialog)"));
        capture.Children.Add(_saveFolderBox);
        capture.Children.Add(folderButtons);

        var general = new StackPanel();
        general.Children.Add(_autostart);

        _tabs.Style = (Style)XamlReader.Parse(TabStyles);
        _airPlayTab = MakeTab("AirPlay", airPlay);
        _tabs.Items.Add(MakeTab("AI", ai));
        _tabs.Items.Add(_airPlayTab);
        _tabs.Items.Add(MakeTab("Capture", capture));
        _tabs.Items.Add(MakeTab("General", general));
        _tabs.SelectedIndex = 0;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(_tabs);
        stack.Children.Add(_error);
        stack.Children.Add(buttons);
        Content = stack;

        Loaded += (_, _) =>
        {
            Activate();
            _apiKey.Focus();
        };
    }

    public static bool Open(Window? owner)
    {
        var window = new SettingsWindow();
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        return window.ShowDialog() == true;
    }

    private void OnSave()
    {
        var access = _pin.IsChecked == true ? Access.Pin : _password.IsChecked == true ? Access.Password : Access.Open;
        var pin = _pinText.Text.Trim();
        if (access == Access.Pin && !AppSettings.IsValidPin(pin))
        {
            _tabs.SelectedItem = _airPlayTab;
            _error.Text = "The PIN must be 4 digits.";
            return;
        }
        if (access == Access.Password && _passwordText.Password.Length == 0)
        {
            _tabs.SelectedItem = _airPlayTab;
            _error.Text = "Enter a password, or choose another option.";
            return;
        }

        AppSettings.SaveProvider(new ProviderSettings(
            Fallback(_name.Text, AppSettings.DefaultName),
            Fallback(_baseUrl.Text, AppSettings.DefaultBaseUrl),
            _apiKey.Password.Trim(),
            Fallback(_model.Text, AppSettings.DefaultModel)));
        var previous = AppSettings.LoadReceiver();
        AppSettings.SaveReceiver(new ReceiverSettings(
            Fallback(_receiverName.Text, Environment.MachineName),
            access,
            AppSettings.IsValidPin(pin) ? pin : previous.Pin,
            _passwordText.Password,
            _fullScreen.IsChecked == true));
        var autostart = _autostart.IsChecked == true;
        if (autostart != Autostart.IsEnabled)
        {
            Autostart.SetEnabled(autostart);
        }
        var oldFolder = AppSettings.LoadCapture().SaveFolder;
        AppSettings.SaveCapture(_shortcut);
        ChangeFolder(oldFolder, _shortcut.SaveFolder);
        DialogResult = true;
        App.Instance.ApplySettings();
    }

    private void ChangeFolder(string oldFolder, string newFolder)
    {
        if (string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var count = Shots.LibraryStore.List(oldFolder).Count;
        Shots.LibraryStore.UseFolder(newFolder);
        if (count == 0)
        {
            return;
        }
        var answer = MessageBox.Show(this,
            $"Move the existing {count} capture(s) (images and their sidecars) from\n{oldFolder}\nto\n{newFolder}?",
            "Save location", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }
        var (moved, skipped) = Shots.LibraryStore.MoveAll(oldFolder, newFolder);
        if (skipped.Count > 0)
        {
            MessageBox.Show(this,
                $"Moved {moved} capture(s). Skipped {skipped.Count} because a file with the same name already exists in the new folder:\n{string.Join("\n", skipped)}",
                "Save location", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowShortcut()
    {
        _shortcutBox.Text = CaptureHotkey.Describe(_shortcut);
        var registered = AppSettings.LoadCapture();
        var active = App.Instance.HotkeyRegistered;
        _shortcutStatus.Foreground = active ? Brushes.LightGray : new SolidColorBrush(Color.FromRgb(0xFF, 0x76, 0x75));
        _shortcutStatus.Text = active
            ? $"Active: {CaptureHotkey.Describe(registered)}"
            : CaptureHotkey.ConflictMessage(registered);
    }

    private void OnShortcutKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Snapshot)
        {
            e.Handled = true;
            return;
        }
        Record(key, e);
    }

    private void OnShortcutKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Snapshot)
        {
            Record(key, e);
        }
    }

    private void Record(Key key, KeyEventArgs e)
    {
        var pressed = Keyboard.Modifiers;
        if (pressed == ModifierKeys.None && key is Key.Tab or Key.Escape)
        {
            return;
        }
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None or Key.DeadCharProcessed or Key.ImeProcessed)
        {
            return;
        }
        var modifiers = 0u;
        if (pressed.HasFlag(ModifierKeys.Control)) modifiers |= CaptureHotkey.ModControl;
        if (pressed.HasFlag(ModifierKeys.Alt)) modifiers |= CaptureHotkey.ModAlt;
        if (pressed.HasFlag(ModifierKeys.Shift)) modifiers |= CaptureHotkey.ModShift;
        if (pressed.HasFlag(ModifierKeys.Windows)) modifiers |= CaptureHotkey.ModWin;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            return;
        }
        _shortcut = _shortcut with { Modifiers = modifiers, VirtualKey = (uint)virtualKey };
        ShowShortcut();
    }

    private static TabItem MakeTab(string header, StackPanel content)
    {
        content.Margin = new Thickness(12, 8, 12, 8);
        return new TabItem { Header = header, Content = content };
    }

    private const string TabStyles = @"
        <Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
               xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
               TargetType='TabControl'>
          <Setter Property='Background' Value='#2D3436'/>
          <Setter Property='BorderThickness' Value='0'/>
          <Setter Property='Template'>
            <Setter.Value>
              <ControlTemplate TargetType='TabControl'>
                <DockPanel>
                  <TabPanel DockPanel.Dock='Top' IsItemsHost='True' Background='Transparent'/>
                  <Border Background='#383F41' BorderBrush='#444444' BorderThickness='1'>
                    <ContentPresenter ContentSource='SelectedContent'/>
                  </Border>
                </DockPanel>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
          <Style.Resources>
            <Style TargetType='TabItem'>
              <Setter Property='Foreground' Value='LightGray'/>
              <Setter Property='Padding' Value='14,6'/>
              <Setter Property='Template'>
                <Setter.Value>
                  <ControlTemplate TargetType='TabItem'>
                    <Border x:Name='Bd' Background='Transparent' BorderBrush='Transparent'
                            BorderThickness='0,0,0,2' Padding='{TemplateBinding Padding}'>
                      <ContentPresenter ContentSource='Header' HorizontalAlignment='Center'
                                        TextElement.Foreground='{TemplateBinding Foreground}'/>
                    </Border>
                    <ControlTemplate.Triggers>
                      <Trigger Property='IsMouseOver' Value='True'>
                        <Setter TargetName='Bd' Property='Background' Value='#444444'/>
                      </Trigger>
                      <Trigger Property='IsSelected' Value='True'>
                        <Setter TargetName='Bd' Property='BorderBrush' Value='#0099FF'/>
                        <Setter Property='Foreground' Value='White'/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
          </Style.Resources>
        </Style>";

    private static string Fallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static TextBox Input() => new()
    {
        Background = Field,
        Foreground = Brushes.White,
        CaretBrush = Brushes.White,
        BorderBrush = Accent,
        Padding = new Thickness(4),
        Margin = new Thickness(0, 0, 0, 8)
    };

    private static PasswordBox Secret() => new()
    {
        Background = Field,
        Foreground = Brushes.White,
        BorderBrush = Accent,
        Padding = new Thickness(4),
        Margin = new Thickness(0, 0, 0, 8)
    };

    private static RadioButton Choice(string text) => new()
    {
        Content = text,
        GroupName = "Access",
        Foreground = Brushes.White,
        Margin = new Thickness(0, 2, 0, 4)
    };

    private static CheckBox Check(string text) => new()
    {
        Content = text,
        Foreground = Brushes.White,
        Margin = new Thickness(0, 4, 0, 8)
    };

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        Foreground = Brushes.LightGray,
        Margin = new Thickness(0, 4, 0, 2)
    };

    private static Button MakeButton(string text, bool isDefault) => new()
    {
        Content = text,
        IsDefault = isDefault,
        Width = 80,
        Padding = new Thickness(0, 4, 0, 4),
        Margin = new Thickness(6, 0, 0, 0),
        Foreground = Brushes.White,
        Background = isDefault ? Accent : Field,
        BorderThickness = new Thickness(0)
    };
}
