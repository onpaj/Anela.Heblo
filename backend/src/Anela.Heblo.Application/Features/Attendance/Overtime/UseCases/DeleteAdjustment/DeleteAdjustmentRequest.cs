using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.DeleteAdjustment;

public class DeleteAdjustmentRequest : IRequest<DeleteAdjustmentResponse>
{
    public int Id { get; set; }
}
