using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;

namespace PresenterTools;

public enum ReceiverState
{
    Stopped,
    Starting,
    Running,
    Failed
}

// Runs the bundled UxPlay AirPlay receiver (engine\uxplay.exe) as a child process and restarts it if it exits.
public sealed partial class Receiver : IDisposable
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(3);

    private readonly KillOnCloseJob _job = new();
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;
    private readonly DispatcherTimer _restartTimer;
    private readonly Lock _logLock = new();
    private Process? _process;
    private StreamWriter? _log;
    private CancellationTokenSource? _titleCts;
    private bool _wanted;

    public Receiver()
    {
        _restartTimer = new DispatcherTimer { Interval = RestartDelay };
        _restartTimer.Tick += (_, _) =>
        {
            _restartTimer.Stop();
            if (_wanted)
            {
                Launch();
            }
        };
    }

    public event EventHandler? Changed;

    public ReceiverState State { get; private set; } = ReceiverState.Stopped;

    public string? LastClient { get; private set; }

    public string? Error { get; private set; }

    public static string EngineFolder => Path.Combine(AppContext.BaseDirectory, "engine");

    public static string LogPath => Path.Combine(AppSettings.DataFolder, "uxplay.log");

    public void Start()
    {
        _wanted = true;
        if (_process is null)
        {
            Launch();
        }
    }

    public void Stop()
    {
        _wanted = false;
        _restartTimer.Stop();
        Kill();
        SetState(ReceiverState.Stopped, null);
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    private void Launch()
    {
        var exe = Path.Combine(EngineFolder, "uxplay.exe");
        if (!File.Exists(exe))
        {
            SetState(ReceiverState.Failed, $"The AirPlay engine is missing: {exe}");
            return;
        }

        Directory.CreateDirectory(AppSettings.DataFolder);
        var settings = AppSettings.LoadReceiver();
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = EngineFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in BuildArguments(settings))
        {
            info.ArgumentList.Add(arg);
        }
        info.Environment["PATH"] = EngineFolder + ";" + Environment.GetEnvironmentVariable("PATH");
        info.Environment["GST_PLUGIN_SYSTEM_PATH"] = Path.Combine(EngineFolder, "lib", "gstreamer-1.0");
        info.Environment["GST_PLUGIN_PATH"] = "";
        info.Environment["GST_PLUGIN_SCANNER"] = Path.Combine(EngineFolder, "libexec", "gstreamer-1.0", "gst-plugin-scanner.exe");
        info.Environment["GST_REGISTRY"] = Path.Combine(AppSettings.DataFolder, "gst-registry.bin");

        lock (_logLock)
        {
            _log?.Dispose();
            _log = new StreamWriter(LogPath, append: false) { AutoFlush = true };
        }
        var shown = info.ArgumentList.Select(a => settings.Access == Access.Password && a == settings.Password ? "********" : a);
        WriteLog($"{DateTime.Now:s} starting {exe} {string.Join(' ', shown)}");

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => OnOutput(e.Data);
        process.ErrorDataReceived += (_, e) => OnOutput(e.Data);
        process.Exited += (_, _) => _dispatcher.BeginInvoke(() => OnExited(process));
        try
        {
            process.Start();
            _job.Assign(process.Handle);
        }
        catch (Exception ex)
        {
            process.Dispose();
            SetState(ReceiverState.Failed, ex.Message);
            return;
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;
        SetState(ReceiverState.Starting, null);
    }

    private static List<string> BuildArguments(ReceiverSettings settings)
    {
        var args = new List<string> { "-n", settings.Name, "-nh" };
        args.AddRange(["-key", Path.Combine(AppSettings.DataFolder, "uxplay.pem")]);
        switch (settings.Access)
        {
            case Access.Pin:
                args.Add("-pin" + settings.Pin);
                args.AddRange(["-reg", Path.Combine(AppSettings.DataFolder, "uxplay.register")]);
                break;
            case Access.Password when !string.IsNullOrEmpty(settings.Password):
                args.AddRange(["-pw", settings.Password]);
                break;
        }
        if (settings.FullScreen)
        {
            args.Add("-fs");
        }
        return args;
    }

    private void WriteLog(string line)
    {
        lock (_logLock)
        {
            _log?.WriteLine(line);
        }
    }

    private void OnOutput(string? line)
    {
        if (line is null)
        {
            return;
        }
        WriteLog(line);
        if (line.Contains("Initialized server socket", StringComparison.Ordinal))
        {
            _dispatcher.BeginInvoke(() => SetState(ReceiverState.Running, null));
        }
        var match = ClientPattern().Match(line);
        if (match.Success)
        {
            var deviceName = match.Groups["name"].Value;
            var client = $"{deviceName} ({match.Groups["model"].Value})";
            _dispatcher.BeginInvoke(() =>
            {
                RetitleWindow(deviceName);
                LastClient = $"{client}, {DateTime.Now:t}";
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    private void RetitleWindow(string title)
    {
        var process = _process;
        _titleCts?.Cancel();
        _titleCts?.Dispose();
        _titleCts = null;
        if (process is null)
        {
            return;
        }
        var cts = new CancellationTokenSource();
        _titleCts = cts;
        var id = process.Id;
        _ = MirrorTitle.ApplyAsync(id, title, () => ReferenceEquals(process, _process) && !process.HasExited, cts.Token);
    }

    private void OnExited(Process process)
    {
        if (!ReferenceEquals(process, _process))
        {
            return;
        }
        var code = process.ExitCode;
        process.Dispose();
        _process = null;
        WriteLog($"{DateTime.Now:s} uxplay.exe exited with code {code}");
        if (_wanted)
        {
            SetState(ReceiverState.Failed, $"The AirPlay engine stopped (exit code {code}). Restarting...");
            _restartTimer.Start();
        }
    }

    private void Kill()
    {
        _titleCts?.Cancel();
        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        process.Dispose();
    }

    private void SetState(ReceiverState state, string? error)
    {
        State = state;
        Error = error;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _wanted = false;
        _restartTimer.Stop();
        Kill();
        lock (_logLock)
        {
            _log?.Dispose();
            _log = null;
        }
        _job.Dispose();
    }

    [GeneratedRegex(@"connection request from (?<name>.+) \((?<model>[^)]*)\) with deviceID")]
    private static partial Regex ClientPattern();
}
