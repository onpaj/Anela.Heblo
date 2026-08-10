using Anela.Heblo.Domain.Features.Attendance.Overtime;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.Contracts;

public class OvertimeAdjustmentDto
{
    public int Id { get; set; }
    public Guid PersonId { get; set; }
    public OvertimeAdjustmentType Type { get; set; }
    public decimal Hours { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}
