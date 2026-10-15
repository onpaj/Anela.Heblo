using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdAuditEvents;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAgentRuns;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdChangeHistory;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>Marketing → ad activity: append-only audit log, reported agent runs and out-of-band change events.</summary>
[FeatureAuthorize(Feature.Marketing_Ads)]
[ApiController]
[Route("api/marketing-ads/activity")]
public class MarketingAdsActivityController : BaseApiController
{
    private readonly IMediator _mediator;

    public MarketingAdsActivityController(IMediator mediator) => _mediator = mediator;

    [HttpGet("audit")]
    [ProducesResponseType(typeof(ListAdAuditEventsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ListAdAuditEventsResponse>> ListAuditEvents([FromQuery] ListAdAuditEventsRequest request) =>
        HandleResponse(await _mediator.Send(request));

    [HttpGet("agent-runs")]
    [ProducesResponseType(typeof(ListAgentRunsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ListAgentRunsResponse>> ListAgentRuns([FromQuery] ListAgentRunsRequest request) =>
        HandleResponse(await _mediator.Send(request));

    [HttpGet("change-events")]
    [ProducesResponseType(typeof(GetAdChangeHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GetAdChangeHistoryResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GetAdChangeHistoryResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GetAdChangeHistoryResponse>> ListChangeEvents([FromQuery] GetAdChangeHistoryRequest request) =>
        HandleResponse(await _mediator.Send(request));
}
