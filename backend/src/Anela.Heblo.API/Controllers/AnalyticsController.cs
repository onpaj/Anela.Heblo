using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = AuthorizationConstants.Roles.FinanceReader)]
public class AnalyticsController : BaseApiController
{
    private readonly IMediator _mediator;

    public AnalyticsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("product-margin-summary")]
    [ProducesResponseType(typeof(GetProductMarginSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetProductMarginSummaryResponse>> GetProductMarginSummary([FromQuery] GetProductMarginSummaryRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    [HttpGet("margin-analysis")]
    [ProducesResponseType(typeof(GetProductMarginAnalysisResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetProductMarginAnalysisResponse>> GetMarginAnalysis([FromQuery] GetProductMarginAnalysisRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    [HttpGet("margin-report")]
    [ProducesResponseType(typeof(GetMarginReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetMarginReportResponse>> GetMarginReport([FromQuery] GetMarginReportRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    [HttpGet("invoice-import-statistics")]
    [ProducesResponseType(typeof(GetInvoiceImportStatisticsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetInvoiceImportStatisticsResponse>> GetInvoiceImportStatistics([FromQuery] GetInvoiceImportStatisticsRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }
}