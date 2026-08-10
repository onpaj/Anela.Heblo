namespace Anela.Heblo.Domain.Features.Attendance.Overtime;

/// <summary>
/// A Logeto person tracked in the overtime ledger, with the baseline balance
/// (seeded from the legacy Excel) from which all deltas accumulate.
/// </summary>
public class OvertimeEmployee
{
    public int Id { get; set; }
    public Guid PersonId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public decimal BaselineHours { get; set; }

    /// <summary>Logeto data before this date is never computed.</summary>
    public DateOnly BaselineDate { get; set; }

    public bool IsActive { get; set; } = true;
}
