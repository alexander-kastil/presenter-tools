using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using FluentAssertions;
using Xunit;

namespace PresenterTools.Tests;

public class McpServerTests
{
    private static async Task<JsonNode> PostAsync(int port, string json)
    {
        using var http = new HttpClient();
        var response = await http.PostAsync($"http://127.0.0.1:{port}/mcp", new StringContent(json, Encoding.UTF8, "application/json"));
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static void WithServer(Func<int, Task> body) => Sta.Run(async () =>
    {
        using var server = new McpServer(Dispatcher.CurrentDispatcher, () => null, 0);
        await body(server.Port);
    });

    [Fact]
    public void Initialize_answers_with_the_tools_capability()
    {
        WithServer(async port =>
        {
            var reply = await PostAsync(port, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""");

            reply["id"]!.GetValue<int>().Should().Be(1);
            reply["result"]!["protocolVersion"]!.GetValue<string>().Should().Be("2025-06-18");
            reply["result"]!["capabilities"]!["tools"].Should().NotBeNull();
            reply["result"]!["serverInfo"]!["name"]!.GetValue<string>().Should().Be("presenter-tools");
        });
    }

    [Fact]
    public void Tools_list_names_the_two_mirror_tools()
    {
        WithServer(async port =>
        {
            var reply = await PostAsync(port, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

            reply["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).Should().Equal("mirror_status", "mirror_capture");
        });
    }

    [Fact]
    public void Mirror_status_reports_not_mirrored()
    {
        WithServer(async port =>
        {
            var reply = await PostAsync(port, """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"mirror_status","arguments":{}}}""");

            reply["result"]!["isError"]!.GetValue<bool>().Should().BeFalse();
            reply["result"]!["content"]![0]!["text"]!.GetValue<string>().Should().Be("not mirrored");
        });
    }

    [Fact]
    public void Mirror_capture_is_an_error_result_when_not_mirrored()
    {
        WithServer(async port =>
        {
            var reply = await PostAsync(port, """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"mirror_capture"}}""");

            reply["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
        });
    }
}
