using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models;

/// <summary>A change to one loan in a what-if: new numbers, or paid off from somewhere else.</summary>
public record LoanChange(int LoanId, decimal? Balance, decimal? Apr, decimal? MinimumPayment, bool PaidOff);

/// <summary>
/// Rolling loans into one: into an existing loan (IntoLoanId) or a new one (NewLoanName), at a new rate, plus
/// any fee. The payment is MinimumPayment, or worked out from TermMonths, or else the old payments added up.
/// </summary>
public record Consolidation(int? IntoLoanId, string? NewLoanName, List<int> LoanIds, decimal Apr, decimal? Fee,
    int? TermMonths, decimal? MinimumPayment);

public record NewLoan(string Name, decimal Balance, decimal Apr, decimal MinimumPayment);

/// <summary>
/// A what-if question turned into changes to the loans and the payoff plan. Null plan settings keep the current
/// ones. Unsupported says why a question can't be worked out with these changes.
/// </summary>
public record WhatIfScenario(string Title, List<string> Assumptions, List<LoanChange> LoanChanges,
    List<Consolidation> Consolidations, List<NewLoan> NewLoans, decimal? ExtraMonthly, PayoffStrategy? Strategy,
    decimal? OneTimeAmount, DateTime? OneTimeMonth, string? Unsupported);

/// <summary>A what-if question asked on Debt Payoff, kept so it can be opened again with today's balances.</summary>
public class WhatIfQuestion
{
    public int Id { get; set; }

    [Required, StringLength(1000)]
    public string Question { get; set; } = "";

    [StringLength(200)]
    public string Title { get; set; } = "";

    /// <summary>The WhatIfScenario as JSON.</summary>
    public string ScenarioJson { get; set; } = "";

    public DateTime AskedAt { get; set; }
}
