using Anela.Heblo.Application.Features.Campaigns;
using Anela.Heblo.Application.Features.Campaigns.GetCampaignDashboard;
using Anela.Heblo.Application.Features.Campaigns.GetCampaignDetail;
using Anela.Heblo.Application.Features.Campaigns.GetCampaignList;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = AuthorizationConstants.Roles.HebloUser)]
public class CampaignsController : BaseApiController
{
    private readonly IMediator _mediator;

    public CampaignsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(CampaignDashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CampaignDashboardDto>> GetDashboard(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] AdPlatform? platform = null)
    {
        var result = await _mediator.Send(new GetCampaignDashboardRequest(from, to, platform));
        return Ok(result);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CampaignSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<CampaignSummaryDto>>> GetCampaigns(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] AdPlatform? platform = null)
    {
        var result = await _mediator.Send(new GetCampaignListRequest(from, to, platform));
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CampaignDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CampaignDetailDto>> GetCampaignDetail(
        Guid id,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to)
    {
        try
        {
            var result = await _mediator.Send(new GetCampaignDetailRequest(id, from, to));
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("sync")]
    [Authorize(Roles = AuthorizationConstants.Roles.Administrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SyncMetaAds()
    {
        await _mediator.Send(new SyncMetaAdsRequest());
        return NoContent();
    }
}
