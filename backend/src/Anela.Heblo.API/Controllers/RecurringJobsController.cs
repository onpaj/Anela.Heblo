using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJobsList;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobStatus;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>
/// Controller for managing recurring background jobs
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RecurringJobsController : BaseApiController
{
    private readonly IMediator _mediator;

    public RecurringJobsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get list of all recurring jobs with their current status
    /// </summary>
    /// <returns>List of recurring jobs</returns>
    [HttpGet]
    [ProducesResponseType(typeof(GetRecurringJobsListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<GetRecurringJobsListResponse>> GetRecurringJobs()
    {
        var request = new GetRecurringJobsListRequest();
        var response = await _mediator.Send(request);

        return HandleResponse(response);
    }

    /// <summary>
    /// Update the enabled/disabled status of a recurring job
    /// </summary>
    /// <param name="jobName">The name of the job to update</param>
    /// <param name="request">The status update request containing the new enabled state</param>
    /// <returns>Updated job information</returns>
    [HttpPut("{jobName}/status")]
    [ProducesResponseType(typeof(UpdateRecurringJobStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UpdateRecurringJobStatusResponse>> UpdateJobStatus(
        string jobName,
        [FromBody] UpdateJobStatusRequestBody request)
    {
        var mediatrRequest = new UpdateRecurringJobStatusRequest
        {
            JobName = jobName,
            IsEnabled = request.IsEnabled
        };

        var response = await _mediator.Send(mediatrRequest);

        return HandleResponse(response);
    }
}

/// <summary>
/// Request body for updating recurring job status
/// </summary>
public class UpdateJobStatusRequestBody
{
    /// <summary>
    /// Whether the job should be enabled
    /// </summary>
    public bool IsEnabled { get; set; }
}
