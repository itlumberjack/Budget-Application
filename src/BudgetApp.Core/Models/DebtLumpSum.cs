using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Models;

/// <summary>A one-time extra payment toward debt from the Debt Payoff page, paid from the payday on or before its date.</summary>
public class DebtLumpSum
{
    public int Id { get; set; }

    [DataType(DataType.Date)]
    public DateTime Date { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    [Range(0, 10_000_000)]
    public decimal Amount { get; set; }

    [StringLength(100)]
    public string? Note { get; set; }
}
