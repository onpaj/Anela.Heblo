namespace Anela.Heblo.Domain.Features.Attendance.Overtime;

/// <summary>Manual ledger move (payout, purchase deduction, correction, …), bound to an open month.</summary>
public class OvertimeAdjustment
{
    public int Id { get; set; }
    public Guid PersonId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public OvertimeAdjustmentType Type { get; set; }

    /// <summary>Signed; negative reduces the balance. May be 0 for SportBenefit notes.</summary>
    public decimal Hours { get; set; }

    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}
