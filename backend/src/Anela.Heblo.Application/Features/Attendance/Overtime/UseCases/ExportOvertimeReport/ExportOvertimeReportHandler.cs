using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.ExportOvertimeReport;

public class ExportOvertimeReportHandler : IRequestHandler<ExportOvertimeReportRequest, ExportOvertimeReportResponse>
{
    private readonly IOvertimeEmployeeRepository _employees;
    private readonly IOvertimeStatementRepository _statements;
    private readonly IOvertimeAdjustmentRepository _adjustments;
    private readonly OvertimeExcelBuilder _builder;
    private readonly IOptions<OvertimeOptions> _options;

    public ExportOvertimeReportHandler(
        IOvertimeEmployeeRepository employees,
        IOvertimeStatementRepository statements,
        IOvertimeAdjustmentRepository adjustments,
        OvertimeExcelBuilder builder,
        IOptions<OvertimeOptions> options)
    {
        _employees = employees;
        _statements = statements;
        _adjustments = adjustments;
        _builder = builder;
        _options = options;
    }

    public async Task<ExportOvertimeReportResponse> Handle(ExportOvertimeReportRequest request, CancellationToken cancellationToken)
    {
        var employees = await _employees.GetAllAsync(cancellationToken);
        var closed = await _statements.GetAllClosedAsync(cancellationToken);
        var adjustments = await _adjustments.GetAllAsync(cancellationToken);

        return new ExportOvertimeReportResponse
        {
            Content = _builder.Build(employees, closed, adjustments),
            FileName = _options.Value.ExportFileName
        };
    }
}
