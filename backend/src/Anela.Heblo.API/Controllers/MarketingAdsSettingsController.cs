using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdSettings;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SetAdKillSwitch;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdAutonomySetting;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdLimitSetting;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>Marketing → ad guardrails: autonomy modes, limits and the execution kill switch.</summary>
[FeatureAuthorize(Feature.Marketing_Ads)]
[ApiController]
[Route("api/marketing-ads/settings")]
public class MarketingAdsSettingsController : BaseApiController
{
    private readonly IMediator _mediator;

    public MarketingAdsSettingsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(GetAdSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GetAdSettingsResponse>> GetSettings() =>
        HandleResponse(await _mediator.Send(new GetAdSettingsRequest()));

    [HttpPut("autonomy")]
    [FeatureAuthorize(Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    [ProducesResponseType(typeof(UpdateAdAutonomySettingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UpdateAdAutonomySettingResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(UpdateAdAutonomySettingResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(UpdateAdAutonomySettingResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UpdateAdAutonomySettingResponse>> UpdateAutonomy([FromBody] UpdateAdAutonomySettingRequest request) =>
        HandleResponse(await _mediator.Send(request));

    [HttpPut("limits")]
    [FeatureAuthorize(Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    [ProducesResponseType(typeof(UpdateAdLimitSettingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UpdateAdLimitSettingResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(UpdateAdLimitSettingResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(UpdateAdLimitSettingResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UpdateAdLimitSettingResponse>> UpdateLimit([FromBody] UpdateAdLimitSettingRequest request) =>
        HandleResponse(await _mediator.Send(request));

    [HttpPut("kill-switch")]
    [FeatureAuthorize(Feature.Marketing_AdApprovals, AccessLevel.Admin)]
    [ProducesResponseType(typeof(SetAdKillSwitchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SetAdKillSwitchResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SetAdKillSwitchResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(SetAdKillSwitchResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SetAdKillSwitchResponse>> SetKillSwitch([FromBody] SetAdKillSwitchRequest request) =>
        HandleResponse(await _mediator.Send(request));
}
