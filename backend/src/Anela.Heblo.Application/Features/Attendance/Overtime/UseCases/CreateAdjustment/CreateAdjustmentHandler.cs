using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CreateAdjustment;

public class CreateAdjustmentHandler : IRequestHandler<CreateAdjustmentRequest, CreateAdjustmentResponse>
{
    private readonly IOvertimeEmployeeRepository _employees;
    private readonly IOvertimeStatementRepository _statements;
    private readonly IOvertimeAdjustmentRepository _adjustments;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public CreateAdjustmentHandler(
        IOvertimeEmployeeRepository employees,
        IOvertimeStatementRepository statements,
        IOvertimeAdjustmentRepository adjustments,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _employees = employees;
        _statements = statements;
        _adjustments = adjustments;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public async Task<CreateAdjustmentResponse> Handle(CreateAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var employee = await _employees.GetByPersonIdAsync(request.PersonId, cancellationToken);
        if (employee is null)
        {
            return new CreateAdjustmentResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeEmployeeNotFound
            };
        }

        var monthStatements = await _statements.GetByMonthAsync(request.Year, request.Month, cancellationToken);
        if (monthStatements.Any(s => s.Status == OvertimeStatementStatus.Closed))
        {
            return new CreateAdjustmentResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeAdjustmentMonthClosed
            };
        }

        var adjustment = new OvertimeAdjustment
        {
            PersonId = request.PersonId,
            Year = request.Year,
            Month = request.Month,
            Type = request.Type,
            Hours = request.Hours,
            Note = request.Note,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            CreatedBy = _currentUserService.GetCurrentUser().Name ?? "unknown"
        };

        await _adjustments.AddAsync(adjustment, cancellationToken);

        return new CreateAdjustmentResponse { Id = adjustment.Id };
    }
}
