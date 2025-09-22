using Anela.Heblo.Xcc.Services;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BackgroundJobsController : ControllerBase
{
    private readonly IBackgroundWorker _backgroundWorker;
    private readonly ILogger<BackgroundJobsController> _logger;

    public BackgroundJobsController(IBackgroundWorker backgroundWorker, ILogger<BackgroundJobsController> logger)
    {
        _backgroundWorker = backgroundWorker;
        _logger = logger;
    }

    [HttpGet("queued")]
    public async Task<ActionResult<QueuedJobsResult>> GetQueuedJobs([FromQuery] GetQueuedJobsRequest request)
    {
        try
        {
            var result = await _backgroundWorker.GetQueuedJobsAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving queued jobs");
            return StatusCode(500, "Error retrieving queued jobs");
        }
    }

    [HttpGet("scheduled")]
    public async Task<ActionResult<QueuedJobsResult>> GetScheduledJobs([FromQuery] GetScheduledJobsRequest request)
    {
        try
        {
            var result = await _backgroundWorker.GetScheduledJobsAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving scheduled jobs");
            return StatusCode(500, "Error retrieving scheduled jobs");
        }
    }

    [HttpGet("{jobId}")]
    public async Task<ActionResult<BackgroundJobInfo>> GetJob(string jobId, [FromQuery] bool includeHistory = false)
    {
        try
        {
            var request = new GetJobRequest
            {
                JobId = jobId,
                IncludeHistory = includeHistory
            };
            
            var job = await _backgroundWorker.GetJobAsync(request);
            if (job == null)
            {
                return NotFound($"Job with ID {jobId} not found");
            }
            
            return Ok(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving job {JobId}", jobId);
            return StatusCode(500, "Error retrieving job");
        }
    }

    [HttpGet("failed")]
    public async Task<ActionResult<QueuedJobsResult>> GetFailedJobs([FromQuery] GetFailedJobsRequest request)
    {
        try
        {
            var result = await _backgroundWorker.GetFailedJobsAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving failed jobs");
            return StatusCode(500, "Error retrieving failed jobs");
        }
    }
}