using Anela.Heblo.Application.Features.Attendance.Overtime.Contracts;
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetOvertimeEmployees;

public class GetOvertimeEmployeesHandler : IRequestHandler<GetOvertimeEmployeesRequest, GetOvertimeEmployeesResponse>
{
    private readonly IOvertimeEmployeeRepository _employees;
    private readonly IOvertimeStatementRepository _statements;
    private readonly ILogetoClient _client;

    public GetOvertimeEmployeesHandler(
        IOvertimeEmployeeRepository employees,
        IOvertimeStatementRepository statements,
        ILogetoClient client)
    {
        _employees = employees;
        _statements = statements;
        _client = client;
    }

    public async Task<GetOvertimeEmployeesResponse> Handle(GetOvertimeEmployeesRequest request, CancellationToken cancellationToken)
    {
        var tracked = await _employees.GetAllAsync(cancellationToken);
        var people = await _client.GetPeopleAsync(cancellationToken);

        var response = new GetOvertimeEmployeesResponse();

        foreach (var employee in tracked)
        {
            var latestClosed = await _statements.GetLatestClosedAsync(employee.PersonId, cancellationToken);
            response.Employees.Add(new OvertimeEmployeeDto
            {
                PersonId = employee.PersonId,
                DisplayName = employee.DisplayName,
                BaselineHours = employee.BaselineHours,
                BaselineDate = employee.BaselineDate,
                IsActive = employee.IsActive,
                CurrentBalance = latestClosed?.BalanceAfter ?? employee.BaselineHours
            });
        }

        var trackedIds = tracked.Select(t => t.PersonId).ToHashSet();
        response.AvailablePeople = people
            .Where(p => !p.Inactive && !trackedIds.Contains(p.Guid))
            .Select(p => new AvailableLogetoPersonDto
            {
                PersonId = p.Guid,
                FullName = $"{p.FirstName} {p.LastName}".Trim()
            })
            .OrderBy(p => p.FullName)
            .ToList();

        return response;
    }
}
