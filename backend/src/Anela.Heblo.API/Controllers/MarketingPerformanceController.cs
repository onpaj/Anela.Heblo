using Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceComparison;
using Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceMonths;
using Anela.Heblo.Application.Features.MarketingPerformance.UseCases.RecomputeMarketingPerformance;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>Marketing → Analýzy: monthly ad-spend vs. revenue snapshot.</summary>
[FeatureAuthorize(Feature.Marketing_Performance)]
[ApiController]
[Route("api/[controller]")]
public class MarketingPerformanceController : BaseApiController
{
    private readonly IMediator _mediator;

    public MarketingPerformanceController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Monthly rows with derived metrics for a yyyy-MM range (default last 36 months).</summary>
    [HttpGet("months")]
    [ProducesResponseType(typeof(GetMarketingPerformanceMonthsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GetMarketingPerformanceMonthsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GetMarketingPerformanceMonthsResponse>> GetMonths(
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] bool includeWholesale = false)
    {
        var response = await _mediator.Send(new GetMarketingPerformanceMonthsRequest { From = from, To = to, IncludeWholesale = includeWholesale });
        return HandleResponse(response);
    }

    /// <summary>Year-over-year series (2–3 calendar years, 12 cells each, YTD totals).</summary>
    [HttpGet("comparison")]
    [ProducesResponseType(typeof(GetMarketingPerformanceComparisonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GetMarketingPerformanceComparisonResponse>> GetComparison(
        [FromQuery] int years = 3,
        [FromQuery] bool includeWholesale = false)
    {
        var response = await _mediator.Send(new GetMarketingPerformanceComparisonRequest { Years = years, IncludeWholesale = includeWholesale });
        return HandleResponse(response);
    }

    /// <summary>Enqueues a recompute of every month in [from, to] regardless of locks. Returns the Hangfire job id.</summary>
    [HttpPost("recompute")]
    [FeatureAuthorize(Feature.Marketing_Performance, AccessLevel.Write)]
    [ProducesResponseType(typeof(RecomputeMarketingPerformanceResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(RecomputeMarketingPerformanceResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RecomputeMarketingPerformanceResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(RecomputeMarketingPerformanceResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RecomputeMarketingPerformanceResponse>> Recompute([FromBody] RecomputeMarketingPerformanceRequest request)
    {
        var response = await _mediator.Send(request);
        if (response.Success)
        {
            return Accepted(response);
        }
        return HandleResponse(response);
    }
}
