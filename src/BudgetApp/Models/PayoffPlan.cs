namespace BudgetApp.Models;

public record PayoffPlan(PayoffStrategy Strategy, decimal MonthlyPayment, List<DebtOutcome> Debts, List<ScheduleMonth> Schedule,
    decimal TotalInterest, decimal TotalPaid, bool PaysOff)
{
    public int Months => Schedule.Count;
    public DateTime? DebtFreeDate => PaysOff && Schedule.Count > 0 ? Schedule[^1].Month : null;
}
