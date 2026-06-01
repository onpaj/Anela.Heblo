using System.Security.Claims;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetAvailableTiles;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;
using Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;
using Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : BaseApiController
{
    private readonly IMediator _mediator;

    public DashboardController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("tiles")]
    public async Task<ActionResult<IEnumerable<Application.Features.Dashboard.Contracts.DashboardTileDto>>> GetAvailableTiles()
    {
        var request = new GetAvailableTilesRequest();
        var response = await _mediator.Send(request);

        return Ok(response.Tiles);
    }

    [HttpGet("settings")]
    public async Task<ActionResult<Application.Features.Dashboard.Contracts.UserDashboardSettingsDto>> GetUserSettings()
    {
        var userId = GetCurrentUserId();
        var request = new GetUserSettingsRequest { UserId = userId };
        var response = await _mediator.Send(request);

        return Ok(response.Settings);
    }

    [HttpPost("settings")]
    public async Task<ActionResult> SaveUserSettings([FromBody] SaveUserSettingsRequest request)
    {
        var userId = GetCurrentUserId();
        request.UserId = userId;
        await _mediator.Send(request);

        return Ok();
    }

    [HttpGet("data")]
    public async Task<ActionResult<IEnumerable<Application.Features.Dashboard.Contracts.DashboardTileDto>>> GetTileData([FromQuery] Dictionary<string, string>? tileParameters = null)
    {
        var userId = GetCurrentUserId();
        var request = new GetTileDataRequest
        {
            UserId = userId,
            TileParameters = tileParameters
        };
        var response = await _mediator.Send(request);

        return Ok(response.Tiles);
    }

    [HttpPost("tiles/{tileId}/enable")]
    public async Task<ActionResult> EnableTile(string tileId)
    {
        var userId = GetCurrentUserId();
        var request = new EnableTileRequest
        {
            UserId = userId,
            TileId = tileId
        };
        await _mediator.Send(request);

        return Ok();
    }

    [HttpPost("tiles/{tileId}/disable")]
    public async Task<ActionResult> DisableTile(string tileId)
    {
        var userId = GetCurrentUserId();
        var request = new DisableTileRequest
        {
            UserId = userId,
            TileId = tileId
        };
        await _mediator.Send(request);

        return Ok();
    }

    private string GetCurrentUserId()
    {
        // Get user ID from authentication claims
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value
                     ?? User.FindFirst("oid")?.Value
                     ?? throw new Exception("User not found");
        return userId;
    }
}