using Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting;
using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
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
    public async Task<IActionResult> SetGiftSetting(
        [FromBody] SetGiftSettingCommand command,
        CancellationToken cancellationToken = default)
    {
        command.ModifiedBy = GetCurrentUserId();
        var response = await _mediator.Send(command, cancellationToken);
        if (response.Success) return NoContent();
        return BadRequest(response);
    }
}
