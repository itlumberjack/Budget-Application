using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BudgetApp.Models;

namespace BudgetApp.Services;

/// <summary>
/// Asks Claude how to spread a month's unpaid bills across its paydays so no paycheck runs short
/// and bills are paid before they're due. It only suggests; nothing changes until the user applies it.
/// Needs Anthropic:ApiKey in configuration (user secrets or the Anthropic__ApiKey environment variable).
/// </summary>
public class BudgetAdvisor(HttpClient http, IConfiguration config, ILogger<BudgetAdvisor> logger)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["Anthropic:ApiKey"]);

    private const string SystemPrompt = """
        You help a household arrange a monthly budget. The month is split into columns, one per payday
        (paydays a few days apart share a column). Each bill comes out of one column's money.

        Suggest moving bills between columns so that:
        1. No column's spending is more than its income, if that's possible at all.
        2. Each bill comes out of a payday on or before its due day when one exists. A bill due before the
           first payday belongs in the first column.
        3. Money left over is spread reasonably evenly, so no single paycheck is stretched thin.
        4. As few bills as possible move. If the month already works, suggest no moves.

        Only move bills whose status is NotPaid and that are marked flexible; fixed bills stay where they are.
        Never move a bill to a column whose first payday is after the bill's due day (the first column is the
        exception, for bills due before any payday). Never move a bill to the column it's already in.
        Give each move a short, plain reason (one sentence, no jargon) that mentions the due day or the
        shortfall it fixes. The summary is one or two friendly sentences about how the month looks.
        """;

    // The answer comes back as JSON matching this schema (structured outputs).
    private static readonly JsonObject AnswerSchema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["summary"] = new JsonObject { ["type"] = "string" },
            ["moves"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["bill_id"] = new JsonObject { ["type"] = "integer" },
                        ["to_column"] = new JsonObject { ["type"] = "integer", ["description"] = "Column number to move the bill to" },
                        ["reason"] = new JsonObject { ["type"] = "string" }
                    },
                    ["required"] = new JsonArray("bill_id", "to_column", "reason"),
                    ["additionalProperties"] = false
                }
            }
        },
        ["required"] = new JsonArray("summary", "moves"),
        ["additionalProperties"] = false
    };

    public async Task<ArrangementSuggestion> SuggestAsync(MonthViewModel month, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new AdvisorException("The AI helper isn't set up yet. Add your Anthropic API key as Anthropic:ApiKey (see the README).");

        var columns = month.Columns.Where(c => !c.IsUnassigned).ToList();
        if (columns.Count == 0)
            throw new AdvisorException("This month has no paydays yet, so there's nothing to arrange.");
        var expenses = month.Budget!.Expenses;

        var request = new JsonObject
        {
            ["model"] = config["Anthropic:Model"] ?? "claude-opus-5-5",
            ["max_tokens"] = 16000,
            ["system"] = SystemPrompt,
            ["output_config"] = new JsonObject
            {
                ["effort"] = "medium",
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = AnswerSchema.DeepClone() }
            },
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = DescribeMonth(month, columns)
            })
        };

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
        JsonNode? input = null;
        try { input = text is null ? null : JsonNode.Parse(text); } catch (JsonException) { }
        if (input is null)
        {
            logger.LogWarning("Unexpected Anthropic API reply: {Body}", body);
            throw new AdvisorException("The AI didn't send back a suggestion. Try again.");
        }

        // Keep only moves that make sense for this month: an unpaid bill, a real column, a different one.
        var moves = new List<SuggestedMove>();
        var seen = new HashSet<int>();
        foreach (var m in input["moves"]?.AsArray() ?? [])
        {
            if (m?["bill_id"]?.GetValueKind() != JsonValueKind.Number || m["to_column"]?.GetValueKind() != JsonValueKind.Number) continue;
            var expense = expenses.FirstOrDefault(e => e.Id == (int)m["bill_id"]!);
            var to = (int)m["to_column"]! - 1;
            if (expense is null || expense.Status != PaymentStatus.NotPaid || expense.IsFixed || to < 0 || to >= columns.Count
                || !month.CanMoveTo(columns[to], expense) || !seen.Add(expense.Id)) continue;
            var from = columns.FirstOrDefault(c => c.Expenses.Contains(expense));
            if (from == columns[to]) continue;
            moves.Add(new SuggestedMove(expense.Id, expense.Title, expense.Amount, expense.DueDay,
                from?.Heading ?? "Not assigned", columns[to].Heading, columns[to].TargetPayPeriodId!.Value,
                (string?)m["reason"] ?? ""));
        }

        return new ArrangementSuggestion((string?)input["summary"] ?? "", moves, ColumnOutcome.For(columns, moves));
    }

    /// <summary>The "message" from an API error body, if there is one.</summary>
    private static string? ApiErrorMessage(string body)
    {
        try { return (string?)JsonNode.Parse(body)?["error"]?["message"]; }
        catch (JsonException) { return null; }
    }

    /// <summary>The month as plain text for the prompt.</summary>
    private static string DescribeMonth(MonthViewModel month, List<PayColumn> columns)
    {
        var us = CultureInfo.GetCultureInfo("en-US");
        var sb = new StringBuilder();
        sb.AppendLine($"Budget for {month.Title}.");
        sb.AppendLine();
        sb.AppendLine("Columns:");
        for (var i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            var pays = string.Join(", ", c.Paychecks.Select(p => $"{p.Label} on {p.PayDate:MMM d} for {p.Total.ToString("C", us)}"));
            sb.AppendLine($"Column {i + 1} ({c.Heading}): income {c.Income.ToString("C", us)} from {pays}; " +
                          $"spending {c.Spending.ToString("C", us)}; left over {c.LeftOver.ToString("C", us)}.");
        }
        sb.AppendLine();
        sb.AppendLine("Bills (id | title | category | amount | due day | status | fixed or flexible | current column):");
        foreach (var e in month.Budget!.Expenses.OrderBy(e => e.DueDay ?? 32).ThenBy(e => e.Title))
        {
            var col = columns.FindIndex(c => c.Expenses.Contains(e));
            var category = month.Categories.FirstOrDefault(c => c.Id == e.CategoryId)?.Name ?? "Other";
            sb.AppendLine($"{e.Id} | {e.Title} | {category} | {e.Amount.ToString("C", us)} | " +
                          $"{(e.DueDay is int d ? "day " + d : "no due day")} | {e.Status} | {(e.IsFixed ? "fixed" : "flexible")} | " +
                          $"{(col >= 0 ? "column " + (col + 1) : "not assigned")}");
        }
        return sb.ToString();
    }
}
