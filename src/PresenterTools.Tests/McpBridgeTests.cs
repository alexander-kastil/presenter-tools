using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using FluentAssertions;
using Xunit;

namespace PresenterTools.Tests;

public class McpBridgeTests
{
    private static async Task<string[]> BridgeAsync(int port, string input)
    {
        var output = new StringWriter();
        await McpBridge.RunAsync(new StringReader(input), output, port);
        return output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void Bridge_relays_initialize_and_tools_list_and_skips_notifications()
    {
        Sta.Run(async () =>
        {
            using var server = new McpServer(Dispatcher.CurrentDispatcher, () => null, 0);

            var lines = await BridgeAsync(server.Port, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}
                {"jsonrpc":"2.0","method":"notifications/initialized"}
                {"jsonrpc":"2.0","id":2,"method":"tools/list"}
                """.ReplaceLineEndings("\n"));

            lines.Should().HaveCount(2);
            JsonNode.Parse(lines[0])!["result"]!["serverInfo"]!["name"]!.GetValue<string>().Should().Be("presenter-tools");
            JsonNode.Parse(lines[1])!["result"]!["tools"]!.AsArray().Should().HaveCount(2);
        });
    }

    [Fact]
    public async Task Bridge_answers_every_request_with_an_error_when_the_app_is_not_running()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var lines = await BridgeAsync(port, "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/list\"}\n{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}\n");

        lines.Should().HaveCount(1);
        var reply = JsonNode.Parse(lines[0])!;
        reply["id"]!.GetValue<int>().Should().Be(7);
        reply["error"]!["message"]!.GetValue<string>().Should().Be("Presenter Tools is not running");
    }
}
