namespace BudgetApp.Models;

public record PayoffPlan(PayoffStrategy Strategy, decimal MonthlyPayment, List<DebtOutcome> Debts, List<ScheduleMonth> Schedule,
    decimal TotalInterest, decimal TotalPaid, bool PaysOff)
{
    public int Months => Schedule.Count;
    public DateTime? DebtFreeDate => PaysOff && Schedule.Count > 0 ? Schedule[^1].Month : null;

    /// <summary>One check per debt with a promo that hadn't ended when the plan starts.</summary>
    public List<PromoCheck> Promos { get; init; } = new();
}
