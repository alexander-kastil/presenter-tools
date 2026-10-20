using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace PresenterTools.Tests;

// One STA thread with one Application for the whole test run: WPF allows a single Application per process.
internal static class Sta
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(nint context);

    private static readonly Lazy<Dispatcher> Ui = new(Start);

    private static Dispatcher Start()
    {
        var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        var thread = new Thread(() =>
        {
            // The app runs PerMonitorV2, so the test process does too: window rectangles are physical pixels.
            SetProcessDpiAwarenessContext(-4);
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    public static void Run(Func<Task> body) => Ui.Value.InvokeAsync(body).Task.Unwrap().GetAwaiter().GetResult();

    public static void Run(Action body) => Ui.Value.Invoke(body);

    public static async Task Settle(int milliseconds = 600)
    {
        await Task.Delay(milliseconds);
        await Ui.Value.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
