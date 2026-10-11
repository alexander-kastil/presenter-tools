using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace PresenterTools.Shots;

internal static class Chrome
{
    public static readonly Brush Panel = Solid(0x2D, 0x34, 0x36);
    public static readonly Brush Accent = Solid(0x00, 0x99, 0xFF);
    public static readonly Brush Field = Solid(0x44, 0x44, 0x44);
    public static readonly Brush Ring = Solid(0x77, 0x77, 0x77);

    static readonly FontFamily Icons = new("Segoe MDL2 Assets");
    static readonly ResourceDictionary Styles = (ResourceDictionary)XamlReader.Parse(StyleXaml);

    public static Style Get(string key) => (Style)Styles[key];

    static Brush Solid(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static Border Separator() => new()
    {
        Width = 1,
        Height = 22,
        Margin = new Thickness(6, 0, 6, 0),
        Background = Ring,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static Border Wrap(Window window, UIElement body)
    {
        window.Background = Panel;
        window.Foreground = Brushes.White;
        var chrome = new WindowChrome
        {
            CaptionHeight = 0,
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(6),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        };

        var maximize = Button("", "Maximize");
        maximize.Click += (_, _) => Toggle(window);
        var minimize = Button("", "Minimize");
        minimize.Click += (_, _) => window.WindowState = WindowState.Minimized;
        var close = Button("", "Close");
        close.Click += (_, _) => window.Close();

        foreach (var button in new[] { minimize, maximize, close })
        {
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(minimize);
        buttons.Children.Add(maximize);
        buttons.Children.Add(close);

        var logo = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/integrations-wordmark.png")),
            Height = 18,
            Margin = new Thickness(2, 2, 2, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Presenter Tools by integrations.at",
        };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);

        var header = new Grid { Margin = new Thickness(6, 6, 6, 8), Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
        header.Children.Add(logo);
        header.Children.Add(buttons);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(body, 1);
        grid.Children.Add(header);
        grid.Children.Add(body);

        var root = new Border { Background = Panel, Child = grid };
        var custom = false;
        var inMove = false;
        var reasserting = false;

        void Caption()
        {
            if (custom && header.IsLoaded && header.ActualHeight > 0)
            {
                var bottom = root.Padding.Top + header.Margin.Top + header.ActualHeight + header.Margin.Bottom;
                if (Math.Abs(chrome.CaptionHeight - bottom) > 0.5)
                {
                    chrome.CaptionHeight = bottom;
                }
            }
        }

        void Fit()
        {
            var padding = custom && window.WindowState == WindowState.Maximized ? Overhang(window) : new Thickness(0);
            if (root.Padding != padding)
            {
                root.Padding = padding;
            }

            Caption();
        }

        void FitLater() => window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Fit);

        void Reassert()
        {
            if (custom && window.WindowState == WindowState.Maximized && !Covers(window))
            {
                reasserting = true;
                window.WindowState = WindowState.Normal;
                window.WindowState = WindowState.Maximized;
                reasserting = false;
                Fit();
            }
        }

        void Apply()
        {
            var maximized = window.WindowState == WindowState.Maximized;
            if (window.WindowState == WindowState.Minimized)
            {
                return;
            }

            if (maximized != custom)
            {
                custom = maximized;
                if (custom)
                {
                    window.WindowStyle = WindowStyle.None;
                    WindowChrome.SetWindowChrome(window, chrome);
                    header.Visibility = Visibility.Visible;
                    window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Reassert);
                }
                else
                {
                    WindowChrome.SetWindowChrome(window, null);
                    window.WindowStyle = WindowStyle.SingleBorderWindow;
                    header.Visibility = Visibility.Collapsed;
                }
            }

            maximize.Content = maximized ? "" : "";
            maximize.ToolTip = maximized ? "Restore" : "Maximize";
            Fit();
            FitLater();
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmEnterSizeMove)
            {
                inMove = true;
            }
            else if (msg == WmExitSizeMove)
            {
                inMove = false;
                window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Apply);
            }

            return IntPtr.Zero;
        }

        window.SourceInitialized += (_, _) =>
        {
            if (System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(window).Handle) is { } source)
            {
                source.AddHook(Hook);
            }
        };
        header.SizeChanged += (_, _) => Fit();
        header.Loaded += (_, _) => Fit();
        window.SizeChanged += (_, _) => Fit();
        window.LocationChanged += (_, _) => Fit();
        window.DpiChanged += (_, _) =>
        {
            Fit();
            FitLater();
        };
        window.StateChanged += (_, _) =>
        {
            if (!inMove && !reasserting)
            {
                Apply();
            }
        };
        return root;
    }

    const int WmEnterSizeMove = 0x0231;
    const int WmExitSizeMove = 0x0232;

    static bool Covers(Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var info = new Native.MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (handle == IntPtr.Zero || !Native.GetMonitorInfo(Native.MonitorFromWindow(handle, Native.MONITOR_DEFAULTTONEAREST), ref info) || !Native.GetWindowRect(handle, out var rect))
        {
            return true;
        }

        return rect.Left <= info.Work.Left && rect.Top <= info.Work.Top && rect.Right >= info.Work.Right && rect.Bottom >= info.Work.Bottom;
    }

    static Thickness Overhang(Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return SystemParameters.WindowResizeBorderThickness;
        }

        var info = new Native.MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromWindow(handle, Native.MONITOR_DEFAULTTONEAREST), ref info) || !Native.GetWindowRect(handle, out var rect))
        {
            return SystemParameters.WindowResizeBorderThickness;
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        return new Thickness(
            Math.Max(0, info.Work.Left - rect.Left) / dpi.DpiScaleX,
            Math.Max(0, info.Work.Top - rect.Top) / dpi.DpiScaleY,
            Math.Max(0, rect.Right - info.Work.Right) / dpi.DpiScaleX,
            Math.Max(0, rect.Bottom - info.Work.Bottom) / dpi.DpiScaleY);
    }

    static void Toggle(Window window) =>
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    static Button Button(string glyph, string tip) => new()
    {
        Style = Get("Chip"),
        Margin = new Thickness(3, 0, 0, 0),
        Padding = new Thickness(6, 2, 6, 2),
        FontFamily = Icons,
        FontSize = 10,
        Content = glyph,
        ToolTip = tip,
    };

    const string StyleXaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Style x:Key="Chip" TargetType="Button">
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="Margin" Value="3" />
    <Setter Property="Padding" Value="10,4" />
    <Setter Property="VerticalAlignment" Value="Center" />
    <Setter Property="Foreground" Value="White" />
    <Setter Property="Background" Value="#FF444444" />
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="BorderThickness" Value="2" />
    <Setter Property="Focusable" Value="False" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="4" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="ToolChip" TargetType="RadioButton">
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="Margin" Value="3" />
    <Setter Property="Padding" Value="10,4" />
    <Setter Property="VerticalAlignment" Value="Center" />
    <Setter Property="Foreground" Value="White" />
    <Setter Property="Background" Value="#FF444444" />
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="BorderThickness" Value="2" />
    <Setter Property="Focusable" Value="False" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RadioButton">
          <Border x:Name="Box" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="4" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Box" Property="Background" Value="#FF0B3A5E" />
              <Setter TargetName="Box" Property="BorderBrush" Value="#FF0099FF" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="Swatch" TargetType="RadioButton">
    <Setter Property="Margin" Value="3" />
    <Setter Property="Width" Value="26" />
    <Setter Property="Height" Value="26" />
    <Setter Property="VerticalAlignment" Value="Center" />
    <Setter Property="BorderBrush" Value="#FF777777" />
    <Setter Property="BorderThickness" Value="2" />
    <Setter Property="Focusable" Value="False" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RadioButton">
          <Border x:Name="Ring" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="13" />
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Ring" Property="BorderBrush" Value="#FF0099FF" />
              <Setter TargetName="Ring" Property="BorderThickness" Value="4" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="ComboItem" TargetType="ComboBoxItem">
    <Setter Property="Foreground" Value="White" />
    <Setter Property="Padding" Value="8,3" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBoxItem">
          <Border x:Name="Row" Background="Transparent" Padding="{TemplateBinding Padding}">
            <ContentPresenter />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="Row" Property="Background" Value="#FF0B3A5E" />
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="Row" Property="Background" Value="#FF0B3A5E" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="Combo" TargetType="ComboBox">
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="Margin" Value="3" />
    <Setter Property="VerticalAlignment" Value="Center" />
    <Setter Property="Foreground" Value="White" />
    <Setter Property="Focusable" Value="False" />
    <Setter Property="ItemContainerStyle" Value="{StaticResource ComboItem}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBox">
          <Grid>
            <ToggleButton Focusable="False" ClickMode="Press"
                          IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
              <ToggleButton.Template>
                <ControlTemplate TargetType="ToggleButton">
                  <Border Background="#FF444444" BorderBrush="Transparent" BorderThickness="2" CornerRadius="4">
                    <Path Data="M 0 0 L 4 4 L 8 0 Z" Fill="White" HorizontalAlignment="Right"
                          VerticalAlignment="Center" Margin="0,0,8,0" />
                  </Border>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter IsHitTestVisible="False" Margin="10,4,26,4" VerticalAlignment="Center"
                              HorizontalAlignment="Left" Content="{TemplateBinding SelectionBoxItem}"
                              ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" />
            <Popup IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" Focusable="False" PopupAnimation="Fade">
              <Border Background="#FF2D3436" BorderBrush="#FF777777" BorderThickness="1" CornerRadius="4"
                      MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}">
                <ScrollViewer MaxHeight="{TemplateBinding MaxDropDownHeight}">
                  <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained" />
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="BlueScrollBar" TargetType="ScrollBar">
    <Setter Property="Width" Value="10" />
    <Setter Property="MinWidth" Value="10" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ScrollBar">
          <Border Background="#FF3A3F41" CornerRadius="5" Margin="2,6">
            <Track x:Name="PART_Track" IsDirectionReversed="True">
              <Track.DecreaseRepeatButton>
                <RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False" />
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False" />
              </Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb Cursor="Hand">
                  <Thumb.Template>
                    <ControlTemplate TargetType="Thumb">
                      <Border Background="#FF0099FF" CornerRadius="4" MinHeight="16" />
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>
""";
}
