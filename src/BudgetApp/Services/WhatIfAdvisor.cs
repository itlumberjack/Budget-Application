using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using BudgetApp.Models;

namespace BudgetApp.Services;

/// <summary>
/// Turns a what-if question about the household's loans ("what if I roll everything into the HELOC at 7%?") into a
/// WhatIfScenario. Claude only describes the changes; WhatIfCalculator and DebtPlanner work out the numbers.
/// </summary>
public class WhatIfAdvisor(ClaudeApi claude)
{
    private const string SystemPrompt = """
        You turn a household's what-if question about its debt into changes the app's payoff calculator can run.
        You get the loans (each with an id) and the current payoff plan. Describe the question as:
        - loan_changes: a new balance, apr or minimum_payment for an existing loan (null keeps it), or paid_off: true
          when the question pays a loan off with money from outside the plan (a bonus, savings, selling something).
        - consolidations: rolling loans together. into_loan_id is an existing loan that takes the others in (such as
          refinancing a HELOC and moving the other debt into it), or null with new_loan_name for a brand-new loan.
          loan_ids lists the loans rolled in (for "all my other debt", every loan except the one taking it in).
          apr is the new rate. fee is any closing cost or transfer fee in dollars (a 3% transfer fee is 3% of the
          balance moved). term_months is the loan's term if the question gives one ("a 15-year HELOC" is 180);
          minimum_payment only if the question names one. Otherwise leave both null.
        - new_loans: a loan the question adds that isn't consolidating anything (such as a new car loan).
        - extra_monthly: a new monthly extra payment toward debt, strategy: a different payoff order, and
          one_time_amount with one_time_month (YYYY-MM-01) for a lump sum. Null keeps the current plan's.
        Only change what the question asks. Don't invent numbers it doesn't give; when you must assume something, say
        so in assumptions (short plain sentences). title is a short name for the scenario, like "HELOC at 7% takes
        all other debt". If the question can't be described with these changes, say why in unsupported and leave
        the rest empty; otherwise unsupported is null.
        """;

    private static JsonObject Nullable(string type) => new() { ["anyOf"] = new JsonArray(new JsonObject { ["type"] = type }, new JsonObject { ["type"] = "null" }) };
    private static JsonObject Obj(JsonObject props) => new()
    {
        ["type"] = "object", ["properties"] = props,
        ["required"] = new JsonArray(props.Select(p => (JsonNode)p.Key).ToArray()), ["additionalProperties"] = false
    };

    private static readonly JsonObject AnswerSchema = Obj(new JsonObject
    {
        ["title"] = new JsonObject { ["type"] = "string" },
        ["assumptions"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
        ["loan_changes"] = new JsonObject
        {
            ["type"] = "array",
            ["items"] = Obj(new JsonObject
            {
                ["loan_id"] = new JsonObject { ["type"] = "integer" },
                ["balance"] = Nullable("number"), ["apr"] = Nullable("number"), ["minimum_payment"] = Nullable("number"),
                ["paid_off"] = new JsonObject { ["type"] = "boolean" }
            })
        },
        ["consolidations"] = new JsonObject
        {
            ["type"] = "array",
            ["items"] = Obj(new JsonObject
            {
                ["into_loan_id"] = Nullable("integer"), ["new_loan_name"] = Nullable("string"),
                ["loan_ids"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" } },
                ["apr"] = new JsonObject { ["type"] = "number" }, ["fee"] = Nullable("number"),
                ["term_months"] = Nullable("integer"), ["minimum_payment"] = Nullable("number")
            })
        },
        ["new_loans"] = new JsonObject
        {
            ["type"] = "array",
            ["items"] = Obj(new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string" }, ["balance"] = new JsonObject { ["type"] = "number" },
                ["apr"] = new JsonObject { ["type"] = "number" }, ["minimum_payment"] = new JsonObject { ["type"] = "number" }
            })
        },
        ["extra_monthly"] = Nullable("number"),
        ["strategy"] = new JsonObject { ["anyOf"] = new JsonArray(
            new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Enum.GetNames<PayoffStrategy>().Select(n => (JsonNode)n).ToArray()) },
            new JsonObject { ["type"] = "null" }) },
        ["one_time_amount"] = Nullable("number"),
        ["one_time_month"] = Nullable("string"),
        ["unsupported"] = Nullable("string")
    });

    public async Task<WhatIfScenario> AskAsync(string question, IReadOnlyList<RecurringDebt> loans, DebtPlanSettings settings, CancellationToken ct = default)
    {
        var text = new StringBuilder();
        text.AppendLine($"Today is {DateTime.Today:yyyy-MM-dd}. Loans (id | name | lender | balance | APR | monthly payment | promo):");
        foreach (var l in loans)
            text.AppendLine(string.Join(" | ", l.Id, l.Title, l.Company ?? "-", (l.Balance ?? 0).ToString("0.00", CultureInfo.InvariantCulture),
                (l.InterestRate ?? 0).ToString("0.###", CultureInfo.InvariantCulture) + "%", l.MonthlyAmount.ToString("0.00", CultureInfo.InvariantCulture),
                l.Promo is { } p ? $"{p.Rate:0.###}% until {p.EndDate:yyyy-MM-dd}" : "-"));
        text.AppendLine($"Current plan: strategy {settings.Strategy}, extra {settings.ExtraMonthly.ToString("0.00", CultureInfo.InvariantCulture)} a month" +
                        string.Concat(settings.LumpSums.Select(l => $", one-time {l.Amount.ToString("0.00", CultureInfo.InvariantCulture)} on {l.Date:yyyy-MM-dd}")) + ".");
        text.AppendLine().AppendLine("Question: " + question.Trim());

        var a = await claude.AskAsync(claude.NewRequest(SystemPrompt, text.ToString(), AnswerSchema), ct);
        var ids = loans.Select(l => l.Id).ToHashSet();
        List<JsonNode> Items(string key) => (a[key]?.AsArray() ?? []).Where(x => x is not null).Select(x => x!).ToList();
        return new WhatIfScenario(
            Text(a["title"]) ?? "What if",
            Items("assumptions").Select(Text).OfType<string>().ToList(),
            Items("loan_changes").Where(c => ids.Contains((int?)c["loan_id"] ?? 0))
                .Select(c => new LoanChange((int)c["loan_id"]!, Positive(c["balance"]), Rate(c["apr"]), Positive(c["minimum_payment"]), (bool?)c["paid_off"] ?? false)).ToList(),
            Items("consolidations").Where(c => Rate(c["apr"]) is not null)
                .Select(c => new Consolidation(Id(c["into_loan_id"], ids), Text(c["new_loan_name"]),
                    (c["loan_ids"]?.AsArray() ?? []).Select(i => (int?)i ?? 0).Where(ids.Contains).Distinct().ToList(),
                    Rate(c["apr"])!.Value, Positive(c["fee"]), (int?)c["term_months"] is int n && n > 0 && n <= 600 ? n : null, Positive(c["minimum_payment"]))).ToList(),
            Items("new_loans").Where(n => Positive(n["balance"]) is not null && Rate(n["apr"]) is not null)
                .Select(n => new NewLoan(Text(n["name"]) ?? "New loan", Positive(n["balance"])!.Value, Rate(n["apr"])!.Value, Positive(n["minimum_payment"]) ?? 0)).ToList(),
            Number(a["extra_monthly"]) is decimal e ? Math.Max(0, e) : null,
            Enum.TryParse<PayoffStrategy>((string?)a["strategy"], out var s) ? s : null,
            Positive(a["one_time_amount"]),
            DateTime.TryParseExact((string?)a["one_time_month"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? new DateTime(d.Year, d.Month, 1) : null,
            Text(a["unsupported"]));
    }

    private static string? Text(JsonNode? n) => (string?)n is { } s && !string.IsNullOrWhiteSpace(s) ? s.Trim()[..Math.Min(200, s.Trim().Length)] : null;
    private static decimal? Number(JsonNode? n) => n?.GetValueKind() == System.Text.Json.JsonValueKind.Number ? Math.Round(n.GetValue<decimal>(), 2) : null;
    private static decimal? Positive(JsonNode? n) => Number(n) is decimal d && d > 0 ? d : null;
    private static decimal? Rate(JsonNode? n) => n?.GetValueKind() == System.Text.Json.JsonValueKind.Number && n.GetValue<decimal>() is var r && r >= 0 && r <= 100 ? Math.Round(r, 3) : null;
    private static int? Id(JsonNode? n, HashSet<int> ids) => (int?)n is int i && ids.Contains(i) ? i : null;
}
