using System.Globalization;
using System.Text.Json.Nodes;
using BudgetApp.Models;

namespace BudgetApp.Services;

/// <summary>What the AI found on a check stub. Anything it couldn't find is null.</summary>
public record StubFindings(string? Employer, DateTime? PayDate, DateTime? PeriodStart, DateTime? PeriodEnd,
    decimal? Gross, decimal? Net, List<StubFindingLine> Lines, string Summary)
{
    public decimal Total(StubLineKind kind) => Lines.Where(l => l.Kind == kind).Sum(l => l.Amount);

    /// <summary>Gross pay less taxes and deductions; null when the stub has no gross pay.</summary>
    public decimal? Worked => Gross is decimal g
        ? g - Total(StubLineKind.Tax) - Total(StubLineKind.PreTaxDeduction) - Total(StubLineKind.AfterTaxDeduction)
        : null;

    /// <summary>The lines add up to the take-home pay, give or take a few cents.</summary>
    public bool AddsUp => Worked is decimal w && Net is decimal n && Math.Abs(w - n) < 0.05m;
}

public record StubFindingLine(StubLineKind Kind, string Name, decimal Amount, decimal? YearToDate);

/// <summary>
/// Has Claude read a check stub (a PDF or a photo) for this paycheck's gross pay, taxes, deductions and take-home
/// pay. Nothing is saved here; Pay &amp; Schedule shows what was found and the user picks what to keep. The file is
/// sent to the AI service and not stored.
/// </summary>
public class StubReader(ClaudeApi claude)
{
    private const string SystemPrompt = """
        You read a household's check stubs (pay stubs). For this pay period only (not year-to-date), find:
        - employer, pay_date (the check date), period_start and period_end.
        - gross: total gross pay for this period.
        - net: the net pay or take-home amount actually deposited for this period.
        - lines: every earnings, tax and deduction line for this period, each with kind:
          "earnings" for pay earned (regular, overtime, bonus, holiday, PTO),
          "tax" for taxes withheld (federal and state income tax, Social Security/OASDI, Medicare, local taxes),
          "pretax" for deductions taken before taxes (401(k)/403(b), traditional retirement, health, dental and vision
          insurance, HSA, FSA), and "aftertax" for the rest (Roth retirement, life insurance, union dues, garnishments,
          loan repayments). Give the period amount as a positive number and the year-to-date amount if shown.
          Leave out lines that are zero for this period.
        Give dates as YYYY-MM-DD and amounts as plain numbers. Use null for anything the stub doesn't show; never guess.
        summary: two or three plain sentences for the household: what came out of the check and anything worth
        noticing, such as overtime that makes this check bigger than usual or a deduction that looks new.
        """;

    private static JsonObject Nullable(string type) => new() { ["anyOf"] = new JsonArray(new JsonObject { ["type"] = type }, new JsonObject { ["type"] = "null" }) };

    private static readonly JsonObject AnswerSchema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["employer"] = Nullable("string"),
            ["pay_date"] = Nullable("string"),
            ["period_start"] = Nullable("string"),
            ["period_end"] = Nullable("string"),
            ["gross"] = Nullable("number"),
            ["net"] = Nullable("number"),
            ["lines"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("earnings", "tax", "pretax", "aftertax") },
                        ["name"] = new JsonObject { ["type"] = "string" },
                        ["amount"] = new JsonObject { ["type"] = "number" },
                        ["year_to_date"] = Nullable("number")
                    },
                    ["required"] = new JsonArray("kind", "name", "amount", "year_to_date"),
                    ["additionalProperties"] = false
                }
            },
            ["summary"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("employer", "pay_date", "period_start", "period_end", "gross", "net", "lines", "summary"),
        ["additionalProperties"] = false
    };

    public async Task<StubFindings> ReadAsync(IncomeSource income, string fileName, byte[] file, CancellationToken ct = default)
    {
        if (!StatementReader.Types.TryGetValue(Path.GetExtension(fileName), out var mediaType))
            throw new AdvisorException("Upload the check stub as a PDF or a photo (JPG, PNG, WEBP or GIF).");
        if (file.Length == 0 || file.Length > StatementReader.MaxBytes)
            throw new AdvisorException("That file is empty or bigger than 20 MB.");

        var source = new JsonObject { ["type"] = "base64", ["media_type"] = mediaType, ["data"] = Convert.ToBase64String(file) };
        var content = new JsonArray(
            new JsonObject { ["type"] = mediaType == "application/pdf" ? "document" : "image", ["source"] = source },
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = $"This is a check stub for \"{income.Name}\", paid {income.Frequency.ToString().ToLowerInvariant()}. " +
                           $"Today is {DateTime.Today:yyyy-MM-dd}. Read it and fill in the answer."
            });
        var answer = await claude.AskAsync(claude.NewRequest(SystemPrompt, content, AnswerSchema), ct);

        var lines = (answer["lines"]?.AsArray() ?? [])
            .Where(l => l is not null && Number(l["amount"]) is decimal a && a != 0)
            .Select(l => new StubFindingLine(Kind((string?)l!["kind"]), Trim((string?)l["name"]), Math.Abs(Number(l["amount"])!.Value),
                Number(l["year_to_date"])))
            .ToList();
        return new StubFindings((string?)answer["employer"], Date(answer["pay_date"]), Date(answer["period_start"]),
            Date(answer["period_end"]), Number(answer["gross"]), Number(answer["net"]), lines, (string?)answer["summary"] ?? "");
    }

    private static StubLineKind Kind(string? kind) => kind switch
    {
        "earnings" => StubLineKind.Earnings,
        "tax" => StubLineKind.Tax,
        "pretax" => StubLineKind.PreTaxDeduction,
        _ => StubLineKind.AfterTaxDeduction
    };

    private static string Trim(string? name) => string.IsNullOrWhiteSpace(name) ? "Other" : name.Trim()[..Math.Min(100, name.Trim().Length)];

    private static decimal? Number(JsonNode? node) =>
        node?.GetValueKind() == System.Text.Json.JsonValueKind.Number ? Math.Round(node.GetValue<decimal>(), 2) : null;

    private static DateTime? Date(JsonNode? node) =>
        DateTime.TryParseExact((string?)node, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
