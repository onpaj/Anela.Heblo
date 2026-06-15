using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJob;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJobsList;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobStatus;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobCron;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>
/// Controller for managing recurring background jobs.
/// Listing and editing the schedule require <see cref="Feature.Admin_Administration"/>;
/// running a job requires <see cref="Feature.Jobs_Trigger"/> and enabling/disabling it
/// requires <see cref="Feature.Jobs_Disable"/> — both granted independently of administration.
/// </summary>
[ApiController]
[Route("api/[controller]")]
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
    [FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Read)]
    [ProducesResponseType(typeof(GetRecurringJobsListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<GetRecurringJobsListResponse>> GetRecurringJobs()
    {
        var request = new GetRecurringJobsListRequest();
        var response = await _mediator.Send(request);

        return HandleResponse(response);
    }

    /// <summary>
    /// Get a single recurring job by name. Readable by holders of the job trigger or
    /// disable permission (so shortcut buttons can show current status / next run) as
    /// well as administrators.
    /// </summary>
    /// <param name="jobName">The name of the job to fetch</param>
    /// <returns>The recurring job's current status</returns>
    [HttpGet("{jobName}")]
    [FeatureAuthorize(Feature.Jobs_Trigger, Feature.Jobs_Disable, Feature.Admin_Administration)]
    [ProducesResponseType(typeof(GetRecurringJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetRecurringJobResponse>> GetRecurringJob(string jobName)
    {
        var request = new GetRecurringJobRequest { JobName = jobName };
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
    [FeatureAuthorize(Feature.Jobs_Disable)]
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

    /// <summary>
    /// Update the CRON schedule of a recurring job
    /// </summary>
    /// <param name="jobName">The name of the job to update</param>
    /// <param name="request">The CRON update request containing the new expression</param>
    /// <returns>Updated job information with new CRON expression</returns>
    [HttpPut("{jobName}/cron")]
    [FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]
    [ProducesResponseType(typeof(UpdateRecurringJobCronResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UpdateRecurringJobCronResponse>> UpdateJobCron(
        string jobName,
        [FromBody] UpdateJobCronRequestBody request)
    {
        var mediatrRequest = new UpdateRecurringJobCronRequest
        {
            JobName = jobName,
            CronExpression = request.CronExpression
        };

        var response = await _mediator.Send(mediatrRequest);

        return HandleResponse(response);
    }

    /// <summary>
    /// Manually trigger a recurring job to run immediately
    /// </summary>
    /// <param name="jobName">The name of the job to trigger</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Background job ID if triggered successfully</returns>
    [HttpPost("{jobName}/trigger")]
    [FeatureAuthorize(Feature.Jobs_Trigger)]
    [ProducesResponseType(typeof(TriggerRecurringJobResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<TriggerRecurringJobResponse>> TriggerJob(
        string jobName,
        CancellationToken cancellationToken = default)
    {
        var request = new TriggerRecurringJobRequest
        {
            JobName = jobName
        };

        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            return HandleResponse(response);
        }

        return Accepted(response);
    }
}
