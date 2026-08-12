using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.DeleteAdjustment;

public class DeleteAdjustmentHandler : IRequestHandler<DeleteAdjustmentRequest, DeleteAdjustmentResponse>
{
    private readonly IOvertimeStatementRepository _statements;
    private readonly IOvertimeAdjustmentRepository _adjustments;

    public DeleteAdjustmentHandler(
        IOvertimeStatementRepository statements,
        IOvertimeAdjustmentRepository adjustments)
    {
        _statements = statements;
        _adjustments = adjustments;
    }

    public async Task<DeleteAdjustmentResponse> Handle(DeleteAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var adjustment = await _adjustments.GetByIdAsync(request.Id, cancellationToken);
        if (adjustment is null)
        {
            return new DeleteAdjustmentResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeAdjustmentNotFound
            };
        }

        var monthStatements = await _statements.GetByMonthAsync(adjustment.Year, adjustment.Month, cancellationToken);
        if (monthStatements.Any(s => s.Status == OvertimeStatementStatus.Closed))
        {
            return new DeleteAdjustmentResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeAdjustmentMonthClosed
            };
        }

        await _adjustments.DeleteAsync(adjustment, cancellationToken);
        return new DeleteAdjustmentResponse();
    }
}
