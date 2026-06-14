using Anela.Heblo.Application.Features.FeatureFlags.Contracts;
using Anela.Heblo.Application.Features.FeatureFlags.UseCases.ClearFlagOverride;
using Anela.Heblo.Application.Features.FeatureFlags.UseCases.EvaluateFlagsForClient;
using Anela.Heblo.Application.Features.FeatureFlags.UseCases.ListFlags;
using Anela.Heblo.Application.Features.FeatureFlags.UseCases.UpsertFlagOverride;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/feature-flags")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class FeatureFlagsController : BaseApiController
{
    private readonly IMediator _mediator;

    public FeatureFlagsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(EvaluateFlagsForClientResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<EvaluateFlagsForClientResponse>> Get(CancellationToken ct)
        => HandleResponse(await _mediator.Send(new EvaluateFlagsForClientRequest(), ct));

    [HttpGet("admin")]
    [FeatureAuthorize(Feature.Admin_FeatureFlags, AccessLevel.Write)]
    [ProducesResponseType(typeof(ListFlagsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ListFlagsResponse>> GetAdmin(CancellationToken ct)
        => HandleResponse(await _mediator.Send(new ListFlagsRequest(), ct));

    [HttpPut("admin/{key}")]
    [FeatureAuthorize(Feature.Admin_FeatureFlags, AccessLevel.Write)]
    [ProducesResponseType(typeof(UpsertFlagOverrideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UpsertFlagOverrideResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UpsertFlagOverrideResponse>> Put(
        string key,
        [FromBody] UpsertFlagOverrideBodyDto body,
        CancellationToken ct)
    {
        var name = User.Identity?.Name;
        if (name is null)
            Logger.LogWarning("UpsertFlagOverride: User.Identity.Name resolved to null for authenticated request");
        var updatedBy = name ?? "unknown";
        return HandleResponse(await _mediator.Send(new UpsertFlagOverrideRequest
        {
            Key = key,
            IsEnabled = body.IsEnabled,
            UpdatedBy = updatedBy,
        }, ct));
    }

    [HttpDelete("admin/{key}")]
    [FeatureAuthorize(Feature.Admin_FeatureFlags, AccessLevel.Write)]
    [ProducesResponseType(typeof(ClearFlagOverrideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ClearFlagOverrideResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ClearFlagOverrideResponse>> Delete(string key, CancellationToken ct)
        => HandleResponse(await _mediator.Send(new ClearFlagOverrideRequest { Key = key }, ct));
}
