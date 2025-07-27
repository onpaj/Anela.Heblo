# Automated Compliance Monitoring User Story

## Feature Overview
The Automated Compliance Monitoring feature provides scheduled background job execution for continuous warehouse compliance validation. It implements Hangfire-based job scheduling with configurable execution controls, comprehensive error handling, and automated alerting capabilities. The system ensures consistent compliance monitoring without manual intervention while providing operational oversight and emergency controls for maintenance scenarios.

## Business Requirements

### Primary Use Case
As a system administrator, I want to automatically schedule and execute warehouse compliance validation reports at regular intervals so that compliance violations are detected promptly, business rule enforcement is consistent, operational teams receive timely notifications of issues, and warehouse integrity is maintained continuously without manual oversight or intervention.

### Acceptance Criteria
1. The system shall execute compliance reports automatically on configurable schedules
2. The system shall prevent concurrent execution of compliance jobs using timeout controls
3. The system shall provide enable/disable controls for job management and maintenance
4. The system shall implement 20-minute timeout protection for long-running operations
5. The system shall integrate with job management service for operational control
6. The system shall log execution status, timing, and error details comprehensively
7. The system shall handle job failures gracefully with proper error recovery
8. The system shall support manual job triggering for immediate compliance validation
9. The system shall provide job execution history and performance metrics
10. The system shall implement alerting mechanisms for critical compliance violations

## Technical Contracts

### Domain Model

```csharp
// Background job for automated compliance monitoring
[DisableConcurrentExecution(timeoutInSeconds: 20 * 60)]
public class ControllingJob : AsyncBackgroundJob<ControllingJobArgs>
{
    private readonly IControllingAppService _appService;
    private readonly IJobsAppService _jobsService;
    private readonly ILogger<ControllingJob> _logger;
    private readonly IComplianceAlertService _alertService;
    private readonly IClock _clock;

    public ControllingJob(
        IControllingAppService appService,
        IJobsAppService jobsService,
        ILogger<ControllingJob> logger,
        IComplianceAlertService alertService,
        IClock clock)
    {
        _appService = appService;
        _jobsService = jobsService;
        _logger = logger;
        _alertService = alertService;
        _clock = clock;
    }

    public async Task GenerateReports(string jobName)
    {
        if (!await _jobsService.IsEnabled(jobName))
        {
            _logger.LogDebug("Job {JobName} is disabled, skipping execution", jobName);
            return;
        }

        _logger.LogInformation("Starting automated compliance monitoring job: {JobName}", jobName);
        await ExecuteAsync(new ControllingJobArgs { JobName = jobName });
    }

    [DisableConcurrentExecution(timeoutInSeconds: 20 * 60)]
    public override async Task ExecuteAsync(ControllingJobArgs args)
    {
        var executionId = Guid.NewGuid();
        var startTime = _clock.Now;
        
        _logger.LogInformation("Executing compliance monitoring job {ExecutionId} at {StartTime}", 
            executionId, startTime);

        try
        {
            var results = await _appService.GenerateReportsAsync();
            var duration = _clock.Now - startTime;
            
            var violations = results.Where(r => !r.IsSuccess).ToList();
            var successes = results.Where(r => r.IsSuccess).ToList();
            
            _logger.LogInformation("Compliance job {ExecutionId} completed: {SuccessCount} successful, {ViolationCount} violations (Duration: {Duration})",
                executionId, successes.Count, violations.Count, duration);

            if (violations.Any())
            {
                await _alertService.ProcessComplianceViolationsAsync(violations, executionId);
            }

            await _jobsService.RecordJobExecutionAsync(
                args.JobName ?? "ControllingJob", 
                duration, 
                true, 
                null, 
                new Dictionary<string, object>
                {
                    { "ExecutionId", executionId },
                    { "TotalReports", results.Count },
                    { "SuccessfulReports", successes.Count },
                    { "ViolationReports", violations.Count },
                    { "ViolationDetails", violations.Select(v => new { v.Report, v.Message }).ToList() }
                });
        }
        catch (Exception ex)
        {
            var duration = _clock.Now - startTime;
            
            _logger.LogError(ex, "Compliance monitoring job {ExecutionId} failed after {Duration}: {ErrorMessage}",
                executionId, duration, ex.Message);

            await _alertService.NotifyJobFailureAsync(executionId, ex);

            await _jobsService.RecordJobExecutionAsync(
                args.JobName ?? "ControllingJob", 
                duration, 
                false, 
                ex.Message, 
                new Dictionary<string, object>
                {
                    { "ExecutionId", executionId },
                    { "ErrorType", ex.GetType().Name },
                    { "StackTrace", ex.StackTrace ?? "" }
                });

            throw; // Re-throw to ensure Hangfire marks job as failed
        }
    }
}

// Job arguments for parameterized execution
public class ControllingJobArgs
{
    public string? JobName { get; set; }
    public bool ForceExecution { get; set; } = false;
    public List<Warehouses>? SpecificWarehouses { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public DateTime? ScheduledTime { get; set; }
    public string? TriggeredBy { get; set; }
}

// Job execution tracking and metrics
public class JobExecution : AuditedAggregateRoot<int>
{
    public string JobName { get; set; } = "";
    public Guid ExecutionId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }
    public int TotalReports { get; set; }
    public int SuccessfulReports { get; set; }
    public int ViolationReports { get; set; }
    public string? ExecutionContext { get; set; } // JSON metadata
    public JobExecutionTrigger TriggerType { get; set; } = JobExecutionTrigger.Scheduled;
    public string? TriggeredBy { get; set; }
    
    protected JobExecution() { }
    
    public static JobExecution Create(
        string jobName, 
        Guid executionId, 
        JobExecutionTrigger triggerType = JobExecutionTrigger.Scheduled,
        string? triggeredBy = null)
    {
        return new JobExecution
        {
            JobName = jobName,
            ExecutionId = executionId,
            StartTime = DateTime.UtcNow,
            TriggerType = triggerType,
            TriggeredBy = triggeredBy
        };
    }
    
    public void Complete(bool successful, TimeSpan duration, string? errorMessage = null)
    {
        EndTime = DateTime.UtcNow;
        Duration = duration;
        IsSuccessful = successful;
        ErrorMessage = errorMessage;
    }
    
    public void UpdateReportCounts(int total, int successful, int violations)
    {
        TotalReports = total;
        SuccessfulReports = successful;
        ViolationReports = violations;
    }
    
    public void SetExecutionContext(object context)
    {
        ExecutionContext = JsonSerializer.Serialize(context);
    }
    
    public T? GetExecutionContext<T>() where T : class
    {
        if (string.IsNullOrEmpty(ExecutionContext))
            return null;
            
        try
        {
            return JsonSerializer.Deserialize<T>(ExecutionContext);
        }
        catch (JsonException)
        {
            return null;
        }
    }
    
    public bool IsLongRunning => Duration?.TotalMinutes > 15;
    public bool HasErrors => !IsSuccessful && !string.IsNullOrEmpty(ErrorMessage);
    public bool HasViolations => ViolationReports > 0;
    public double SuccessRate => TotalReports > 0 ? (double)SuccessfulReports / TotalReports * 100 : 0;
}

public enum JobExecutionTrigger
{
    Scheduled = 1,
    Manual = 2,
    Retry = 3,
    Emergency = 4
}

// Job scheduling configuration
public class JobScheduleConfiguration : Entity<int>
{
    public string JobName { get; set; } = "";
    public string CronExpression { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public DateTime? NextExecution { get; set; }
    public DateTime? LastExecution { get; set; }
    public int ExecutionCount { get; set; }
    public int FailureCount { get; set; }
    public int ConsecutiveFailures { get; set; }
    public bool AutoDisableOnFailures { get; set; } = true;
    public int MaxConsecutiveFailures { get; set; } = 5;
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(20);
    public Dictionary<string, object> DefaultParameters { get; set; } = new();
    
    protected JobScheduleConfiguration() { }
    
    public static JobScheduleConfiguration Create(
        string jobName, 
        string cronExpression, 
        TimeSpan? timeout = null)
    {
        return new JobScheduleConfiguration
        {
            JobName = jobName,
            CronExpression = cronExpression,
            ExecutionTimeout = timeout ?? TimeSpan.FromMinutes(20)
        };
    }
    
    public void RecordExecution(bool successful)
    {
        ExecutionCount++;
        LastExecution = DateTime.UtcNow;
        
        if (successful)
        {
            ConsecutiveFailures = 0;
        }
        else
        {
            FailureCount++;
            ConsecutiveFailures++;
            
            if (AutoDisableOnFailures && ConsecutiveFailures >= MaxConsecutiveFailures)
            {
                IsEnabled = false;
            }
        }
    }
    
    public void UpdateSchedule(string cronExpression)
    {
        CronExpression = cronExpression;
        // Next execution would be calculated by scheduling service
    }
    
    public void Enable()
    {
        IsEnabled = true;
        ConsecutiveFailures = 0; // Reset failure count when manually enabled
    }
    
    public void Disable(string? reason = null)
    {
        IsEnabled = false;
        // Reason could be stored in audit log or separate tracking
    }
    
    public bool RequiresAttention => ConsecutiveFailures >= 3 || !IsEnabled;
    public double FailureRate => ExecutionCount > 0 ? (double)FailureCount / ExecutionCount * 100 : 0;
    public bool IsHealthy => IsEnabled && ConsecutiveFailures == 0 && FailureRate < 10;
}

// Compliance alert and notification management
public class ComplianceAlert : AuditedAggregateRoot<int>
{
    public Guid ExecutionId { get; set; }
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public Warehouses? WarehouseId { get; set; }
    public int ViolationCount { get; set; }
    public string? ViolationDetails { get; set; }
    public DateTime AlertTime { get; set; } = DateTime.UtcNow;
    public bool IsAcknowledged { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }
    public List<string> NotificationChannels { get; set; } = new();
    
    protected ComplianceAlert() { }
    
    public static ComplianceAlert CreateViolationAlert(
        Guid executionId,
        Warehouses warehouseId,
        int violationCount,
        string violationDetails)
    {
        var severity = violationCount switch
        {
            > 10 => AlertSeverity.Critical,
            > 5 => AlertSeverity.High,
            > 0 => AlertSeverity.Medium,
            _ => AlertSeverity.Low
        };
        
        return new ComplianceAlert
        {
            ExecutionId = executionId,
            Type = AlertType.ComplianceViolation,
            Severity = severity,
            Title = $"Warehouse Compliance Violation - {warehouseId}",
            Message = $"Found {violationCount} compliance violations in {warehouseId} warehouse",
            WarehouseId = warehouseId,
            ViolationCount = violationCount,
            ViolationDetails = violationDetails
        };
    }
    
    public static ComplianceAlert CreateJobFailureAlert(Guid executionId, Exception exception)
    {
        return new ComplianceAlert
        {
            ExecutionId = executionId,
            Type = AlertType.JobFailure,
            Severity = AlertSeverity.High,
            Title = "Compliance Monitoring Job Failed",
            Message = $"Compliance monitoring job failed: {exception.Message}",
            ViolationDetails = exception.StackTrace
        };
    }
    
    public void Acknowledge(string acknowledgedBy)
    {
        IsAcknowledged = true;
        AcknowledgedAt = DateTime.UtcNow;
        AcknowledgedBy = acknowledgedBy;
    }
    
    public void Resolve(string resolutionNotes, string resolvedBy)
    {
        IsResolved = true;
        ResolvedAt = DateTime.UtcNow;
        ResolutionNotes = resolutionNotes;
        
        if (!IsAcknowledged)
        {
            Acknowledge(resolvedBy);
        }
    }
    
    public bool RequiresImmediateAttention => 
        Severity == AlertSeverity.Critical && !IsAcknowledged;
        
    public TimeSpan TimeToAcknowledge => 
        AcknowledgedAt?.Subtract(AlertTime) ?? DateTime.UtcNow.Subtract(AlertTime);
        
    public TimeSpan? TimeToResolve => 
        ResolvedAt?.Subtract(AlertTime);
}

public enum AlertType
{
    ComplianceViolation = 1,
    JobFailure = 2,
    SystemError = 3,
    PerformanceIssue = 4
}

public enum AlertSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}
```

### Application Layer Contracts

```csharp
// Enhanced job management service interface
public interface IJobsAppService : IApplicationService
{
    Task<bool> IsEnabled(string jobName);
    Task EnableJobAsync(string jobName);
    Task DisableJobAsync(string jobName, string? reason = null);
    Task<JobScheduleConfigurationDto> GetJobConfigurationAsync(string jobName);
    Task UpdateJobScheduleAsync(string jobName, string cronExpression);
    Task TriggerJobAsync(string jobName, Dictionary<string, object>? parameters = null);
    Task<List<JobExecutionDto>> GetJobExecutionHistoryAsync(string jobName, int maxResults = 50);
    Task<JobExecutionStatisticsDto> GetJobStatisticsAsync(string jobName);
    Task RecordJobExecutionAsync(string jobName, TimeSpan duration, bool successful, string? errorMessage = null, Dictionary<string, object>? metadata = null);
    Task<List<JobScheduleConfigurationDto>> GetAllJobConfigurationsAsync();
    Task<HealthCheckDto> GetJobHealthStatusAsync();
}

// Compliance alert service interface
public interface IComplianceAlertService : ITransientDependency
{
    Task ProcessComplianceViolationsAsync(List<ReportResultDto> violations, Guid executionId);
    Task NotifyJobFailureAsync(Guid executionId, Exception exception);
    Task<List<ComplianceAlertDto>> GetActiveAlertsAsync(AlertSeverity? minimumSeverity = null);
    Task<ComplianceAlertDto> AcknowledgeAlertAsync(int alertId, string acknowledgedBy);
    Task<ComplianceAlertDto> ResolveAlertAsync(int alertId, string resolutionNotes, string resolvedBy);
    Task<AlertStatisticsDto> GetAlertStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null);
    Task CleanupOldAlertsAsync(DateTime olderThan);
}

// Repository interfaces for persistence
public interface IJobExecutionRepository : IRepository<JobExecution, int>
{
    Task<List<JobExecution>> GetExecutionHistoryAsync(string jobName, int maxResults = 50, CancellationToken cancellationToken = default);
    Task<JobExecution?> GetLatestExecutionAsync(string jobName, CancellationToken cancellationToken = default);
    Task<List<JobExecution>> GetFailedExecutionsAsync(DateTime fromDate, CancellationToken cancellationToken = default);
    Task<JobExecutionStatistics> GetJobStatisticsAsync(string jobName, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<int> CleanupOldExecutionsAsync(DateTime olderThan, CancellationToken cancellationToken = default);
}

public interface IJobScheduleConfigurationRepository : IRepository<JobScheduleConfiguration, int>
{
    Task<JobScheduleConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default);
    Task<List<JobScheduleConfiguration>> GetEnabledJobsAsync(CancellationToken cancellationToken = default);
    Task<List<JobScheduleConfiguration>> GetJobsRequiringAttentionAsync(CancellationToken cancellationToken = default);
}

public interface IComplianceAlertRepository : IRepository<ComplianceAlert, int>
{
    Task<List<ComplianceAlert>> GetActiveAlertsAsync(AlertSeverity? minimumSeverity = null, CancellationToken cancellationToken = default);
    Task<List<ComplianceAlert>> GetUnacknowledgedAlertsAsync(CancellationToken cancellationToken = default);
    Task<List<ComplianceAlert>> GetAlertsByExecutionAsync(Guid executionId, CancellationToken cancellationToken = default);
    Task<AlertStatistics> GetAlertStatisticsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<int> CleanupOldAlertsAsync(DateTime olderThan, CancellationToken cancellationToken = default);
}

// DTOs for API contracts
public class JobExecutionDto : AuditedEntityDto<int>
{
    public string JobName { get; set; } = "";
    public Guid ExecutionId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public int TotalReports { get; set; }
    public int SuccessfulReports { get; set; }
    public int ViolationReports { get; set; }
    public JobExecutionTrigger TriggerType { get; set; }
    public string? TriggeredBy { get; set; }
    public bool IsLongRunning { get; set; }
    public bool HasViolations { get; set; }
    public double SuccessRate { get; set; }
}

public class JobScheduleConfigurationDto : EntityDto<int>
{
    public string JobName { get; set; } = "";
    public string CronExpression { get; set; } = "";
    public bool IsEnabled { get; set; }
    public DateTime? NextExecution { get; set; }
    public DateTime? LastExecution { get; set; }
    public int ExecutionCount { get; set; }
    public int FailureCount { get; set; }
    public int ConsecutiveFailures { get; set; }
    public TimeSpan ExecutionTimeout { get; set; }
    public bool RequiresAttention { get; set; }
    public double FailureRate { get; set; }
    public bool IsHealthy { get; set; }
}

public class JobExecutionStatisticsDto
{
    public string JobName { get; set; } = "";
    public int TotalExecutions { get; set; }
    public int SuccessfulExecutions { get; set; }
    public int FailedExecutions { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }
    public DateTime? LastFailedExecution { get; set; }
    public int ConsecutiveFailures { get; set; }
    public bool IsHealthy { get; set; }
    public List<JobExecutionTrendDto> ExecutionTrend { get; set; } = new();
}

public class JobExecutionTrendDto
{
    public DateTime Date { get; set; }
    public int ExecutionCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
}

public class ComplianceAlertDto : AuditedEntityDto<int>
{
    public Guid ExecutionId { get; set; }
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public Warehouses? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public int ViolationCount { get; set; }
    public string? ViolationDetails { get; set; }
    public DateTime AlertTime { get; set; }
    public bool IsAcknowledged { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }
    public bool RequiresImmediateAttention { get; set; }
    public TimeSpan TimeToAcknowledge { get; set; }
    public TimeSpan? TimeToResolve { get; set; }
}

public class AlertStatisticsDto
{
    public int TotalAlerts { get; set; }
    public int ActiveAlerts { get; set; }
    public int AcknowledgedAlerts { get; set; }
    public int ResolvedAlerts { get; set; }
    public int CriticalAlerts { get; set; }
    public int HighSeverityAlerts { get; set; }
    public int MediumSeverityAlerts { get; set; }
    public int LowSeverityAlerts { get; set; }
    public TimeSpan AverageAcknowledgeTime { get; set; }
    public TimeSpan AverageResolutionTime { get; set; }
    public Dictionary<AlertType, int> AlertsByType { get; set; } = new();
    public Dictionary<Warehouses, int> AlertsByWarehouse { get; set; } = new();
}

public class HealthCheckDto
{
    public bool IsHealthy { get; set; }
    public int TotalJobs { get; set; }
    public int EnabledJobs { get; set; }
    public int DisabledJobs { get; set; }
    public int FailingJobs { get; set; }
    public int JobsRequiringAttention { get; set; }
    public DateTime LastHealthCheck { get; set; }
    public List<JobHealthStatusDto> JobStatuses { get; set; } = new();
}

public class JobHealthStatusDto
{
    public string JobName { get; set; } = "";
    public bool IsEnabled { get; set; }
    public bool IsHealthy { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? NextExecution { get; set; }
    public string? LastError { get; set; }
}

public class TriggerJobRequestDto
{
    public string JobName { get; set; } = "";
    public Dictionary<string, object>? Parameters { get; set; }
    public string? TriggeredBy { get; set; }
    public string? Reason { get; set; }
}
```

## Implementation Details

### Enhanced JobsAppService Implementation

```csharp
[Authorize]
public class JobsAppService : ApplicationService, IJobsAppService
{
    private readonly IJobScheduleConfigurationRepository _configRepository;
    private readonly IJobExecutionRepository _executionRepository;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly ILogger<JobsAppService> _logger;
    private readonly IClock _clock;

    public JobsAppService(
        IJobScheduleConfigurationRepository configRepository,
        IJobExecutionRepository executionRepository,
        IBackgroundJobManager backgroundJobManager,
        IRecurringJobManager recurringJobManager,
        ILogger<JobsAppService> logger,
        IClock clock)
    {
        _configRepository = configRepository;
        _executionRepository = executionRepository;
        _backgroundJobManager = backgroundJobManager;
        _recurringJobManager = recurringJobManager;
        _logger = logger;
        _clock = clock;
    }

    public async Task<bool> IsEnabled(string jobName)
    {
        Check.NotNullOrEmpty(jobName, nameof(jobName));
        
        var config = await _configRepository.GetByJobNameAsync(jobName);
        return config?.IsEnabled ?? false;
    }

    public async Task EnableJobAsync(string jobName)
    {
        Check.NotNullOrEmpty(jobName, nameof(jobName));
        
        _logger.LogInformation("Enabling job: {JobName}", jobName);
        
        var config = await _configRepository.GetByJobNameAsync(jobName);
        if (config == null)
        {
            throw new EntityNotFoundException(typeof(JobScheduleConfiguration), jobName);
        }

        config.Enable();
        await _configRepository.UpdateAsync(config);
        
        // Re-register with Hangfire if it has a schedule
        if (!string.IsNullOrEmpty(config.CronExpression))
        {
            _recurringJobManager.AddOrUpdate<ControllingJob>(
                jobName,
                job => job.GenerateReports(jobName),
                config.CronExpression);
        }
        
        _logger.LogInformation("Job {JobName} enabled successfully", jobName);
    }

    public async Task DisableJobAsync(string jobName, string? reason = null)
    {
        Check.NotNullOrEmpty(jobName, nameof(jobName));
        
        _logger.LogInformation("Disabling job: {JobName}, Reason: {Reason}", jobName, reason);
        
        var config = await _configRepository.GetByJobNameAsync(jobName);
        if (config == null)
        {
            throw new EntityNotFoundException(typeof(JobScheduleConfiguration), jobName);
        }

        config.Disable(reason);
        await _configRepository.UpdateAsync(config);
        
        // Remove from Hangfire scheduling
        _recurringJobManager.RemoveIfExists(jobName);
        
        _logger.LogInformation("Job {JobName} disabled successfully", jobName);
    }

    public async Task<JobScheduleConfigurationDto> GetJobConfigurationAsync(string jobName)
    {
        Check.NotNullOrEmpty(jobName, nameof(jobName));
        
        var config = await _configRepository.GetByJobNameAsync(jobName);
        if (config == null)
        {
            throw new EntityNotFoundException(typeof(JobScheduleConfiguration), jobName);
        }

        return ObjectMapper.Map<JobScheduleConfiguration, JobScheduleConfigurationDto>(config);
    }

    public async Task UpdateJobScheduleAsync(string jobName, string cronExpression)
    {
        Check.NotNullOrEmpty(jobName, nameof(jobName));
        Check.NotNullOrEmpty(cronExpression, nameof(cronExpression));
        
        _logger.LogInformation("Updating schedule for job {JobName} to: {CronExpression}", jobName, cronExpression);
        
        var config = await _configRepository.GetByJobNameAsync(jobName);
        if (config == null)
        {
            throw new EntityNotFoundException(typeof(JobScheduleConfiguration), jobName);
        }

        config.UpdateSchedule(cronExpression);
        await _configRepository.UpdateAsync(config);
        
        // Update Hangfire schedule if job is enabled
        if (config.IsEnabled)
        {
            _recurringJobManager.AddOrUpdate<ControllingJob>(
                jobName,
                job => job.GenerateReports(jobName),
                cronExpression);
        }
        
        _logger.LogInformation("Job {JobName} schedule updated successfully", jobName);
    }

    public async Task TriggerJobAsync(string jobName, Dictionary<string, object>? parameters = null)
    {
        Check.NotNullOrEmpty(jobName, nameof(jobName));
        
        _logger.LogInformation("Manually triggering job: {JobName}", jobName);
        
        var config = await _configRepository.GetByJobNameAsync(jobName);
        if (config == null)
        {
            throw new EntityNotFoundException(typeof(JobScheduleConfiguration), jobName);
        }

        var args = new ControllingJobArgs
        {
            JobName = jobName,
            ForceExecution = true,
            Parameters = parameters ?? new Dictionary<string, object>(),
            TriggeredBy = CurrentUser.UserName ?? "System"
        };

        var jobId = _backgroundJobManager.Enqueue<ControllingJob>(job => job.ExecuteAsync(args));
        
        _logger.LogInformation("Job {JobName} triggered manually with ID: {JobId}", jobName, jobId);
    }

    public async Task<List<JobExecutionDto>> GetJobExecutionHistoryAsync(string jobName, int maxResults = 50)
    {
        Check.NotNullOrEmpty(jobName, nameof(jobName));
        
        var executions = await _executionRepository.GetExecutionHistoryAsync(jobName, maxResults);
        return ObjectMapper.Map<List<JobExecution>, List<JobExecutionDto>>(executions);
    }

    public async Task<JobExecutionStatisticsDto> GetJobStatisticsAsync(string jobName)
    {
        Check.NotNullOrEmpty(jobName, nameof(jobName));
        
        var fromDate = _clock.Now.AddDays(-30);
        var toDate = _clock.Now;
        
        var statistics = await _executionRepository.GetJobStatisticsAsync(jobName, fromDate, toDate);
        return ObjectMapper.Map<JobExecutionStatistics, JobExecutionStatisticsDto>(statistics);
    }

    public async Task RecordJobExecutionAsync(
        string jobName, 
        TimeSpan duration, 
        bool successful, 
        string? errorMessage = null, 
        Dictionary<string, object>? metadata = null)
    {
        Check.NotNullOrEmpty(jobName, nameof(jobName));
        
        var execution = JobExecution.Create(jobName, Guid.NewGuid(), JobExecutionTrigger.Scheduled);
        execution.Complete(successful, duration, errorMessage);
        
        if (metadata != null)
        {
            execution.SetExecutionContext(metadata);
            
            if (metadata.ContainsKey("TotalReports"))
            {
                var total = Convert.ToInt32(metadata["TotalReports"]);
                var successful_count = metadata.ContainsKey("SuccessfulReports") ? Convert.ToInt32(metadata["SuccessfulReports"]) : 0;
                var violations = metadata.ContainsKey("ViolationReports") ? Convert.ToInt32(metadata["ViolationReports"]) : 0;
                
                execution.UpdateReportCounts(total, successful_count, violations);
            }
        }

        await _executionRepository.InsertAsync(execution);
        
        // Update job configuration statistics
        var config = await _configRepository.GetByJobNameAsync(jobName);
        if (config != null)
        {
            config.RecordExecution(successful);
            await _configRepository.UpdateAsync(config);
        }
    }

    public async Task<List<JobScheduleConfigurationDto>> GetAllJobConfigurationsAsync()
    {
        var configurations = await _configRepository.GetListAsync();
        return ObjectMapper.Map<List<JobScheduleConfiguration>, List<JobScheduleConfigurationDto>>(configurations);
    }

    public async Task<HealthCheckDto> GetJobHealthStatusAsync()
    {
        var configurations = await _configRepository.GetListAsync();
        var jobsRequiringAttention = await _configRepository.GetJobsRequiringAttentionAsync();
        
        var healthCheck = new HealthCheckDto
        {
            LastHealthCheck = _clock.Now,
            TotalJobs = configurations.Count,
            EnabledJobs = configurations.Count(c => c.IsEnabled),
            DisabledJobs = configurations.Count(c => !c.IsEnabled),
            FailingJobs = configurations.Count(c => c.ConsecutiveFailures > 0),
            JobsRequiringAttention = jobsRequiringAttention.Count,
            IsHealthy = jobsRequiringAttention.Count == 0
        };

        healthCheck.JobStatuses = configurations.Select(c => new JobHealthStatusDto
        {
            JobName = c.JobName,
            IsEnabled = c.IsEnabled,
            IsHealthy = c.IsHealthy,
            ConsecutiveFailures = c.ConsecutiveFailures,
            LastExecution = c.LastExecution,
            NextExecution = c.NextExecution
        }).ToList();

        return healthCheck;
    }
}
```

### Compliance Alert Service Implementation

```csharp
public class ComplianceAlertService : IComplianceAlertService
{
    private readonly IComplianceAlertRepository _alertRepository;
    private readonly INotificationService _notificationService;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<ComplianceAlertService> _logger;
    private readonly IClock _clock;

    public ComplianceAlertService(
        IComplianceAlertRepository alertRepository,
        INotificationService notificationService,
        IEmailSender emailSender,
        ILogger<ComplianceAlertService> logger,
        IClock clock)
    {
        _alertRepository = alertRepository;
        _notificationService = notificationService;
        _emailSender = emailSender;
        _logger = logger;
        _clock = clock;
    }

    public async Task ProcessComplianceViolationsAsync(List<ReportResultDto> violations, Guid executionId)
    {
        _logger.LogInformation("Processing {ViolationCount} compliance violations for execution {ExecutionId}", 
            violations.Count, executionId);

        var alerts = new List<ComplianceAlert>();

        foreach (var violation in violations)
        {
            var warehouseId = DetermineWarehouseFromReport(violation.Report);
            var violationCount = CountViolationsInMessage(violation.Message);
            
            var alert = ComplianceAlert.CreateViolationAlert(
                executionId, 
                warehouseId, 
                violationCount, 
                violation.Message ?? "");
            
            alerts.Add(alert);
        }

        // Save alerts
        foreach (var alert in alerts)
        {
            await _alertRepository.InsertAsync(alert);
        }

        // Send notifications for critical alerts
        var criticalAlerts = alerts.Where(a => a.Severity == AlertSeverity.Critical).ToList();
        if (criticalAlerts.Any())
        {
            await SendCriticalAlertNotificationsAsync(criticalAlerts);
        }

        _logger.LogInformation("Created {AlertCount} compliance alerts, {CriticalCount} critical", 
            alerts.Count, criticalAlerts.Count);
    }

    public async Task NotifyJobFailureAsync(Guid executionId, Exception exception)
    {
        _logger.LogWarning("Creating job failure alert for execution {ExecutionId}: {ErrorMessage}", 
            executionId, exception.Message);

        var alert = ComplianceAlert.CreateJobFailureAlert(executionId, exception);
        await _alertRepository.InsertAsync(alert);

        // Send immediate notification for job failures
        await SendJobFailureNotificationAsync(alert);
    }

    public async Task<List<ComplianceAlertDto>> GetActiveAlertsAsync(AlertSeverity? minimumSeverity = null)
    {
        var alerts = await _alertRepository.GetActiveAlertsAsync(minimumSeverity);
        return ObjectMapper.Map<List<ComplianceAlert>, List<ComplianceAlertDto>>(alerts);
    }

    public async Task<ComplianceAlertDto> AcknowledgeAlertAsync(int alertId, string acknowledgedBy)
    {
        var alert = await _alertRepository.GetAsync(alertId);
        
        alert.Acknowledge(acknowledgedBy);
        alert = await _alertRepository.UpdateAsync(alert);
        
        _logger.LogInformation("Alert {AlertId} acknowledged by {AcknowledgedBy}", alertId, acknowledgedBy);
        
        return ObjectMapper.Map<ComplianceAlert, ComplianceAlertDto>(alert);
    }

    public async Task<ComplianceAlertDto> ResolveAlertAsync(int alertId, string resolutionNotes, string resolvedBy)
    {
        var alert = await _alertRepository.GetAsync(alertId);
        
        alert.Resolve(resolutionNotes, resolvedBy);
        alert = await _alertRepository.UpdateAsync(alert);
        
        _logger.LogInformation("Alert {AlertId} resolved by {ResolvedBy}: {ResolutionNotes}", 
            alertId, resolvedBy, resolutionNotes);
        
        return ObjectMapper.Map<ComplianceAlert, ComplianceAlertDto>(alert);
    }

    public async Task<AlertStatisticsDto> GetAlertStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var from = fromDate ?? _clock.Now.AddDays(-30);
        var to = toDate ?? _clock.Now;
        
        var statistics = await _alertRepository.GetAlertStatisticsAsync(from, to);
        return ObjectMapper.Map<AlertStatistics, AlertStatisticsDto>(statistics);
    }

    public async Task CleanupOldAlertsAsync(DateTime olderThan)
    {
        _logger.LogInformation("Starting cleanup of alerts older than {CutoffDate}", olderThan);
        
        var deletedCount = await _alertRepository.CleanupOldAlertsAsync(olderThan);
        
        _logger.LogInformation("Cleaned up {DeletedCount} old alerts", deletedCount);
    }

    private async Task SendCriticalAlertNotificationsAsync(List<ComplianceAlert> criticalAlerts)
    {
        foreach (var alert in criticalAlerts)
        {
            try
            {
                // Send dashboard notification
                await _notificationService.SendNotificationAsync(new NotificationData
                {
                    Title = alert.Title,
                    Message = alert.Message,
                    Severity = NotificationSeverity.Error,
                    Recipients = GetWarehouseManagers(alert.WarehouseId)
                });

                // Send email notification
                await _emailSender.SendAsync(
                    to: GetWarehouseManagerEmails(alert.WarehouseId),
                    subject: $"CRITICAL: {alert.Title}",
                    body: CreateEmailBody(alert));

                alert.NotificationChannels.AddRange(new[] { "Dashboard", "Email" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send critical alert notification for alert {AlertId}", alert.Id);
            }
        }
    }

    private async Task SendJobFailureNotificationAsync(ComplianceAlert alert)
    {
        try
        {
            await _notificationService.SendNotificationAsync(new NotificationData
            {
                Title = alert.Title,
                Message = alert.Message,
                Severity = NotificationSeverity.Error,
                Recipients = GetSystemAdministrators()
            });

            alert.NotificationChannels.Add("Dashboard");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send job failure notification for alert {AlertId}", alert.Id);
        }
    }

    private Warehouses DetermineWarehouseFromReport(string reportName)
    {
        return reportName switch
        {
            "MaterialWarehouseInvalidProductsReport" => Warehouses.Material,
            "ProductsProductWarehouseInvalidProductsReport" => Warehouses.Product,
            "SemiProductsWarehouseInvalidProductsReport" => Warehouses.SemiProduct,
            _ => Warehouses.UNDEFINED
        };
    }

    private int CountViolationsInMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return 0;
            
        return message.Split(',').Length;
    }

    private List<string> GetWarehouseManagers(Warehouses? warehouseId)
    {
        // Implementation would retrieve warehouse managers from user management system
        return new List<string> { "warehouse.manager@company.com" };
    }

    private List<string> GetWarehouseManagerEmails(Warehouses? warehouseId)
    {
        // Implementation would retrieve warehouse manager emails
        return new List<string> { "warehouse.manager@company.com" };
    }

    private List<string> GetSystemAdministrators()
    {
        // Implementation would retrieve system administrators
        return new List<string> { "system.admin@company.com" };
    }

    private string CreateEmailBody(ComplianceAlert alert)
    {
        return $"""
            Compliance Alert: {alert.Title}
            
            Severity: {alert.Severity}
            Warehouse: {alert.WarehouseId}
            Violation Count: {alert.ViolationCount}
            Alert Time: {alert.AlertTime:yyyy-MM-dd HH:mm:ss}
            
            Details:
            {alert.ViolationDetails}
            
            Please review and take appropriate action.
            
            Execution ID: {alert.ExecutionId}
            """;
    }
}
```

## Happy Day Scenario

1. **Scheduled Execution**: Hangfire triggers compliance monitoring job based on configured cron schedule
2. **Job Enablement Check**: System verifies job is enabled through JobsAppService before execution
3. **Execution Tracking**: Job creates execution record with unique ID and start time tracking
4. **Report Generation**: ControllingAppService generates all registered compliance reports
5. **Result Processing**: System analyzes report results to identify violations and successes
6. **Alert Creation**: Violations automatically generate compliance alerts with appropriate severity
7. **Notification Dispatch**: Critical alerts trigger immediate notifications to relevant stakeholders
8. **Execution Recording**: Job execution statistics and metadata are persisted for monitoring
9. **Performance Tracking**: Duration, success rate, and violation metrics are updated
10. **Health Monitoring**: Job health status is updated based on execution results and failure patterns

## Error Handling

### Job Execution Errors
- **Timeout Protection**: 20-minute maximum execution time with automatic termination
- **Concurrent Execution Prevention**: Hangfire attributes prevent job overlap and conflicts
- **Exception Recovery**: Comprehensive exception handling with detailed error logging
- **Retry Mechanisms**: Failed jobs can be retried manually or through automated policies

### System Integration Errors
- **ERP Connectivity Issues**: Graceful handling of FlexiBee connection failures
- **Database Errors**: Transaction rollback and consistent state maintenance
- **Background Service Failures**: Hangfire resilience with job persistence and recovery
- **Memory Management**: Proper resource disposal and memory leak prevention

### Alert and Notification Errors
- **Notification Failures**: Fallback mechanisms for failed alert delivery
- **Email Service Issues**: Alternative notification channels when email unavailable
- **Dashboard Connectivity**: Offline alert queuing and batch delivery
- **User Management Integration**: Graceful handling of user lookup failures

### Configuration Errors
- **Invalid Cron Expressions**: Validation and error reporting for schedule configuration
- **Missing Job Configurations**: Automatic creation of default configurations
- **Permission Issues**: Proper authorization checking and error messaging
- **Schedule Conflicts**: Detection and resolution of overlapping job schedules

## Business Rules

### Job Execution Rules
1. **Single Execution**: Only one instance of compliance job can run simultaneously
2. **Enable/Disable Control**: Jobs can be disabled for maintenance without losing configuration
3. **Timeout Enforcement**: All job executions must complete within 20-minute timeout
4. **Failure Threshold**: Jobs auto-disable after 5 consecutive failures
5. **Manual Override**: Administrative users can force job execution regardless of schedule

### Alert Management Rules
1. **Severity Classification**: Violations classified by count (Critical: >10, High: >5, Medium: >0)
2. **Immediate Notification**: Critical alerts trigger real-time notifications
3. **Acknowledgment Required**: High and critical alerts require manual acknowledgment
4. **Resolution Tracking**: All alerts must be resolved with detailed notes
5. **Escalation Procedures**: Unacknowledged critical alerts escalate after 2 hours

### Performance Requirements
- Complete compliance validation within 5 minutes for normal operations
- Support up to 100 concurrent job execution history queries
- Maintain alert response time under 30 seconds for critical notifications
- Handle job execution tracking for 10,000+ executions per month
- Provide real-time job health status with sub-second response times
- Scale linearly with additional warehouse and report types