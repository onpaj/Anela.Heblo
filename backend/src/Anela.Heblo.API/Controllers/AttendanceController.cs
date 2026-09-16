using Anela.Heblo.Application.Features.Attendance.UseCases.RunBreakInsertion;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>
/// On-demand entry points for the Logeto attendance jobs. Running one is gated by
/// <see cref="Feature.Jobs_Trigger"/>, the same permission as the generic job trigger.
/// </summary>
[ApiController]
[Route("api/attendance")]
[FeatureAuthorize(Feature.Jobs_Trigger)]
public class AttendanceController : BaseApiController
{
    private readonly IMediator _mediator;

    public AttendanceController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <summary>
    /// Runs the break-insertion walk immediately and returns what it did. Unlike the generic job
    /// trigger this takes an explicit window, so history can be swept in steps without changing the
    /// nightly schedule. The walk runs synchronously; see
    /// <see cref="RunBreakInsertionValidator.MaxWindowDays"/> for the per-call limit.
    ///
    /// Refused with 409 when the job is disabled, or when another walk is already in flight.
    /// </summary>
    [HttpPost("break-insertion/run")]
    [ProducesResponseType(typeof(RunBreakInsertionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RunBreakInsertionResponse>> RunBreakInsertion(
        [FromBody] RunBreakInsertionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);

        return HandleResponse(response);
    }
}
