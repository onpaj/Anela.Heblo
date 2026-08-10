using Anela.Heblo.Domain.Features.Attendance.Overtime;
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CreateAdjustment;

public class CreateAdjustmentRequest : IRequest<CreateAdjustmentResponse>
{
    public Guid PersonId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public OvertimeAdjustmentType Type { get; set; }
    public decimal Hours { get; set; }
    public string Note { get; set; } = string.Empty;
}
