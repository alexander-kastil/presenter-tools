using System.Net.Http;
using System.IO;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace PresenterTools;

public static class AiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private const string Prompt = """
You clean up hand-drawn screen annotations. The image shows dark strokes on a white background, drawn with a mouse or pen.
Decide what it is and answer with ONE JSON object only, no prose:
- Handwritten text: {"kind":"text","text":"<the text exactly as written, keep line breaks as \n>"}
- A recognizable drawing (shapes, arrows, icons, diagrams, simple objects): {"kind":"drawing","label":"<what it is>","shapes":[...]} redrawn cleanly as a few geometric primitives. Coordinates use a 0 to 1000 grid over the image: x and y are integers from 0 to 1000, origin top-left. Allowed shapes:
  {"type":"line","points":[[x1,y1],[x2,y2]]}
  {"type":"arrow","points":[[tailx,taily],[headx,heady]]}
  {"type":"polyline","points":[[x,y],...],"closed":true|false}
  {"type":"rectangle","x":..,"y":..,"width":..,"height":..}
  {"type":"ellipse","cx":..,"cy":..,"rx":..,"ry":..}
  Keep the same position, size and proportions as the original, straighten lines, close shapes, make symmetric parts symmetric.
- Neither: {"kind":"unknown"}
""";

    public static async Task<AiResult?> RecogniseAsync(byte[] png, ProviderSettings settings, string apiKey)
    {
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["temperature"] = 0.1,
            ["max_tokens"] = 1500,
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject { ["url"] = "data:image/png;base64," + Convert.ToBase64String(png) }
                        },
                        new JsonObject { ["type"] = "text", ["text"] = Prompt }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.BaseUrl.TrimEnd('/') + "/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var code = (int)response.StatusCode;
            throw new IOException(code is 401 or 403
                ? $"API key rejected ({code}). Check Settings."
                : $"API error {code}");
        }
        return ResponseParser.ParseResponse(await response.Content.ReadAsStringAsync());
    }
}
