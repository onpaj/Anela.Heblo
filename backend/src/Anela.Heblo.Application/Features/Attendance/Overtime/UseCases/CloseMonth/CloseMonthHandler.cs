using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CloseMonth;

public class CloseMonthHandler : IRequestHandler<CloseMonthRequest, CloseMonthResponse>
{
    private readonly IOvertimeEmployeeRepository _employees;
    private readonly IOvertimeStatementRepository _statements;
    private readonly IOvertimeAdjustmentRepository _adjustments;
    private readonly OvertimeCalculationService _calculation;
    private readonly OvertimeExcelBuilder _excelBuilder;
    private readonly IOvertimeReportPublisher _publisher;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<OvertimeOptions> _options;
    private readonly ILogger<CloseMonthHandler> _logger;

    public CloseMonthHandler(
        IOvertimeEmployeeRepository employees,
        IOvertimeStatementRepository statements,
        IOvertimeAdjustmentRepository adjustments,
        OvertimeCalculationService calculation,
        OvertimeExcelBuilder excelBuilder,
        IOvertimeReportPublisher publisher,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider,
        IOptions<OvertimeOptions> options,
        ILogger<CloseMonthHandler> logger)
    {
        _employees = employees;
        _statements = statements;
        _adjustments = adjustments;
        _calculation = calculation;
        _excelBuilder = excelBuilder;
        _publisher = publisher;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    public async Task<CloseMonthResponse> Handle(CloseMonthRequest request, CancellationToken cancellationToken)
    {
        var monthParams = new Dictionary<string, string>
        {
            { "year", request.Year.ToString() },
            { "month", request.Month.ToString() }
        };

        var monthStatements = await _statements.GetByMonthAsync(request.Year, request.Month, cancellationToken);
        if (monthStatements.Any(s => s.Status == OvertimeStatementStatus.Closed))
        {
            return new CloseMonthResponse { Success = false, ErrorCode = ErrorCodes.OvertimeMonthAlreadyClosed, Params = monthParams };
        }

        if (await _statements.AnyOpenBeforeAsync(request.Year, request.Month, cancellationToken))
        {
            return new CloseMonthResponse { Success = false, ErrorCode = ErrorCodes.OvertimePreviousMonthOpen, Params = monthParams };
        }

        var allEmployees = await _employees.GetAllAsync(cancellationToken);
        var active = allEmployees.Where(e => e.IsActive).ToList();
        var nameByPerson = allEmployees.ToDictionary(e => e.PersonId, e => e.DisplayName);
        var computations = await _calculation.ComputeMonthAsync(request.Year, request.Month, active, cancellationToken);

        var missingContract = computations.Where(c => c.DailyContractHours is null)
            .Select(c => nameByPerson[c.PersonId]).ToList();
        if (missingContract.Count > 0)
        {
            return new CloseMonthResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeContractHoursMissing,
                Params = new Dictionary<string, string> { { "names", string.Join(", ", missingContract) } }
            };
        }

        var statementByPerson = monthStatements.ToDictionary(s => s.PersonId);
        if (!request.Force)
        {
            var unreviewed = computations
                .Where(c => !statementByPerson.TryGetValue(c.PersonId, out var s) || !s.IsReviewed)
                .Select(c => nameByPerson[c.PersonId]).ToList();
            if (unreviewed.Count > 0)
            {
                return new CloseMonthResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.OvertimeMonthNotReviewed,
                    Params = new Dictionary<string, string> { { "names", string.Join(", ", unreviewed) } }
                };
            }
        }

        var monthAdjustments = await _adjustments.GetByMonthAsync(request.Year, request.Month, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var closedBy = _currentUserService.GetCurrentUser().Name ?? "unknown";

        foreach (var computation in computations)
        {
            if (!statementByPerson.TryGetValue(computation.PersonId, out var statement))
            {
                statement = new OvertimeMonthlyStatement
                {
                    PersonId = computation.PersonId,
                    Year = request.Year,
                    Month = request.Month
                };
                await _statements.AddAsync(statement, cancellationToken);
            }

            statement.RequiredHours = computation.RequiredHours;
            statement.WorkedHours = computation.WorkedHours;
            statement.VacationHours = computation.VacationHours;
            statement.SickHours = computation.SickHours;
            statement.DoctorHours = computation.DoctorHours;
            statement.CompTimeHours = computation.CompTimeHours;
            statement.OtherAbsenceHours = computation.OtherAbsenceHours;
            statement.DeltaHours = computation.DeltaHours;

            var latestClosed = await _statements.GetLatestClosedAsync(computation.PersonId, cancellationToken);
            var employee = active.First(e => e.PersonId == computation.PersonId);
            var previousBalance = latestClosed?.BalanceAfter ?? employee.BaselineHours;
            var adjustmentsTotal = monthAdjustments.Where(a => a.PersonId == computation.PersonId).Sum(a => a.Hours);

            statement.BalanceAfter = previousBalance + computation.DeltaHours + adjustmentsTotal;
            statement.Status = OvertimeStatementStatus.Closed;
            statement.ClosedAtUtc = now;
            statement.ClosedBy = closedBy;
        }

        // Closing a month must close ALL its statements, including ones for employees who
        // were deactivated after the statement was opened (they have no computation row, so
        // the loop above never touches them; left Open they'd block every future close via
        // AnyOpenBeforeAsync with no recovery path).
        var computedPersonIds = computations.Select(c => c.PersonId).ToHashSet();
        var employeeByPerson = allEmployees.ToDictionary(e => e.PersonId);
        var sweptCount = 0;
        foreach (var statement in monthStatements)
        {
            if (computedPersonIds.Contains(statement.PersonId) || statement.Status != OvertimeStatementStatus.Open)
            {
                continue;
            }

            var latestClosed = await _statements.GetLatestClosedAsync(statement.PersonId, cancellationToken);
            var previousBalance = latestClosed?.BalanceAfter
                ?? (employeeByPerson.TryGetValue(statement.PersonId, out var employee) ? employee.BaselineHours : 0m);
            var adjustmentsTotal = monthAdjustments.Where(a => a.PersonId == statement.PersonId).Sum(a => a.Hours);

            statement.BalanceAfter = previousBalance + statement.DeltaHours + adjustmentsTotal;
            statement.Status = OvertimeStatementStatus.Closed;
            statement.ClosedAtUtc = now;
            statement.ClosedBy = closedBy;
            sweptCount++;
        }

        await _statements.SaveChangesAsync(cancellationToken);

        var response = new CloseMonthResponse { ClosedCount = computations.Count + sweptCount };

        if (!_publisher.IsConfigured)
        {
            response.PublishSkipped = true;
            return response;
        }

        try
        {
            var closed = await _statements.GetAllClosedAsync(cancellationToken);
            var allAdjustments = await _adjustments.GetAllAsync(cancellationToken);
            var workbook = _excelBuilder.Build(allEmployees, closed, allAdjustments);
            await _publisher.PublishAsync(workbook, _options.Value.ExportFileName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Overtime report publish failed after closing {Year}/{Month}", request.Year, request.Month);
            response.PublishFailed = true;
        }

        return response;
    }
}
