using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BudgetApp.Models;

namespace BudgetApp.Services;

/// <summary>A bill the advisor suggests moving to another payday column.</summary>
public record SuggestedMove(int ExpenseId, string Title, decimal Amount, int? DueDay,
    string From, string To, int ToPayPeriodId, string Reason);

/// <summary>Left over in one column before and after the suggested moves.</summary>
public record ColumnOutcome(string Heading, decimal Before, decimal After);

public record ArrangementSuggestion(string Summary, List<SuggestedMove> Moves, List<ColumnOutcome> Columns);

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

        Only move bills whose status is NotPaid. Never move a bill to the column it's already in.
        Give each move a short, plain reason (one sentence, no jargon) that mentions the due day or the
        shortfall it fixes. The summary is one or two friendly sentences about how the month looks.
        Call the propose_arrangement tool with your answer.
        """;

    private static readonly JsonObject Tool = new()
    {
        ["name"] = "propose_arrangement",
        ["description"] = "Propose which unpaid bills should move to a different payday column.",
        ["input_schema"] = new JsonObject
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
                        ["required"] = new JsonArray("bill_id", "to_column", "reason")
                    }
                }
            },
            ["required"] = new JsonArray("summary", "moves")
        }
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
            ["max_tokens"] = 4000,
            ["system"] = SystemPrompt,
            ["tools"] = new JsonArray(Tool.DeepClone()),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = "propose_arrangement" },
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
            throw new AdvisorException((int)response.StatusCode is 401 or 403
                ? "The AI service didn't accept the API key. Check Anthropic:ApiKey."
                : "The AI service had a problem. Try again in a minute.");
        }

        var input = JsonNode.Parse(body)?["content"]?.AsArray()
            .FirstOrDefault(c => (string?)c?["type"] == "tool_use")?["input"]
            ?? throw new AdvisorException("The AI didn't send back a suggestion. Try again.");

        // Keep only moves that make sense for this month: an unpaid bill, a real column, a different one.
        var moves = new List<SuggestedMove>();
        var seen = new HashSet<int>();
        foreach (var m in input["moves"]?.AsArray() ?? [])
        {
            if (m?["bill_id"]?.GetValueKind() != JsonValueKind.Number || m["to_column"]?.GetValueKind() != JsonValueKind.Number) continue;
            var expense = expenses.FirstOrDefault(e => e.Id == (int)m["bill_id"]!);
            var to = (int)m["to_column"]! - 1;
            if (expense is null || expense.Status != PaymentStatus.NotPaid || to < 0 || to >= columns.Count || !seen.Add(expense.Id)) continue;
            var from = columns.FirstOrDefault(c => c.Expenses.Contains(expense));
            if (from == columns[to]) continue;
            moves.Add(new SuggestedMove(expense.Id, expense.Title, expense.Amount, expense.DueDay,
                from?.Heading ?? "Not assigned", columns[to].Heading, columns[to].TargetPayPeriodId!.Value,
                (string?)m["reason"] ?? ""));
        }

        var outcomes = columns.Select(c =>
        {
            var after = c.LeftOver
                + moves.Where(mv => c.Expenses.Any(e => e.Id == mv.ExpenseId)).Sum(mv => mv.Amount)
                - moves.Where(mv => mv.To == c.Heading).Sum(mv => mv.Amount);
            return new ColumnOutcome(c.Heading, c.LeftOver, after);
        }).ToList();

        return new ArrangementSuggestion((string?)input["summary"] ?? "", moves, outcomes);
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
        sb.AppendLine("Bills (id | title | category | amount | due day | status | current column):");
        foreach (var e in month.Budget!.Expenses.OrderBy(e => e.DueDay ?? 32).ThenBy(e => e.Title))
        {
            var col = columns.FindIndex(c => c.Expenses.Contains(e));
            var category = month.Categories.FirstOrDefault(c => c.Id == e.CategoryId)?.Name ?? "Other";
            sb.AppendLine($"{e.Id} | {e.Title} | {category} | {e.Amount.ToString("C", us)} | " +
                          $"{(e.DueDay is int d ? "day " + d : "no due day")} | {e.Status} | " +
                          $"{(col >= 0 ? "column " + (col + 1) : "not assigned")}");
        }
        return sb.ToString();
    }
}

/// <summary>A problem the user should see, worded for them.</summary>
public class AdvisorException(string message) : Exception(message);
