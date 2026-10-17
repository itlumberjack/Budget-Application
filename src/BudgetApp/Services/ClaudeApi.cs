using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BudgetApp.Services;

/// <summary>
/// Sends one request to the Anthropic Messages API and returns the JSON answer (the requests use structured
/// outputs, so the reply's text is JSON). Problems come back as an <see cref="AdvisorException"/> with a message
/// that can be shown as is. Needs Anthropic:ApiKey (user secrets or the Anthropic__ApiKey environment variable).
/// </summary>
public class ClaudeApi(HttpClient http, IConfiguration config, ILogger<ClaudeApi> logger)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["Anthropic:ApiKey"]);

    public string Model => config["Anthropic:Model"] ?? "claude-opus-5-5";

    /// <summary>A request with a system prompt, one user message and an answer that follows the schema.</summary>
    public JsonObject NewRequest(string system, JsonNode userContent, JsonObject schema, string effort = "medium") => new()
    {
        ["model"] = Model,
        ["max_tokens"] = 16000,
        ["system"] = system,
        ["output_config"] = new JsonObject
        {
            ["effort"] = effort,
            ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = schema.DeepClone() }
        },
        ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = userContent })
    };

    public async Task<JsonNode> AskAsync(JsonObject request, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new AdvisorException("The AI helper isn't set up yet. Add your Anthropic API key as Anthropic:ApiKey (see the README).");

        var baseUrl = (config["Anthropic:BaseUrl"] ?? "https://api.anthropic.com").TrimEnd('/');
        using var message = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/v1/messages")
        {
            Content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json")
        };
        message.Headers.Add("x-api-key", config["Anthropic:ApiKey"]);
        message.Headers.Add("anthropic-version", "2023-06-01");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not reach the Anthropic API");
            throw new AdvisorException("Couldn't reach the AI service. Check the internet connection and try again.");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Anthropic API returned {Status}: {Body}", (int)response.StatusCode, body);
            var detail = ApiErrorMessage(body);
            throw new AdvisorException((int)response.StatusCode switch
            {
                401 or 403 => "The AI service didn't accept the API key. Check Anthropic:ApiKey.",
                413 => "That file is too big for the AI service. Try a smaller file or a photo of the summary page.",
                429 => "The AI service is busy or the account is out of credit. Try again in a minute.",
                _ => "The AI service turned the request down" + (detail is null ? "." : ": " + detail)
            });
        }

        var reply = JsonNode.Parse(body);
        var stopReason = (string?)reply?["stop_reason"];
        if (stopReason == "refusal")
            throw new AdvisorException("The AI declined to answer this one. Try again.");
        if (stopReason == "max_tokens")
            throw new AdvisorException("The AI's answer was cut off. Try again.");

        // Thinking blocks may come first; the answer is the text block.
        var text = reply?["content"]?.AsArray()
            .Where(c => (string?)c?["type"] == "text")
            .Select(c => (string?)c?["text"])
            .LastOrDefault();
        JsonNode? answer = null;
        try { answer = text is null ? null : JsonNode.Parse(text); } catch (JsonException) { }
        if (answer is null)
        {
            logger.LogWarning("Unexpected Anthropic API reply: {Body}", body);
            throw new AdvisorException("The AI didn't send back an answer. Try again.");
        }
        return answer;
    }

    /// <summary>The "message" from an API error body, if there is one.</summary>
    private static string? ApiErrorMessage(string body)
    {
        try { return (string?)JsonNode.Parse(body)?["error"]?["message"]; }
        catch (JsonException) { return null; }
    }
}
