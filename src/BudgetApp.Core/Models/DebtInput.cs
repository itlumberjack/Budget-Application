namespace BudgetApp.Models;

/// <summary>One debt going into a payoff plan.</summary>
public record DebtInput(int Id, string Name, decimal Balance, decimal Apr, decimal MinPayment, int? Priority, PromoTerms? Promo = null);

/// <summary>
/// A promotional rate (often 0%) on some or all of a loan until a date. Balance is the part under the promo
/// (null: all of it). With deferred interest, any promo balance left when it ends is charged the interest it
/// would have built up at the regular rate since the promo started.
/// </summary>
public record PromoTerms(decimal Rate, decimal? Balance, DateTime EndDate, bool DeferredInterest);

/// <summary>Whether a plan clears a promo balance before the promo ends, and what it would take if not.</summary>
public record PromoCheck(int DebtId, string Name, decimal PromoBalance, decimal Rate, DateTime EndDate, bool DeferredInterest,
    DateTime? ClearedMonth, bool ClearsInTime, decimal NeededMonthly, decimal LeftAtEnd, decimal DeferredInterestCharged);
