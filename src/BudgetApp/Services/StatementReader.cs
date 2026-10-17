using System.Globalization;
using System.Text.Json.Nodes;
using BudgetApp.Models;

namespace BudgetApp.Services;

/// <summary>What the AI found on a loan or card statement. Anything it couldn't find is null.</summary>
public record StatementFindings(string? Lender, DateTime? StatementDate, decimal? Balance, decimal? Apr, decimal? MinimumPayment,
    DateTime? DueDate, List<StatementPromo> Promos, string Summary);

/// <summary>A promotional balance on the statement, such as "0% until 03/2027".</summary>
public record StatementPromo(string Description, decimal Rate, decimal? Balance, DateTime? EndDate, bool DeferredInterest);

/// <summary>
/// Has Claude read a loan or credit card statement (a PDF or a photo) for the amount owed, interest rate,
/// minimum payment and any promotional rate and when it ends. Nothing is saved here; the loan page shows
/// what was found and the user picks what to keep. The file is sent to the AI service and not stored.
/// </summary>
public class StatementReader(ClaudeApi claude)
{
    public const long MaxBytes = 20 * 1024 * 1024;

    public static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".png"] = "image/png", [".webp"] = "image/webp", [".gif"] = "image/gif"
    };

    public bool IsConfigured => claude.IsConfigured;

    private const string SystemPrompt = """
        You read a household's loan and credit card statements. From the statement, find:
        - balance: the current amount owed (the new or statement balance; for an installment loan, the principal
          balance or payoff amount if no statement balance is shown).
        - apr: the regular yearly interest rate for purchases or the loan, as a percent (e.g. 26.99), not a promo rate.
        - minimum_payment and payment_due_date for this statement.
        - statement_date: the closing or statement date.
        - promos: every promotional balance or plan, such as "0% APR until 03/15/2027", "No interest if paid in full
          by ...", or a special-financing purchase. Give its rate as a percent, the balance under it if shown, the
          date it ends, and deferred_interest: true when interest is charged back from the purchase date if it isn't
          paid in full by the end date ("no interest if paid in full", "deferred interest"). Leave promos empty if
          there are none.
        Give dates as YYYY-MM-DD and amounts as plain numbers. Use null for anything the statement doesn't show;
        never guess. summary: two or three plain sentences for the household, mentioning anything that needs action,
        such as a promo ending soon and how much a month it would take to clear it in time.
        """;

    private static JsonObject Nullable(string type) => new() { ["anyOf"] = new JsonArray(new JsonObject { ["type"] = type }, new JsonObject { ["type"] = "null" }) };

    private static readonly JsonObject AnswerSchema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["lender"] = Nullable("string"),
            ["statement_date"] = Nullable("string"),
            ["balance"] = Nullable("number"),
            ["apr"] = Nullable("number"),
            ["minimum_payment"] = Nullable("number"),
            ["payment_due_date"] = Nullable("string"),
            ["promos"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["description"] = new JsonObject { ["type"] = "string" },
                        ["rate"] = new JsonObject { ["type"] = "number" },
                        ["balance"] = Nullable("number"),
                        ["end_date"] = Nullable("string"),
                        ["deferred_interest"] = new JsonObject { ["type"] = "boolean" }
                    },
                    ["required"] = new JsonArray("description", "rate", "balance", "end_date", "deferred_interest"),
                    ["additionalProperties"] = false
                }
            },
            ["summary"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("lender", "statement_date", "balance", "apr", "minimum_payment", "payment_due_date", "promos", "summary"),
        ["additionalProperties"] = false
    };

    public async Task<StatementFindings> ReadAsync(RecurringDebt loan, string fileName, byte[] file, CancellationToken ct = default)
    {
        if (!Types.TryGetValue(Path.GetExtension(fileName), out var mediaType))
            throw new AdvisorException("Upload the statement as a PDF or a photo (JPG, PNG, WEBP or GIF).");
        if (file.Length == 0 || file.Length > MaxBytes)
            throw new AdvisorException("That file is empty or bigger than 20 MB.");

        var data = Convert.ToBase64String(file);
        var source = new JsonObject { ["type"] = "base64", ["media_type"] = mediaType, ["data"] = data };
        var content = new JsonArray(
            new JsonObject { ["type"] = mediaType == "application/pdf" ? "document" : "image", ["source"] = source },
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = $"This is a statement for \"{loan.Title}\"{(loan.Company is null ? "" : " from " + loan.Company)}. " +
                           $"Today is {DateTime.Today:yyyy-MM-dd}. Read it and fill in the answer."
            });
        var answer = await claude.AskAsync(claude.NewRequest(SystemPrompt, content, AnswerSchema), ct);

        var promos = (answer["promos"]?.AsArray() ?? [])
            .Where(p => p is not null)
            .Select(p => new StatementPromo((string?)p!["description"] ?? "Promotional rate", Number(p["rate"]) ?? 0,
                Number(p["balance"]), Date(p["end_date"]), (bool?)p["deferred_interest"] ?? false))
            .ToList();
        return new StatementFindings((string?)answer["lender"], Date(answer["statement_date"]), Number(answer["balance"]),
            Number(answer["apr"]), Number(answer["minimum_payment"]), Date(answer["payment_due_date"]), promos,
            (string?)answer["summary"] ?? "");
    }

    private static decimal? Number(JsonNode? node) =>
        node?.GetValueKind() == System.Text.Json.JsonValueKind.Number ? Math.Round(node.GetValue<decimal>(), 3) : null;

    private static DateTime? Date(JsonNode? node) =>
        DateTime.TryParseExact((string?)node, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
