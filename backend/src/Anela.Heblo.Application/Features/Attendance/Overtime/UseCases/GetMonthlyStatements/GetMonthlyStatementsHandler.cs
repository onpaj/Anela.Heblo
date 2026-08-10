using Anela.Heblo.Application.Features.Attendance.Overtime.Contracts;
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetMonthlyStatements;

public class GetMonthlyStatementsHandler : IRequestHandler<GetMonthlyStatementsRequest, GetMonthlyStatementsResponse>
{
    private readonly IOvertimeEmployeeRepository _employees;
    private readonly IOvertimeStatementRepository _statements;
    private readonly IOvertimeAdjustmentRepository _adjustments;
    private readonly OvertimeCalculationService _calculation;

    public GetMonthlyStatementsHandler(
        IOvertimeEmployeeRepository employees,
        IOvertimeStatementRepository statements,
        IOvertimeAdjustmentRepository adjustments,
        OvertimeCalculationService calculation)
    {
        _employees = employees;
        _statements = statements;
        _adjustments = adjustments;
        _calculation = calculation;
    }

    public async Task<GetMonthlyStatementsResponse> Handle(GetMonthlyStatementsRequest request, CancellationToken cancellationToken)
    {
        var allEmployees = await _employees.GetAllAsync(cancellationToken);
        var byPerson = allEmployees.ToDictionary(e => e.PersonId);
        var existing = await _statements.GetByMonthAsync(request.Year, request.Month, cancellationToken);
        var monthAdjustments = await _adjustments.GetByMonthAsync(request.Year, request.Month, cancellationToken);
        var isClosed = existing.Any(s => s.Status == OvertimeStatementStatus.Closed);

        var response = new GetMonthlyStatementsResponse
        {
            Year = request.Year,
            Month = request.Month,
            IsClosed = isClosed
        };

        if (isClosed)
        {
            foreach (var statement in existing.OrderBy(s => byPerson.TryGetValue(s.PersonId, out var e) ? e.DisplayName : ""))
            {
                var adjustments = MapAdjustments(monthAdjustments, statement.PersonId);
                var adjustmentsTotal = adjustments.Sum(a => a.Hours);
                response.Statements.Add(new OvertimeStatementDto
                {
                    PersonId = statement.PersonId,
                    DisplayName = byPerson.TryGetValue(statement.PersonId, out var emp) ? emp.DisplayName : statement.PersonId.ToString(),
                    IsReviewed = statement.IsReviewed,
                    RequiredHours = statement.RequiredHours,
                    WorkedHours = statement.WorkedHours,
                    VacationHours = statement.VacationHours,
                    SickHours = statement.SickHours,
                    DoctorHours = statement.DoctorHours,
                    CompTimeHours = statement.CompTimeHours,
                    OtherAbsenceHours = statement.OtherAbsenceHours,
                    DeltaHours = statement.DeltaHours,
                    PreviousBalance = statement.BalanceAfter - statement.DeltaHours - adjustmentsTotal,
                    AdjustmentsTotal = adjustmentsTotal,
                    ProjectedBalance = statement.BalanceAfter,
                    Adjustments = adjustments
                });
            }

            return response;
        }

        IReadOnlyList<PersonMonthComputation> computations;
        try
        {
            var active = allEmployees.Where(e => e.IsActive).ToList();
            computations = await _calculation.ComputeMonthAsync(request.Year, request.Month, active, cancellationToken);
        }
        catch (Exception ex)
        {
            return new GetMonthlyStatementsResponse(ex);
        }

        var existingByPerson = existing.ToDictionary(s => s.PersonId);

        foreach (var computation in computations)
        {
            var employee = byPerson[computation.PersonId];

            if (!existingByPerson.TryGetValue(computation.PersonId, out var statement))
            {
                statement = new OvertimeMonthlyStatement
                {
                    PersonId = computation.PersonId,
                    Year = request.Year,
                    Month = request.Month,
                    Status = OvertimeStatementStatus.Open
                };
                CopyComputation(statement, computation);
                await _statements.AddAsync(statement, cancellationToken);
            }
            else
            {
                CopyComputation(statement, computation);
                await _statements.SaveChangesAsync(cancellationToken);
            }

            var latestClosed = await _statements.GetLatestClosedAsync(computation.PersonId, cancellationToken);
            var previousBalance = latestClosed?.BalanceAfter ?? employee.BaselineHours;
            var adjustments = MapAdjustments(monthAdjustments, computation.PersonId);
            var adjustmentsTotal = adjustments.Sum(a => a.Hours);

            response.Statements.Add(new OvertimeStatementDto
            {
                PersonId = computation.PersonId,
                DisplayName = employee.DisplayName,
                IsReviewed = statement.IsReviewed,
                DailyContractHours = computation.DailyContractHours,
                RequiredHours = computation.RequiredHours,
                WorkedHours = computation.WorkedHours,
                VacationHours = computation.VacationHours,
                SickHours = computation.SickHours,
                DoctorHours = computation.DoctorHours,
                CompTimeHours = computation.CompTimeHours,
                OtherAbsenceHours = computation.OtherAbsenceHours,
                DeltaHours = computation.DeltaHours,
                PreviousBalance = previousBalance,
                AdjustmentsTotal = adjustmentsTotal,
                ProjectedBalance = previousBalance + computation.DeltaHours + adjustmentsTotal,
                Warnings = computation.Warnings,
                Adjustments = adjustments
            });
        }

        response.Statements = response.Statements.OrderBy(s => s.DisplayName).ToList();
        return response;
    }

    private static void CopyComputation(OvertimeMonthlyStatement statement, PersonMonthComputation computation)
    {
        statement.RequiredHours = computation.RequiredHours;
        statement.WorkedHours = computation.WorkedHours;
        statement.VacationHours = computation.VacationHours;
        statement.SickHours = computation.SickHours;
        statement.DoctorHours = computation.DoctorHours;
        statement.CompTimeHours = computation.CompTimeHours;
        statement.OtherAbsenceHours = computation.OtherAbsenceHours;
        statement.DeltaHours = computation.DeltaHours;
    }

    private static List<OvertimeAdjustmentDto> MapAdjustments(IReadOnlyList<OvertimeAdjustment> monthAdjustments, Guid personId)
        => monthAdjustments
            .Where(a => a.PersonId == personId)
            .Select(a => new OvertimeAdjustmentDto
            {
                Id = a.Id,
                PersonId = a.PersonId,
                Type = a.Type,
                Hours = a.Hours,
                Note = a.Note,
                CreatedAtUtc = a.CreatedAtUtc,
                CreatedBy = a.CreatedBy
            })
            .ToList();
}
