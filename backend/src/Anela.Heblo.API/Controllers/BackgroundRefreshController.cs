using Anela.Heblo.Application.Common.Cache;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BackgroundRefreshController : ControllerBase
{
    private readonly ILogger<BackgroundRefreshController> _logger;
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;

    public BackgroundRefreshController(
        ILogger<BackgroundRefreshController> logger,
        IBackgroundRefreshTaskRegistry taskRegistry)
    {
        _logger = logger;
        _taskRegistry = taskRegistry;
    }

    [HttpGet("tasks")]
    public ActionResult<IEnumerable<RefreshTaskDto>> GetRegisteredTasks()
    {
        var tasks = _taskRegistry.GetRegisteredTasks()
            .Select(MapToDto)
            .ToList();

        return Ok(tasks);
    }

    [HttpGet("tasks/{taskId}/history")]
    public ActionResult<IEnumerable<RefreshTaskExecutionLogDto>> GetTaskHistory(
        string taskId,
        [FromQuery] int maxRecords = 50)
    {
        var history = _taskRegistry.GetExecutionHistory(taskId, maxRecords)
            .Select(MapToDto)
            .ToList();

        return Ok(history);
    }

    [HttpGet("history")]
    public ActionResult<IEnumerable<RefreshTaskExecutionLogDto>> GetAllHistory([FromQuery] int maxRecords = 100)
    {
        var history = _taskRegistry.GetExecutionHistory(null, maxRecords)
            .Select(MapToDto)
            .ToList();

        return Ok(history);
    }

    [HttpPost("tasks/{taskId}/force-refresh")]
    public async Task<ActionResult> ForceRefresh(string taskId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Force refresh requested for task '{TaskId}' by user", taskId);

            await _taskRegistry.ForceRefreshAsync(taskId, cancellationToken);

            return Ok(new { Message = $"Task '{taskId}' refresh initiated successfully" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Force refresh failed for task '{TaskId}': {Error}", taskId, ex.Message);
            return NotFound(new { Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during force refresh of task '{TaskId}'", taskId);
            return StatusCode(500, new { Error = "An unexpected error occurred during force refresh" });
        }
    }

    [HttpGet("tasks/{taskId}/status")]
    public ActionResult<RefreshTaskStatusDto> GetTaskStatus(string taskId)
    {
        var task = _taskRegistry.GetRegisteredTasks()
            .FirstOrDefault(t => t.TaskId == taskId);

        if (task == null)
        {
            return NotFound(new { Error = $"Task '{taskId}' not found" });
        }

        var lastExecution = _taskRegistry.GetLastExecution(taskId);

        var status = new RefreshTaskStatusDto
        {
            TaskId = taskId,
            Enabled = task.Enabled,
            RefreshInterval = task.RefreshInterval,
            LastExecution = lastExecution != null ? MapToDto(lastExecution) : null
        };

        return Ok(status);
    }

    private static RefreshTaskDto MapToDto(RefreshTaskConfiguration task)
    {
        return new RefreshTaskDto
        {
            TaskId = task.TaskId,
            InitialDelay = task.InitialDelay,
            RefreshInterval = task.RefreshInterval,
            Enabled = task.Enabled,
        };
    }

    private static RefreshTaskExecutionLogDto MapToDto(RefreshTaskExecutionLog log)
    {
        return new RefreshTaskExecutionLogDto
        {
            TaskId = log.TaskId,
            StartedAt = log.StartedAt,
            CompletedAt = log.CompletedAt,
            Status = log.Status.ToString(),
            ErrorMessage = log.ErrorMessage,
            Duration = log.Duration,
            Metadata = log.Metadata
        };
    }
}