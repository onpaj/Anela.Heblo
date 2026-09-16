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
public class AttendanceController : BaseApiController
{
    private readonly IMediator _mediator;

    public AttendanceController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <summary>
    /// Runs the break-insertion walk immediately and returns what it did. Unlike the generic job
    /// trigger this takes a lookback, so history can be swept without changing the nightly schedule.
    /// The walk runs synchronously; see <see cref="RunBreakInsertionValidator.MaxLookbackDays"/>
    /// for the per-call limit.
    /// </summary>
    [HttpPost("break-insertion/run")]
    [FeatureAuthorize(Feature.Jobs_Trigger)]
    [ProducesResponseType(typeof(RunBreakInsertionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RunBreakInsertionResponse>> RunBreakInsertion(
        [FromBody] RunBreakInsertionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);

        return HandleResponse(response);
    }
}
