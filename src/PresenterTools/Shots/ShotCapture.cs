namespace PresenterTools.Shots;

internal static class ShotCapture
{
    static bool capturing;

    public static async Task Run(int delayMs)
    {
        if (capturing)
        {
            return;
        }

        capturing = true;
        var restore = App.Instance.Session?.HideForCapture();
        try
        {
            await App.Instance.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            await Task.Delay(delayMs > 0 ? delayMs : 80);

            var (shot, x, y) = ScreenGrabber.Grab();
            var overlay = new OverlayWindow(shot, x, y);
            overlay.ShowDialog();
            restore?.Invoke();
            restore = null;
            if (overlay.Result is { } cropped)
            {
                LibraryWindow.ShowOrActivate(LibraryStore.Add(cropped).Id);
            }
        }
        finally
        {
            restore?.Invoke();
            capturing = false;
        }
    }
}
