using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Threading;

namespace PresenterTools;

// MCP over Streamable HTTP on 127.0.0.1: POST /mcp with one JSON-RPC message, answered as application/json.
public sealed class McpServer : IDisposable
{
    public const int DefaultPort = 47615;
    private const string Latest = "2025-06-18";
    private static readonly string[] Versions = [Latest, "2025-03-26", "2024-11-05"];

    private readonly TcpListener _listener;
    private readonly Dispatcher _dispatcher;
    private readonly Func<Mirror?> _mirror;
    private readonly CancellationTokenSource _stop = new();

    public McpServer(Dispatcher dispatcher, Func<Mirror?> mirror, int port = DefaultPort)
    {
        _dispatcher = dispatcher;
        _mirror = mirror;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public static McpServer? TryStart(Dispatcher dispatcher, Func<Mirror?> mirror)
    {
        try
        {
            return new McpServer(dispatcher, mirror);
        }
        catch (SocketException ex)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataFolder);
                File.AppendAllText(Path.Combine(AppSettings.DataFolder, "mcp.log"), $"{DateTime.Now:s} MCP server not started on 127.0.0.1:{DefaultPort}: {ex.Message}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
            return null;
        }
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => ServeAsync(client));
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        client.NoDelay = true;
        try
        {
            await using var stream = new BufferedStream(client.GetStream());
            while (!_stop.IsCancellationRequested && await ReadRequestAsync(stream) is { } request)
            {
                var (status, body) = await HandleAsync(request);
                var head = $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nAllow: POST\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                await stream.WriteAsync(body);
                await stream.FlushAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
        {
        }
    }

    private async Task<(string Method, string Path, Dictionary<string, string> Headers, byte[] Body)?> ReadRequestAsync(Stream stream)
    {
        if (await ReadLineAsync(stream) is not { Length: > 0 } line)
        {
            return null;
        }
        var parts = line.Split(' ');
        if (parts.Length < 2)
        {
            throw new InvalidDataException();
        }
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await ReadLineAsync(stream) is { Length: > 0 } header)
        {
            var colon = header.IndexOf(':');
            if (colon > 0)
            {
                headers[header[..colon]] = header[(colon + 1)..].Trim();
            }
        }
        var length = headers.TryGetValue("Content-Length", out var value) && int.TryParse(value, out var n) ? n : 0;
        if (length is < 0 or > 1 << 20)
        {
            throw new InvalidDataException();
        }
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, _stop.Token);
        return (parts[0], parts[1], headers, body);
    }

    private async Task<string?> ReadLineAsync(Stream stream)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (await stream.ReadAsync(one, _stop.Token) == 1)
        {
            if (one[0] == '\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
            }
            bytes.Add(one[0]);
            if (bytes.Count > 8192)
            {
                throw new InvalidDataException();
            }
        }
        return null;
    }

    private async Task<(string Status, byte[] Body)> HandleAsync((string Method, string Path, Dictionary<string, string> Headers, byte[] Body) request)
    {
        if (request.Path != "/mcp")
        {
            return ("404 Not Found", []);
        }
        if (request.Headers.TryGetValue("Origin", out var origin) && !(Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback))
        {
            return ("403 Forbidden", []);
        }
        if (request.Method != "POST")
        {
            return ("405 Method Not Allowed", []);
        }
        JsonNode? message;
        try
        {
            message = JsonNode.Parse(request.Body);
        }
        catch (System.Text.Json.JsonException)
        {
            return ("400 Bad Request", Reply(null, error: Error(-32700, "parse error")));
        }
        if (message is not JsonObject call || call["method"]?.GetValue<string>() is not { } method)
        {
            return ("400 Bad Request", Reply(null, error: Error(-32600, "invalid request")));
        }
        if (call["id"] is not { } id)
        {
            return ("202 Accepted", []);
        }
        var parameters = call["params"] as JsonObject;
        return method switch
        {
            "initialize" => ("200 OK", Reply(id, Initialize(parameters))),
            "ping" => ("200 OK", Reply(id, new JsonObject())),
            "tools/list" => ("200 OK", Reply(id, Tools())),
            "tools/call" => ("200 OK", Reply(id, await CallAsync(parameters))),
            _ => ("200 OK", Reply(id, error: Error(-32601, $"method not found: {method}")))
        };
    }

    private static JsonObject Initialize(JsonObject? parameters)
    {
        var asked = parameters?["protocolVersion"]?.GetValue<string>();
        return new JsonObject
        {
            ["protocolVersion"] = asked is not null && Versions.Contains(asked) ? asked : Latest,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject { ["name"] = "presenter-tools", ["version"] = typeof(McpServer).Assembly.GetName().Version?.ToString(3) }
        };
    }

    private static JsonObject Tools() => new()
    {
        ["tools"] = new JsonArray
        {
            Tool("mirror_status", "Device, size, screen rectangle and name of the AirPlay mirror shown by Presenter Tools, or \"not mirrored\"."),
            Tool("mirror_capture", "The AirPlay mirror picture as a PNG image; an error when nothing is mirrored.")
        }
    };

    private static JsonObject Tool(string name, string description) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
        ["annotations"] = new JsonObject { ["readOnlyHint"] = true }
    };

    private async Task<JsonObject> CallAsync(JsonObject? parameters)
    {
        switch (parameters?["name"]?.GetValue<string>())
        {
            case "mirror_status":
                var status = await _dispatcher.InvokeAsync(_mirror);
                return Text(status is { } m ? ScriptCommand.MirrorLine(m) : "not mirrored", isError: false);
            case "mirror_capture":
                var png = await _dispatcher.InvokeAsync(() => _mirror() is { } m ? ScriptCommand.Png([m.Left, m.Top, m.Width, m.Height]) : null);
                return png is null
                    ? Text("not mirrored", isError: true)
                    : new JsonObject
                    {
                        ["content"] = new JsonArray(new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(png), ["mimeType"] = "image/png" }),
                        ["isError"] = false
                    };
            default:
                return Text("unknown tool", isError: true);
        }
    }

    private static JsonObject Text(string text, bool isError) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError
    };

    private static JsonObject Error(int code, string message) => new() { ["code"] = code, ["message"] = message };

    private static byte[] Reply(JsonNode? id, JsonObject? result = null, JsonObject? error = null)
    {
        var reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone() };
        if (error is null)
        {
            reply["result"] = result;
        }
        else
        {
            reply["error"] = error;
        }
        return Encoding.UTF8.GetBytes(reply.ToJsonString());
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}
