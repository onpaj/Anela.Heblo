namespace Anela.Heblo.Domain.Features.Attendance.Overtime;

/// <summary>
/// One person-month of the overtime ledger. While Open, hour fields are a cache of the
/// live Logeto computation; on close they freeze and become the audit record.
/// </summary>
public class OvertimeMonthlyStatement
{
    public int Id { get; set; }
    public Guid PersonId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public OvertimeStatementStatus Status { get; set; } = OvertimeStatementStatus.Open;

    public decimal RequiredHours { get; set; }
    public decimal WorkedHours { get; set; }
    public decimal VacationHours { get; set; }
    public decimal SickHours { get; set; }
    public decimal DoctorHours { get; set; }
    public decimal CompTimeHours { get; set; }
    public decimal OtherAbsenceHours { get; set; }
    public decimal DeltaHours { get; set; }

    /// <summary>Previous balance + delta + month's adjustments; written on close.</summary>
    public decimal BalanceAfter { get; set; }

    public bool IsReviewed { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? ClosedBy { get; set; }
}
