using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace PresenterTools;

public static class McpBridge
{
    public const string Flag = "--mcp";

    public static bool IsBridge(IReadOnlyList<string> args) => args.Contains(Flag);

    public static int Run()
    {
        using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        Task.Run(() => RunAsync(input, output, McpServer.DefaultPort)).GetAwaiter().GetResult();
        return 0;
    }

    public static async Task RunAsync(TextReader input, TextWriter output, int port)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var url = $"http://127.0.0.1:{port}/mcp";
        while (await input.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var reply = await ForwardAsync(http, url, line);
            if (reply is not null)
            {
                await output.WriteLineAsync(reply);
            }
        }
    }

    private static async Task<string?> ForwardAsync(HttpClient http, string url, string line)
    {
        JsonNode? id = null;
        try
        {
            id = (JsonNode.Parse(line) as JsonObject)?["id"]?.DeepClone();
            using var response = await http.PostAsync(url, new StringContent(line, Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(body) ? null : body.ReplaceLineEndings(" ");
        }
        catch (System.Text.Json.JsonException)
        {
            return Error(null, -32700, "parse error");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return id is null ? null : Error(id, -32000, "Presenter Tools is not running");
        }
    }

    private static string Error(JsonNode? id, int code, string message) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        }.ToJsonString();
}
