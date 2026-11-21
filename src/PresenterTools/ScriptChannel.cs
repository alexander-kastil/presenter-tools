using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows.Threading;

namespace PresenterTools;

public sealed record ScriptMessage(string Folder, string[] Args);

// A second PresenterTools.exe started with --cmd sends its arguments to the running instance over a
// named pipe of the user's session and prints the one-line reply: ok, or error=<reason>.
public sealed class ScriptChannel : IDisposable
{
    private const int ConnectTimeoutMs = 3000;
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(15);

    private readonly CancellationTokenSource _stop = new();

    public ScriptChannel(Dispatcher dispatcher, Func<ScriptMessage, string> handle, string? pipeName = null)
    {
        var name = pipeName ?? PipeName;
        _ = Task.Run(() => ServeAsync(name, dispatcher, handle, _stop.Token));
    }

    public static string PipeName { get; } = $"PresenterTools-{Process.GetCurrentProcess().SessionId}";

    public static bool IsCommand(IReadOnlyList<string> args) => args.Contains(ScriptCommand.Flag);

    public static int Run(string[] args)
    {
        var reply = Task.Run(() => SendAsync(new ScriptMessage(Environment.CurrentDirectory, args), PipeName)).GetAwaiter().GetResult();
        Console.Out.WriteLine(reply);
        Console.Out.Flush();
        return ScriptCommand.IsOk(reply) ? 0 : 1;
    }

    public static async Task<string> SendAsync(ScriptMessage message, string pipeName)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(ConnectTimeoutMs);
        }
        catch (TimeoutException)
        {
            return "error=not running";
        }
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(message));
        using var wait = new CancellationTokenSource(ReplyTimeout);
        try
        {
            return await reader.ReadLineAsync(wait.Token) ?? "error=no reply";
        }
        catch (OperationCanceledException)
        {
            return "error=no reply";
        }
    }

    private static async Task ServeAsync(string name, Dispatcher dispatcher, Func<ScriptMessage, string> handle, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var reply = await ReplyAsync(await reader.ReadLineAsync(stop), dispatcher, handle);
                await writer.WriteLineAsync(reply);
                pipe.WaitForPipeDrain();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                await Task.Delay(200, CancellationToken.None);
            }
        }
    }

    private static async Task<string> ReplyAsync(string? line, Dispatcher dispatcher, Func<ScriptMessage, string> handle)
    {
        try
        {
            var message = line is null ? null : JsonSerializer.Deserialize<ScriptMessage>(line);
            if (message?.Args is null || message.Folder is null)
            {
                return "error=empty request";
            }
            return await dispatcher.InvokeAsync(() => handle(message));
        }
        catch (Exception ex)
        {
            return "error=" + ex.Message.ReplaceLineEndings(" ");
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
