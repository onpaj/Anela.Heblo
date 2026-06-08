using Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting;
using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Warehouse_Logistics)]
[ApiController]
[Route("api/gift-settings")]
public class GiftSettingsController : BaseApiController
{
    private readonly IMediator _mediator;

    public GiftSettingsController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [HttpGet]
    public async Task<IActionResult> GetGiftSetting(CancellationToken cancellationToken = default)
    {
        var dto = await _mediator.Send(new GetGiftSettingQuery(), cancellationToken);
        return Ok(dto);
    }

    [HttpPut]
    [FeatureAuthorize(Feature.Warehouse_Logistics, AccessLevel.Write)]
    public async Task<IActionResult> SetGiftSetting(
        [FromBody] SetGiftSettingCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(command, cancellationToken);
        if (response.Success) return NoContent();
        if (response.ErrorCode == ErrorCodes.Unauthorized) return Unauthorized(response);
        return BadRequest(response);
    }
}
