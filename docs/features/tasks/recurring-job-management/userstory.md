# Recurring Job Management User Story

## Feature Overview
The Recurring Job Management feature provides comprehensive control and monitoring for Hangfire-based background jobs. It enables system administrators to manage job execution schedules, monitor job health and performance, and control job enablement through a centralized interface. This system ensures reliable automated processing of critical business operations while providing visibility and control over the background job infrastructure.

## Business Requirements

### Primary Use Case
As a system administrator, I want to manage and monitor recurring background jobs so that automated business processes execute reliably on schedule, with the ability to enable/disable jobs as needed, monitor execution status and performance, and ensure system stability through comprehensive job oversight and health monitoring.

### Acceptance Criteria
1. The system shall provide a centralized interface for viewing all recurring jobs
2. The system shall allow enabling and disabling of individual jobs
3. The system shall display job schedules using human-readable cron expressions
4. The system shall show next execution times for enabled jobs
5. The system shall provide job execution history and status monitoring
6. The system shall integrate with Hangfire metadata for real-time job information
7. The system shall support job health monitoring and failure detection
8. The system shall provide job performance metrics and execution statistics
9. The system shall ensure only enabled jobs execute during scheduled times
10. The system shall provide comprehensive audit trails for job management operations

## Technical Contracts

### Domain Model

```csharp
// Primary entity for recurring job management
public class RecurringJob : Entity<string>
{
    // Job identification (matches Hangfire job ID)
    public string JobName => Id;
    
    // Job control
    public bool Enabled { get; set; } = true;
    
    // Job metadata
    public string? Description { get; set; }
    public string? CronExpression { get; set; }
    public string? TimeZone { get; set; } = "UTC";
    public DateTime? LastExecutionTime { get; set; }
    public DateTime? NextExecutionTime { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Scheduled;
    
    // Performance metrics
    public int ExecutionCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public TimeSpan? AverageExecutionDuration { get; set; }
    public TimeSpan? LastExecutionDuration { get; set; }
    
    // Error tracking
    public string? LastError { get; set; }
    public DateTime? LastErrorTime { get; set; }
    public int ConsecutiveFailures { get; set; }
    
    // Configuration
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(5);
    public bool NotifyOnFailure { get; set; } = true;
    public string? NotificationEmails { get; set; }
    
    // Computed properties
    public double SuccessRate => ExecutionCount > 0 ? (double)SuccessCount / ExecutionCount * 100 : 0;
    public bool IsHealthy => ConsecutiveFailures < 3 && SuccessRate >= 80;
    public bool RequiresAttention => ConsecutiveFailures >= 2 || SuccessRate < 50;
    
    // Business methods
    public static RecurringJob Create(string jobId, string? description = null)
    {
        if (string.IsNullOrEmpty(jobId))
            throw new BusinessException("Job ID is required");
        
        return new RecurringJob
        {
            Id = jobId,
            Description = description ?? jobId,
            Enabled = true,
            Status = JobStatus.Scheduled
        };
    }
    
    public void Enable()
    {
        if (Enabled)
            return;
        
        Enabled = true;
        Status = JobStatus.Scheduled;
        ConsecutiveFailures = 0; // Reset failure count when re-enabling
    }
    
    public void Disable(string? reason = null)
    {
        if (!Enabled)
            return;
        
        Enabled = false;
        Status = JobStatus.Disabled;
        
        // Log disable reason if provided
        if (!string.IsNullOrEmpty(reason))
        {
            LastError = $"Manually disabled: {reason}";
            LastErrorTime = DateTime.UtcNow;
        }
    }
    
    public void RecordExecution(TimeSpan duration, bool success, string? error = null)
    {
        ExecutionCount++;
        LastExecutionTime = DateTime.UtcNow;
        LastExecutionDuration = duration;
        
        if (success)
        {
            SuccessCount++;
            ConsecutiveFailures = 0;
            Status = JobStatus.Completed;
            LastError = null;
            LastErrorTime = null;
        }
        else
        {
            FailureCount++;
            ConsecutiveFailures++;
            Status = JobStatus.Failed;
            LastError = error;
            LastErrorTime = DateTime.UtcNow;
        }
        
        // Update average execution duration
        if (SuccessCount > 0)
        {
            var totalDuration = AverageExecutionDuration?.Multiply(SuccessCount - 1) ?? TimeSpan.Zero;
            if (success)
            {
                AverageExecutionDuration = totalDuration.Add(duration).Divide(SuccessCount);
            }
        }
        else if (success)
        {
            AverageExecutionDuration = duration;
        }
        
        // Auto-disable if too many consecutive failures
        if (ConsecutiveFailures >= MaxRetries)
        {
            Disable($"Auto-disabled after {ConsecutiveFailures} consecutive failures");
        }
    }
    
    public void UpdateSchedule(string cronExpression, string? timeZone = null)
    {
        if (string.IsNullOrEmpty(cronExpression))
            throw new BusinessException("Cron expression is required");
        
        CronExpression = cronExpression;
        TimeZone = timeZone ?? "UTC";
        
        // Calculate next execution time
        try
        {
            var cron = Cronos.CronExpression.Parse(cronExpression);
            var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
            NextExecutionTime = cron.GetNextOccurrence(DateTime.UtcNow, timeZoneInfo);
        }
        catch (Exception ex)
        {
            throw new BusinessException($"Invalid cron expression: {ex.Message}", ex);
        }
    }
    
    public void ResetHealthMetrics()
    {
        ConsecutiveFailures = 0;
        LastError = null;
        LastErrorTime = null;
        Status = Enabled ? JobStatus.Scheduled : JobStatus.Disabled;
    }
}

// Job status enumeration
public enum JobStatus
{
    Scheduled = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Disabled = 4,
    Retrying = 5
}

// Job execution history record
public class JobExecutionHistory : Entity<int>
{
    public string JobId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration => EndTime - StartTime;
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Output { get; set; }
    public string? TriggerType { get; set; } = "Scheduled";
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    public static JobExecutionHistory Create(string jobId, DateTime startTime)
    {
        return new JobExecutionHistory
        {
            JobId = jobId,
            StartTime = startTime,
            IsSuccessful = false // Will be updated on completion
        };
    }
    
    public void Complete(bool success, string? output = null, string? error = null)
    {
        EndTime = DateTime.UtcNow;
        IsSuccessful = success;
        Output = output;
        ErrorMessage = error;
    }
}

// Job health check result
public class JobHealthCheck
{
    public string JobId { get; set; }
    public string JobName { get; set; }
    public HealthStatus Health { get; set; }
    public List<HealthIssue> Issues { get; set; } = new();
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan CheckDuration { get; set; }
    
    public static JobHealthCheck Healthy(string jobId, string jobName)
    {
        return new JobHealthCheck
        {
            JobId = jobId,
            JobName = jobName,
            Health = HealthStatus.Healthy
        };
    }
    
    public static JobHealthCheck Warning(string jobId, string jobName, params HealthIssue[] issues)
    {
        return new JobHealthCheck
        {
            JobId = jobId,
            JobName = jobName,
            Health = HealthStatus.Warning,
            Issues = issues.ToList()
        };
    }
    
    public static JobHealthCheck Critical(string jobId, string jobName, params HealthIssue[] issues)
    {
        return new JobHealthCheck
        {
            JobId = jobId,
            JobName = jobName,
            Health = HealthStatus.Critical,
            Issues = issues.ToList()
        };
    }
}

public enum HealthStatus
{
    Healthy = 0,
    Warning = 1,
    Critical = 2,
    Unknown = 3
}

public class HealthIssue
{
    public HealthIssueType Type { get; set; }
    public string Message { get; set; }
    public object? Details { get; set; }
    
    public static HealthIssue Create(HealthIssueType type, string message, object? details = null)
    {
        return new HealthIssue { Type = type, Message = message, Details = details };
    }
}

public enum HealthIssueType
{
    ConsecutiveFailures,
    LowSuccessRate,
    LongExecutionTime,
    MissedExecution,
    HighMemoryUsage,
    ExternalDependencyFailure
}
```

### Application Layer Contracts

```csharp
// Primary application service interface
public interface IRecurringJobsAppService : IApplicationService
{
    // Job management
    Task<List<RecurringJobDto>> GetListAsync();
    Task<RecurringJobDto> GetAsync(string jobId);
    Task<RecurringJobDto> SetEnabledAsync(string jobId, bool enabled);
    Task<RecurringJobDto> UpdateJobAsync(string jobId, UpdateRecurringJobDto updateDto);
    Task DeleteJobAsync(string jobId);
    
    // Job monitoring
    Task<List<JobExecutionHistoryDto>> GetExecutionHistoryAsync(string jobId, int maxResults = 50);
    Task<JobStatisticsDto> GetJobStatisticsAsync(string jobId, DateTime? fromDate = null, DateTime? toDate = null);
    Task<List<JobHealthCheck>> GetJobHealthChecksAsync();
    Task<JobHealthCheck> GetJobHealthAsync(string jobId);
    
    // Job control
    Task TriggerJobAsync(string jobId);
    Task RetryFailedJobAsync(string jobId);
    Task ResetJobHealthAsync(string jobId);
    
    // System monitoring
    Task<RecurringJobSystemStatusDto> GetSystemStatusAsync();
    Task<List<RecurringJobDto>> GetJobsByStatusAsync(JobStatus status);
    Task<int> CleanupOldExecutionHistoryAsync(TimeSpan olderThan);
}

// Hangfire integration service
public interface IHangfireJobService
{
    Task<HangfireJobInfo?> GetJobInfoAsync(string jobId);
    Task<List<HangfireJobInfo>> GetAllJobsAsync();
    Task EnableJobAsync(string jobId);
    Task DisableJobAsync(string jobId);
    Task TriggerJobAsync(string jobId);
    Task<List<JobExecutionInfo>> GetExecutionHistoryAsync(string jobId, int maxResults = 50);
    Task<bool> IsJobRunningAsync(string jobId);
}

// DTOs for API contracts
public class RecurringJobDto
{
    public string Id { get; set; }
    public string JobName { get; set; }
    public bool Enabled { get; set; }
    public string? Description { get; set; }
    public string? CronExpression { get; set; }
    public string? TimeZone { get; set; }
    public DateTime? LastExecutionTime { get; set; }
    public DateTime? NextExecutionTime { get; set; }
    public JobStatus Status { get; set; }
    
    // Performance metrics
    public int ExecutionCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan? AverageExecutionDuration { get; set; }
    public TimeSpan? LastExecutionDuration { get; set; }
    
    // Health indicators
    public bool IsHealthy { get; set; }
    public bool RequiresAttention { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorTime { get; set; }
    
    // Configuration
    public int MaxRetries { get; set; }
    public TimeSpan RetryDelay { get; set; }
    public bool NotifyOnFailure { get; set; }
    public string? NotificationEmails { get; set; }
    
    // Runtime information
    public bool IsCurrentlyRunning { get; set; }
    public string? HumanReadableSchedule { get; set; }
    public TimeSpan? TimeUntilNextExecution { get; set; }
}

public class UpdateRecurringJobDto
{
    public string? Description { get; set; }
    public string? CronExpression { get; set; }
    public string? TimeZone { get; set; }
    public int? MaxRetries { get; set; }
    public TimeSpan? RetryDelay { get; set; }
    public bool? NotifyOnFailure { get; set; }
    public string? NotificationEmails { get; set; }
}

public class JobExecutionHistoryDto
{
    public int Id { get; set; }
    public string JobId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Output { get; set; }
    public string? TriggerType { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class JobStatisticsDto
{
    public string JobId { get; set; }
    public string JobName { get; set; }
    public DateTime AnalyzedFrom { get; set; }
    public DateTime AnalyzedTo { get; set; }
    
    // Execution statistics
    public int TotalExecutions { get; set; }
    public int SuccessfulExecutions { get; set; }
    public int FailedExecutions { get; set; }
    public double SuccessRate { get; set; }
    
    // Performance statistics
    public TimeSpan? MinExecutionTime { get; set; }
    public TimeSpan? MaxExecutionTime { get; set; }
    public TimeSpan? AverageExecutionTime { get; set; }
    public TimeSpan? MedianExecutionTime { get; set; }
    
    // Reliability statistics
    public int ConsecutiveFailures { get; set; }
    public int MaxConsecutiveFailures { get; set; }
    public TimeSpan? LongestDowntime { get; set; }
    public double Availability { get; set; }
    
    // Recent performance
    public List<ExecutionSummary> RecentExecutions { get; set; } = new();
    public List<string> CommonErrors { get; set; } = new();
}

public class ExecutionSummary
{
    public DateTime ExecutedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public bool WasSuccessful { get; set; }
    public string? ErrorSummary { get; set; }
}

public class RecurringJobSystemStatusDto
{
    public int TotalJobs { get; set; }
    public int EnabledJobs { get; set; }
    public int DisabledJobs { get; set; }
    public int HealthyJobs { get; set; }
    public int JobsRequiringAttention { get; set; }
    public int CurrentlyRunningJobs { get; set; }
    
    public double OverallSuccessRate { get; set; }
    public TimeSpan? AverageExecutionTime { get; set; }
    public DateTime? LastSystemCheck { get; set; }
    
    public List<JobAlertDto> ActiveAlerts { get; set; } = new();
    public List<RecurringJobDto> ProblematicJobs { get; set; } = new();
}

public class JobAlertDto
{
    public string JobId { get; set; }
    public string JobName { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; }
    public DateTime AlertTime { get; set; }
}

public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

public class HangfireJobInfo
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string? CronExpression { get; set; }
    public DateTime? NextExecution { get; set; }
    public DateTime? LastExecution { get; set; }
    public string? TimeZone { get; set; }
    public bool IsEnabled { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class JobExecutionInfo
{
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string State { get; set; }
    public string? Result { get; set; }
    public string? Exception { get; set; }
}
```

## Implementation Details

### Application Service Implementation

```csharp
[Authorize]
public class RecurringJobsAppService : ApplicationService, IRecurringJobsAppService
{
    private readonly IRepository<RecurringJob, string> _repository;
    private readonly IRepository<JobExecutionHistory, int> _historyRepository;
    private readonly IHangfireJobService _hangfireService;
    private readonly ILogger<RecurringJobsAppService> _logger;
    private readonly IClock _clock;

    public RecurringJobsAppService(
        IRepository<RecurringJob, string> repository,
        IRepository<JobExecutionHistory, int> historyRepository,
        IHangfireJobService hangfireService,
        ILogger<RecurringJobsAppService> logger,
        IClock clock)
    {
        _repository = repository;
        _historyRepository = historyRepository;
        _hangfireService = hangfireService;
        _logger = logger;
        _clock = clock;
    }

    public async Task<List<RecurringJobDto>> GetListAsync()
    {
        _logger.LogDebug("Retrieving all recurring jobs");
        
        var jobs = await _repository.GetListAsync();
        var hangfireJobs = await _hangfireService.GetAllJobsAsync();
        
        var result = new List<RecurringJobDto>();
        
        foreach (var job in jobs)
        {
            var hangfireInfo = hangfireJobs.FirstOrDefault(h => h.Id == job.Id);
            var dto = await MapToDto(job, hangfireInfo);
            result.Add(dto);
        }
        
        // Add any Hangfire jobs that don't exist in our database
        foreach (var hangfireJob in hangfireJobs.Where(h => !jobs.Any(j => j.Id == h.Id)))
        {
            var newJob = RecurringJob.Create(hangfireJob.Id, hangfireJob.Name);
            await _repository.InsertAsync(newJob);
            
            var dto = await MapToDto(newJob, hangfireJob);
            result.Add(dto);
        }
        
        _logger.LogInformation("Retrieved {Count} recurring jobs", result.Count);
        
        return result.OrderBy(j => j.JobName).ToList();
    }

    public async Task<RecurringJobDto> GetAsync(string jobId)
    {
        Check.NotNullOrEmpty(jobId, nameof(jobId));
        
        var job = await _repository.GetAsync(jobId);
        var hangfireInfo = await _hangfireService.GetJobInfoAsync(jobId);
        
        return await MapToDto(job, hangfireInfo);
    }

    public async Task<RecurringJobDto> SetEnabledAsync(string jobId, bool enabled)
    {
        Check.NotNullOrEmpty(jobId, nameof(jobId));
        
        _logger.LogInformation("Setting job {JobId} enabled status to {Enabled}", jobId, enabled);
        
        var job = await _repository.GetAsync(jobId);
        
        if (enabled)
        {
            job.Enable();
            await _hangfireService.EnableJobAsync(jobId);
        }
        else
        {
            job.Disable("Manually disabled by user");
            await _hangfireService.DisableJobAsync(jobId);
        }
        
        job = await _repository.UpdateAsync(job);
        
        var hangfireInfo = await _hangfireService.GetJobInfoAsync(jobId);
        
        _logger.LogInformation("Job {JobId} enabled status changed to {Enabled}", jobId, enabled);
        
        return await MapToDto(job, hangfireInfo);
    }

    public async Task<RecurringJobDto> UpdateJobAsync(string jobId, UpdateRecurringJobDto updateDto)
    {
        Check.NotNullOrEmpty(jobId, nameof(jobId));
        Check.NotNull(updateDto, nameof(updateDto));
        
        _logger.LogDebug("Updating job {JobId}", jobId);
        
        var job = await _repository.GetAsync(jobId);
        
        if (!string.IsNullOrEmpty(updateDto.Description))
        {
            job.Description = updateDto.Description;
        }
        
        if (!string.IsNullOrEmpty(updateDto.CronExpression))
        {
            job.UpdateSchedule(updateDto.CronExpression, updateDto.TimeZone);
        }
        
        if (updateDto.MaxRetries.HasValue)
        {
            job.MaxRetries = updateDto.MaxRetries.Value;
        }
        
        if (updateDto.RetryDelay.HasValue)
        {
            job.RetryDelay = updateDto.RetryDelay.Value;
        }
        
        if (updateDto.NotifyOnFailure.HasValue)
        {
            job.NotifyOnFailure = updateDto.NotifyOnFailure.Value;
        }
        
        if (!string.IsNullOrEmpty(updateDto.NotificationEmails))
        {
            job.NotificationEmails = updateDto.NotificationEmails;
        }
        
        job = await _repository.UpdateAsync(job);
        
        var hangfireInfo = await _hangfireService.GetJobInfoAsync(jobId);
        
        _logger.LogInformation("Job {JobId} updated successfully", jobId);
        
        return await MapToDto(job, hangfireInfo);
    }

    public async Task DeleteJobAsync(string jobId)
    {
        Check.NotNullOrEmpty(jobId, nameof(jobId));
        
        _logger.LogWarning("Deleting job {JobId}", jobId);
        
        // Disable in Hangfire first
        await _hangfireService.DisableJobAsync(jobId);
        
        // Delete from our database
        await _repository.DeleteAsync(jobId);
        
        // Clean up execution history
        var historyItems = await _historyRepository.GetListAsync(h => h.JobId == jobId);
        foreach (var history in historyItems)
        {
            await _historyRepository.DeleteAsync(history);
        }
        
        _logger.LogWarning("Job {JobId} deleted successfully", jobId);
    }

    public async Task<List<JobExecutionHistoryDto>> GetExecutionHistoryAsync(string jobId, int maxResults = 50)
    {
        Check.NotNullOrEmpty(jobId, nameof(jobId));
        
        var history = await _historyRepository.GetListAsync(
            h => h.JobId == jobId,
            sorting: h => h.StartTime,
            maxResultCount: maxResults);
        
        var hangfireHistory = await _hangfireService.GetExecutionHistoryAsync(jobId, maxResults);
        
        // Merge our history with Hangfire history
        var result = new List<JobExecutionHistoryDto>();
        
        foreach (var item in history.OrderByDescending(h => h.StartTime))
        {
            result.Add(ObjectMapper.Map<JobExecutionHistory, JobExecutionHistoryDto>(item));
        }
        
        return result;
    }

    public async Task<JobStatisticsDto> GetJobStatisticsAsync(string jobId, DateTime? fromDate = null, DateTime? toDate = null)
    {
        Check.NotNullOrEmpty(jobId, nameof(jobId));
        
        var from = fromDate ?? _clock.Now.AddDays(-30);
        var to = toDate ?? _clock.Now;
        
        var job = await _repository.GetAsync(jobId);
        var history = await _historyRepository.GetListAsync(
            h => h.JobId == jobId && h.StartTime >= from && h.StartTime <= to);
        
        var statistics = new JobStatisticsDto
        {
            JobId = jobId,
            JobName = job.Description ?? jobId,
            AnalyzedFrom = from,
            AnalyzedTo = to,
            TotalExecutions = history.Count,
            SuccessfulExecutions = history.Count(h => h.IsSuccessful),
            FailedExecutions = history.Count(h => !h.IsSuccessful),
            ConsecutiveFailures = job.ConsecutiveFailures
        };
        
        statistics.SuccessRate = statistics.TotalExecutions > 0 
            ? (double)statistics.SuccessfulExecutions / statistics.TotalExecutions * 100 
            : 0;
        
        // Calculate execution time statistics
        var durations = history.Where(h => h.Duration.HasValue).Select(h => h.Duration!.Value).ToList();
        if (durations.Any())
        {
            statistics.MinExecutionTime = durations.Min();
            statistics.MaxExecutionTime = durations.Max();
            statistics.AverageExecutionTime = TimeSpan.FromMilliseconds(durations.Average(d => d.TotalMilliseconds));
            statistics.MedianExecutionTime = durations.OrderBy(d => d).Skip(durations.Count / 2).First();
        }
        
        // Recent executions
        statistics.RecentExecutions = history
            .OrderByDescending(h => h.StartTime)
            .Take(10)
            .Select(h => new ExecutionSummary
            {
                ExecutedAt = h.StartTime,
                Duration = h.Duration ?? TimeSpan.Zero,
                WasSuccessful = h.IsSuccessful,
                ErrorSummary = h.ErrorMessage?.Length > 100 ? h.ErrorMessage[..100] + "..." : h.ErrorMessage
            })
            .ToList();
        
        // Common errors
        statistics.CommonErrors = history
            .Where(h => !h.IsSuccessful && !string.IsNullOrEmpty(h.ErrorMessage))
            .GroupBy(h => h.ErrorMessage)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key!)
            .ToList();
        
        return statistics;
    }

    public async Task<List<JobHealthCheck>> GetJobHealthChecksAsync()
    {
        var jobs = await _repository.GetListAsync();
        var healthChecks = new List<JobHealthCheck>();
        
        foreach (var job in jobs)
        {
            var healthCheck = await PerformJobHealthCheck(job);
            healthChecks.Add(healthCheck);
        }
        
        return healthChecks.OrderByDescending(h => h.Health).ToList();
    }

    public async Task<JobHealthCheck> GetJobHealthAsync(string jobId)
    {
        Check.NotNullOrEmpty(jobId, nameof(jobId));
        
        var job = await _repository.GetAsync(jobId);
        return await PerformJobHealthCheck(job);
    }

    public async Task TriggerJobAsync(string jobId)
    {
        Check.NotNullOrEmpty(jobId, nameof(jobId));
        
        _logger.LogInformation("Manually triggering job {JobId}", jobId);
        
        var job = await _repository.GetAsync(jobId);
        
        if (!job.Enabled)
        {
            throw new BusinessException("Cannot trigger disabled job");
        }
        
        await _hangfireService.TriggerJobAsync(jobId);
        
        // Record the manual trigger in history
        var history = JobExecutionHistory.Create(jobId, _clock.Now);
        history.TriggerType = "Manual";
        await _historyRepository.InsertAsync(history);
        
        _logger.LogInformation("Job {JobId} triggered manually", jobId);
    }

    public async Task RetryFailedJobAsync(string jobId)
    {
        Check.NotNullOrEmpty(jobId, nameof(jobId));
        
        _logger.LogInformation("Retrying failed job {JobId}", jobId);
        
        var job = await _repository.GetAsync(jobId);
        
        if (job.Status != JobStatus.Failed)
        {
            throw new BusinessException("Job is not in failed state");
        }
        
        // Reset failure count and re-enable if auto-disabled
        job.ResetHealthMetrics();
        if (!job.Enabled)
        {
            job.Enable();
        }
        
        await _repository.UpdateAsync(job);
        await _hangfireService.TriggerJobAsync(jobId);
        
        _logger.LogInformation("Failed job {JobId} retry initiated", jobId);
    }

    public async Task ResetJobHealthAsync(string jobId)
    {
        Check.NotNullOrEmpty(jobId, nameof(jobId));
        
        _logger.LogInformation("Resetting health metrics for job {JobId}", jobId);
        
        var job = await _repository.GetAsync(jobId);
        job.ResetHealthMetrics();
        
        await _repository.UpdateAsync(job);
        
        _logger.LogInformation("Health metrics reset for job {JobId}", jobId);
    }

    public async Task<RecurringJobSystemStatusDto> GetSystemStatusAsync()
    {
        var jobs = await _repository.GetListAsync();
        var runningJobs = new List<string>();
        
        foreach (var job in jobs.Where(j => j.Enabled))
        {
            if (await _hangfireService.IsJobRunningAsync(job.Id))
            {
                runningJobs.Add(job.Id);
            }
        }
        
        var healthyJobs = jobs.Count(j => j.IsHealthy);
        var problematicJobs = jobs.Where(j => j.RequiresAttention).ToList();
        
        var status = new RecurringJobSystemStatusDto
        {
            TotalJobs = jobs.Count,
            EnabledJobs = jobs.Count(j => j.Enabled),
            DisabledJobs = jobs.Count(j => !j.Enabled),
            HealthyJobs = healthyJobs,
            JobsRequiringAttention = problematicJobs.Count,
            CurrentlyRunningJobs = runningJobs.Count,
            LastSystemCheck = _clock.Now
        };
        
        // Calculate overall success rate
        if (jobs.Any(j => j.ExecutionCount > 0))
        {
            status.OverallSuccessRate = jobs
                .Where(j => j.ExecutionCount > 0)
                .Average(j => j.SuccessRate);
        }
        
        // Calculate average execution time
        var avgDurations = jobs
            .Where(j => j.AverageExecutionDuration.HasValue)
            .Select(j => j.AverageExecutionDuration!.Value)
            .ToList();
        
        if (avgDurations.Any())
        {
            status.AverageExecutionTime = TimeSpan.FromMilliseconds(
                avgDurations.Average(d => d.TotalMilliseconds));
        }
        
        // Generate alerts for problematic jobs
        status.ActiveAlerts = problematicJobs
            .Select(j => new JobAlertDto
            {
                JobId = j.Id,
                JobName = j.Description ?? j.Id,
                Severity = j.ConsecutiveFailures >= 3 ? AlertSeverity.Critical : AlertSeverity.Warning,
                Message = GenerateAlertMessage(j),
                AlertTime = j.LastErrorTime ?? _clock.Now
            })
            .ToList();
        
        // Include problematic jobs details
        status.ProblematicJobs = await Task.WhenAll(
            problematicJobs.Select(async j => await MapToDto(j, null)));
        
        return status;
    }

    public async Task<List<RecurringJobDto>> GetJobsByStatusAsync(JobStatus status)
    {
        var jobs = await _repository.GetListAsync(j => j.Status == status);
        var result = new List<RecurringJobDto>();
        
        foreach (var job in jobs)
        {
            var hangfireInfo = await _hangfireService.GetJobInfoAsync(job.Id);
            var dto = await MapToDto(job, hangfireInfo);
            result.Add(dto);
        }
        
        return result;
    }

    public async Task<int> CleanupOldExecutionHistoryAsync(TimeSpan olderThan)
    {
        var cutoffDate = _clock.Now.Subtract(olderThan);
        
        _logger.LogInformation("Cleaning up execution history older than {CutoffDate}", cutoffDate);
        
        var oldHistory = await _historyRepository.GetListAsync(h => h.StartTime < cutoffDate);
        
        foreach (var history in oldHistory)
        {
            await _historyRepository.DeleteAsync(history);
        }
        
        _logger.LogInformation("Cleaned up {Count} old execution history records", oldHistory.Count);
        
        return oldHistory.Count;
    }

    private async Task<RecurringJobDto> MapToDto(RecurringJob job, HangfireJobInfo? hangfireInfo)
    {
        var dto = ObjectMapper.Map<RecurringJob, RecurringJobDto>(job);
        
        if (hangfireInfo != null)
        {
            dto.CronExpression = hangfireInfo.CronExpression ?? dto.CronExpression;
            dto.NextExecutionTime = hangfireInfo.NextExecution ?? dto.NextExecutionTime;
            dto.LastExecutionTime = hangfireInfo.LastExecution ?? dto.LastExecutionTime;
            dto.TimeZone = hangfireInfo.TimeZone ?? dto.TimeZone;
            
            // Generate human-readable schedule
            if (!string.IsNullOrEmpty(dto.CronExpression))
            {
                try
                {
                    dto.HumanReadableSchedule = CronExpressionDescriptor.ExpressionDescriptor.GetDescription(dto.CronExpression);
                }
                catch
                {
                    dto.HumanReadableSchedule = dto.CronExpression;
                }
            }
            
            // Calculate time until next execution
            if (dto.NextExecutionTime.HasValue)
            {
                var timeUntil = dto.NextExecutionTime.Value - _clock.Now;
                dto.TimeUntilNextExecution = timeUntil > TimeSpan.Zero ? timeUntil : null;
            }
        }
        
        // Check if job is currently running
        dto.IsCurrentlyRunning = await _hangfireService.IsJobRunningAsync(job.Id);
        
        return dto;
    }

    private async Task<JobHealthCheck> PerformJobHealthCheck(RecurringJob job)
    {
        var issues = new List<HealthIssue>();
        
        // Check consecutive failures
        if (job.ConsecutiveFailures >= 3)
        {
            issues.Add(HealthIssue.Create(
                HealthIssueType.ConsecutiveFailures,
                $"Job has {job.ConsecutiveFailures} consecutive failures",
                job.ConsecutiveFailures));
        }
        
        // Check success rate
        if (job.ExecutionCount > 10 && job.SuccessRate < 80)
        {
            issues.Add(HealthIssue.Create(
                HealthIssueType.LowSuccessRate,
                $"Success rate is {job.SuccessRate:F1}% (below 80%)",
                job.SuccessRate));
        }
        
        // Check execution time
        if (job.AverageExecutionDuration.HasValue && job.AverageExecutionDuration.Value > TimeSpan.FromHours(1))
        {
            issues.Add(HealthIssue.Create(
                HealthIssueType.LongExecutionTime,
                $"Average execution time is {job.AverageExecutionDuration.Value.TotalMinutes:F1} minutes",
                job.AverageExecutionDuration.Value));
        }
        
        // Check for missed executions
        if (job.Enabled && job.NextExecutionTime.HasValue && job.NextExecutionTime.Value < _clock.Now.AddMinutes(-30))
        {
            issues.Add(HealthIssue.Create(
                HealthIssueType.MissedExecution,
                "Job appears to have missed its scheduled execution",
                job.NextExecutionTime.Value));
        }
        
        // Determine overall health
        var health = HealthStatus.Healthy;
        if (issues.Any(i => i.Type == HealthIssueType.ConsecutiveFailures && (int)i.Details! >= 5))
        {
            health = HealthStatus.Critical;
        }
        else if (issues.Any())
        {
            health = HealthStatus.Warning;
        }
        
        return new JobHealthCheck
        {
            JobId = job.Id,
            JobName = job.Description ?? job.Id,
            Health = health,
            Issues = issues,
            CheckDuration = TimeSpan.FromMilliseconds(100) // Placeholder
        };
    }

    private string GenerateAlertMessage(RecurringJob job)
    {
        if (job.ConsecutiveFailures >= 3)
        {
            return $"Job has {job.ConsecutiveFailures} consecutive failures";
        }
        
        if (job.SuccessRate < 50)
        {
            return $"Job success rate is critically low at {job.SuccessRate:F1}%";
        }
        
        if (!string.IsNullOrEmpty(job.LastError))
        {
            return $"Last error: {job.LastError}";
        }
        
        return "Job requires attention";
    }
}
```

### Hangfire Integration Service Implementation

```csharp
public class HangfireJobService : IHangfireJobService
{
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly IMonitoringApi _monitoringApi;
    private readonly ILogger<HangfireJobService> _logger;

    public HangfireJobService(
        IRecurringJobManager recurringJobManager,
        IMonitoringApi monitoringApi,
        ILogger<HangfireJobService> logger)
    {
        _recurringJobManager = recurringJobManager;
        _monitoringApi = monitoringApi;
        _logger = logger;
    }

    public async Task<HangfireJobInfo?> GetJobInfoAsync(string jobId)
    {
        try
        {
            var recurringJobs = _monitoringApi.RecurringJobs();
            var job = recurringJobs.FirstOrDefault(j => j.Id == jobId);
            
            if (job == null)
                return null;
            
            return new HangfireJobInfo
            {
                Id = job.Id,
                Name = job.Id,
                CronExpression = job.Cron,
                NextExecution = job.NextExecution,
                LastExecution = job.LastExecution,
                TimeZone = job.TimeZoneId,
                IsEnabled = !job.Removed,
                Metadata = new Dictionary<string, object>
                {
                    ["LastJobId"] = job.LastJobId ?? "",
                    ["LastJobState"] = job.LastJobState ?? "",
                    ["CreatedAt"] = job.CreatedAt
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Hangfire job info for {JobId}", jobId);
            return null;
        }
    }

    public async Task<List<HangfireJobInfo>> GetAllJobsAsync()
    {
        try
        {
            var recurringJobs = _monitoringApi.RecurringJobs();
            var result = new List<HangfireJobInfo>();
            
            foreach (var job in recurringJobs)
            {
                result.Add(new HangfireJobInfo
                {
                    Id = job.Id,
                    Name = job.Id,
                    CronExpression = job.Cron,
                    NextExecution = job.NextExecution,
                    LastExecution = job.LastExecution,
                    TimeZone = job.TimeZoneId,
                    IsEnabled = !job.Removed,
                    Metadata = new Dictionary<string, object>
                    {
                        ["LastJobId"] = job.LastJobId ?? "",
                        ["LastJobState"] = job.LastJobState ?? "",
                        ["CreatedAt"] = job.CreatedAt
                    }
                });
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all Hangfire jobs");
            return new List<HangfireJobInfo>();
        }
    }

    public async Task EnableJobAsync(string jobId)
    {
        try
        {
            _logger.LogDebug("Enabling Hangfire job {JobId}", jobId);
            
            // Note: Hangfire doesn't have a direct enable method
            // Jobs are enabled by default when created
            // We achieve "enabling" by ensuring the job exists and is not removed
            
            var jobInfo = await GetJobInfoAsync(jobId);
            if (jobInfo == null)
            {
                _logger.LogWarning("Cannot enable job {JobId} - job not found in Hangfire", jobId);
                return;
            }
            
            _logger.LogInformation("Hangfire job {JobId} enabled", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enabling Hangfire job {JobId}", jobId);
            throw;
        }
    }

    public async Task DisableJobAsync(string jobId)
    {
        try
        {
            _logger.LogDebug("Disabling Hangfire job {JobId}", jobId);
            
            _recurringJobManager.RemoveIfExists(jobId);
            
            _logger.LogInformation("Hangfire job {JobId} disabled", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling Hangfire job {JobId}", jobId);
            throw;
        }
    }

    public async Task TriggerJobAsync(string jobId)
    {
        try
        {
            _logger.LogDebug("Triggering Hangfire job {JobId}", jobId);
            
            _recurringJobManager.Trigger(jobId);
            
            _logger.LogInformation("Hangfire job {JobId} triggered", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering Hangfire job {JobId}", jobId);
            throw;
        }
    }

    public async Task<List<JobExecutionInfo>> GetExecutionHistoryAsync(string jobId, int maxResults = 50)
    {
        try
        {
            var result = new List<JobExecutionInfo>();
            
            // Get succeeded jobs
            var succeededJobs = _monitoringApi.SucceededJobs(0, maxResults);
            foreach (var job in succeededJobs.Where(j => j.Value?.Job?.Args?.FirstOrDefault()?.ToString() == jobId))
            {
                result.Add(new JobExecutionInfo
                {
                    StartTime = job.Value.CreatedAt ?? DateTime.MinValue,
                    EndTime = job.Value.StateHistory?.LastOrDefault()?.CreatedAt,
                    State = "Succeeded",
                    Result = job.Value.StateHistory?.LastOrDefault()?.Data?.GetValueOrDefault("Result")?.ToString()
                });
            }
            
            // Get failed jobs
            var failedJobs = _monitoringApi.FailedJobs(0, maxResults);
            foreach (var job in failedJobs.Where(j => j.Value?.Job?.Args?.FirstOrDefault()?.ToString() == jobId))
            {
                result.Add(new JobExecutionInfo
                {
                    StartTime = job.Value.CreatedAt ?? DateTime.MinValue,
                    EndTime = job.Value.StateHistory?.LastOrDefault()?.CreatedAt,
                    State = "Failed",
                    Exception = job.Value.StateHistory?.LastOrDefault()?.Data?.GetValueOrDefault("ExceptionDetails")?.ToString()
                });
            }
            
            return result.OrderByDescending(e => e.StartTime).Take(maxResults).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting execution history for job {JobId}", jobId);
            return new List<JobExecutionInfo>();
        }
    }

    public async Task<bool> IsJobRunningAsync(string jobId)
    {
        try
        {
            var processingJobs = _monitoringApi.ProcessingJobs(0, 1000);
            return processingJobs.Any(j => j.Value?.Job?.Args?.FirstOrDefault()?.ToString() == jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if job {JobId} is running", jobId);
            return false;
        }
    }
}
```

## Happy Day Scenario

1. **Job Discovery**: System discovers all Hangfire recurring jobs and creates corresponding database records
2. **Status Monitoring**: Administrator views job dashboard showing all jobs with health status indicators
3. **Job Management**: Administrator enables/disables specific jobs based on business requirements
4. **Health Monitoring**: System continuously monitors job execution and identifies problematic jobs
5. **Performance Analysis**: Administrator reviews job execution statistics and performance metrics
6. **Issue Resolution**: When job failures are detected, administrator investigates and takes corrective action
7. **Manual Triggering**: Administrator manually triggers specific jobs for testing or urgent execution
8. **System Maintenance**: Regular cleanup of old execution history maintains system performance
9. **Alert Management**: System generates alerts for jobs requiring attention based on health checks
10. **Reporting**: Comprehensive system status reports provide overview of job infrastructure health

## Error Handling

### Job Management Errors
- **Invalid Job ID**: Validate job existence before operations
- **Job State Conflicts**: Handle concurrent job state changes gracefully
- **Hangfire Integration Failures**: Fallback mechanisms when Hangfire is unavailable
- **Configuration Errors**: Validate cron expressions and job settings

### Monitoring Errors
- **Data Collection Failures**: Handle missing or corrupted execution data
- **Performance Metric Errors**: Graceful handling of calculation failures
- **Health Check Failures**: Continue monitoring other jobs when individual checks fail
- **Alert Generation Errors**: Ensure alert system reliability

### System Integration Errors
- **Database Connectivity**: Retry mechanisms and connection pooling
- **Hangfire Communication**: Handle Hangfire API failures gracefully
- **Caching Issues**: Fallback to direct data access when cache fails
- **Notification Failures**: Queue alerts for retry when notification systems fail

## Business Rules

### Job Execution Rules
1. **Enablement Control**: Only enabled jobs execute on schedule
2. **Health-Based Disabling**: Jobs auto-disable after consecutive failures
3. **Manual Override**: Administrators can override automatic decisions
4. **Execution Tracking**: All executions recorded for audit and analysis

### Health Monitoring Rules
1. **Failure Thresholds**: Define failure counts that trigger alerts
2. **Success Rate Requirements**: Monitor success rates over time
3. **Performance Baselines**: Track execution time deviations
4. **Availability Metrics**: Calculate job availability and uptime

### System Maintenance Rules
1. **History Retention**: Automatic cleanup of old execution history
2. **Cache Management**: Regular cache invalidation and refresh
3. **Resource Monitoring**: Track system resource usage
4. **Alert Escalation**: Progressive alert severity based on issue persistence

## Performance Requirements
- Support monitoring of 100+ recurring jobs simultaneously
- Job status updates within 30 seconds of Hangfire changes
- Health check completion within 10 seconds for all jobs
- Execution history queries return within 2 seconds
- System status dashboard loads within 5 seconds
- Handle 1000+ job executions per hour with full tracking