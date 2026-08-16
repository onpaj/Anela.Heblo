using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.UpsertOvertimeEmployee;

public class UpsertOvertimeEmployeeHandler : IRequestHandler<UpsertOvertimeEmployeeRequest, UpsertOvertimeEmployeeResponse>
{
    private readonly IOvertimeEmployeeRepository _employees;
    private readonly IOvertimeStatementRepository _statements;

    public UpsertOvertimeEmployeeHandler(
        IOvertimeEmployeeRepository employees,
        IOvertimeStatementRepository statements)
    {
        _employees = employees;
        _statements = statements;
    }

    public async Task<UpsertOvertimeEmployeeResponse> Handle(UpsertOvertimeEmployeeRequest request, CancellationToken cancellationToken)
    {
        var existing = await _employees.GetByPersonIdAsync(request.PersonId, cancellationToken);
        var latestClosed = await _statements.GetLatestClosedAsync(request.PersonId, cancellationToken);

        var baselineChanged = existing is not null
            && (existing.BaselineHours != request.BaselineHours || existing.BaselineDate != request.BaselineDate);

        if (latestClosed is not null && baselineChanged)
        {
            return new UpsertOvertimeEmployeeResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string>
                {
                    { "message", "Baseline nelze měnit — zaměstnanec už má uzavřený měsíc." }
                }
            };
        }

        await _employees.UpsertAsync(new OvertimeEmployee
        {
            PersonId = request.PersonId,
            DisplayName = request.DisplayName,
            BaselineHours = request.BaselineHours,
            BaselineDate = request.BaselineDate,
            IsActive = request.IsActive
        }, cancellationToken);

        return new UpsertOvertimeEmployeeResponse();
    }
}
