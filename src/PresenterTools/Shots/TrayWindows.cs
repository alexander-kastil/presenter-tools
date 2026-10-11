using System.Windows;

namespace PresenterTools.Shots;

internal static class TrayWindows
{
    public static void Restore(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }
}
