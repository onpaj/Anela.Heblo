using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CloseMonth;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CreateAdjustment;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.DeleteAdjustment;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.ExportOvertimeReport;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetMonthlyStatements;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetOvertimeEmployees;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.SetStatementReviewed;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.UpsertOvertimeEmployee;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Attendance_Overtime)]
[ApiController]
[Route("api/overtime")]
public class OvertimeController : BaseApiController
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly IMediator _mediator;
    private readonly IOvertimeReportPublisher _publisher;

    public OvertimeController(IMediator mediator, IOvertimeReportPublisher publisher)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    [HttpGet("employees")]
    public async Task<ActionResult<GetOvertimeEmployeesResponse>> GetEmployees(CancellationToken cancellationToken = default)
        => HandleResponse(await _mediator.Send(new GetOvertimeEmployeesRequest(), cancellationToken));

    [HttpPut("employees")]
    [FeatureAuthorize(Feature.Attendance_Overtime, AccessLevel.Write)]
    public async Task<ActionResult<UpsertOvertimeEmployeeResponse>> UpsertEmployee(
        [FromBody] UpsertOvertimeEmployeeRequest request, CancellationToken cancellationToken = default)
        => HandleResponse(await _mediator.Send(request, cancellationToken));

    [HttpGet("statements/{year:int}/{month:int}")]
    public async Task<ActionResult<GetMonthlyStatementsResponse>> GetMonthlyStatements(
        int year, int month, CancellationToken cancellationToken = default)
        => HandleResponse(await _mediator.Send(new GetMonthlyStatementsRequest { Year = year, Month = month }, cancellationToken));

    [HttpPost("statements/{year:int}/{month:int}/reviewed")]
    [FeatureAuthorize(Feature.Attendance_Overtime, AccessLevel.Write)]
    public async Task<ActionResult<SetStatementReviewedResponse>> SetReviewed(
        int year, int month, [FromBody] SetStatementReviewedRequest request, CancellationToken cancellationToken = default)
    {
        request.Year = year;
        request.Month = month;
        return HandleResponse(await _mediator.Send(request, cancellationToken));
    }

    [HttpPost("adjustments")]
    [FeatureAuthorize(Feature.Attendance_Overtime, AccessLevel.Write)]
    public async Task<ActionResult<CreateAdjustmentResponse>> CreateAdjustment(
        [FromBody] CreateAdjustmentRequest request, CancellationToken cancellationToken = default)
        => HandleResponse(await _mediator.Send(request, cancellationToken));

    [HttpDelete("adjustments/{id:int}")]
    [FeatureAuthorize(Feature.Attendance_Overtime, AccessLevel.Write)]
    public async Task<ActionResult<DeleteAdjustmentResponse>> DeleteAdjustment(
        int id, CancellationToken cancellationToken = default)
        => HandleResponse(await _mediator.Send(new DeleteAdjustmentRequest { Id = id }, cancellationToken));

    [HttpPost("close/{year:int}/{month:int}")]
    [FeatureAuthorize(Feature.Attendance_Overtime, AccessLevel.Write)]
    public async Task<ActionResult<CloseMonthResponse>> CloseMonth(
        int year, int month, [FromQuery] bool force = false, CancellationToken cancellationToken = default)
        => HandleResponse(await _mediator.Send(new CloseMonthRequest { Year = year, Month = month, Force = force }, cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> DownloadReport(CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ExportOvertimeReportRequest(), cancellationToken);
        if (!response.Success)
        {
            return BadRequest(response);
        }

        return File(response.Content, XlsxContentType, response.FileName);
    }

    [HttpPost("export/publish")]
    [FeatureAuthorize(Feature.Attendance_Overtime, AccessLevel.Write)]
    public async Task<ActionResult<ExportOvertimeReportResponse>> PublishReport(CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ExportOvertimeReportRequest(), cancellationToken);
        if (!response.Success)
        {
            return HandleResponse(response);
        }

        try
        {
            await _publisher.PublishAsync(response.Content, response.FileName, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Manual overtime report publish failed");
            return HandleResponse(new ExportOvertimeReportResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeExportPublishFailed
            });
        }

        // Content is not re-sent on publish — return a lean success envelope.
        return Ok(new ExportOvertimeReportResponse { FileName = response.FileName });
    }
}
