using System.Linq.Expressions;
using Anela.Heblo.Xcc.Services;
using Hangfire;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

public class HangfireBackgroundWorker : IBackgroundWorker
{
    public string Enqueue<T>(Expression<Func<T, Task>> methodCall)
    {
        return BackgroundJob.Enqueue(methodCall);
    }

    public string Enqueue<T>(Expression<Action<T>> methodCall)
    {
        return BackgroundJob.Enqueue(methodCall);
    }

    public string Schedule<T>(Expression<Func<T, Task>> methodCall, TimeSpan delay)
    {
        return BackgroundJob.Schedule(methodCall, delay);
    }

    public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
    {
        return BackgroundJob.Schedule(methodCall, delay);
    }

    public string Schedule<T>(Expression<Func<T, Task>> methodCall, DateTimeOffset enqueueAt)
    {
        return BackgroundJob.Schedule(methodCall, enqueueAt);
    }

    public string Schedule<T>(Expression<Action<T>> methodCall, DateTimeOffset enqueueAt)
    {
        return BackgroundJob.Schedule(methodCall, enqueueAt);
    }

    public async Task<QueuedJobsResult> GetQueuedJobsAsync(GetQueuedJobsRequest request)
    {
        using var connection = JobStorage.Current.GetConnection();
        var monitoringApi = JobStorage.Current.GetMonitoringApi();
        
        var queueName = request.Queue ?? "default";
        var enqueuedJobs = monitoringApi.EnqueuedJobs(queueName, request.Offset, request.Count);
        var jobs = enqueuedJobs.Select(j => MapToBackgroundJobInfo(j)).ToList();
        
        // Filter by state if specified
        if (!string.IsNullOrEmpty(request.State))
        {
            jobs = jobs.Where(j => j.State.Equals(request.State, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        
        var totalCount = monitoringApi.EnqueuedCount(queueName);
        
        return new QueuedJobsResult
        {
            Jobs = jobs,
            TotalCount = (int)totalCount
        };
    }

    public async Task<QueuedJobsResult> GetScheduledJobsAsync(GetScheduledJobsRequest request)
    {
        using var connection = JobStorage.Current.GetConnection();
        var monitoringApi = JobStorage.Current.GetMonitoringApi();
        
        var scheduledJobs = monitoringApi.ScheduledJobs(request.Offset, request.Count);
        var jobs = scheduledJobs.Select(j => MapToBackgroundJobInfo(j)).ToList();
        
        // Filter by date range if specified
        if (request.FromDate.HasValue)
        {
            jobs = jobs.Where(j => j.ScheduledAt >= request.FromDate.Value).ToList();
        }
        
        if (request.ToDate.HasValue)
        {
            jobs = jobs.Where(j => j.ScheduledAt <= request.ToDate.Value).ToList();
        }
        
        var totalCount = monitoringApi.ScheduledCount();
        
        return new QueuedJobsResult
        {
            Jobs = jobs,
            TotalCount = (int)totalCount
        };
    }

    public async Task<BackgroundJobInfo?> GetJobAsync(GetJobRequest request)
    {
        using var connection = JobStorage.Current.GetConnection();
        var jobData = connection.GetJobData(request.JobId);
        
        if (jobData == null)
            return null;
            
        var jobInfo = MapToBackgroundJobInfo(jobData, request.JobId);
        
        // If history is requested, we could add more details here in the future
        if (request.IncludeHistory)
        {
            // TODO: Add job history details when needed
        }
        
        return jobInfo;
    }

    public async Task<QueuedJobsResult> GetFailedJobsAsync(GetFailedJobsRequest request)
    {
        using var connection = JobStorage.Current.GetConnection();
        var monitoringApi = JobStorage.Current.GetMonitoringApi();
        
        var failedJobs = monitoringApi.FailedJobs(request.Offset, request.Count);
        var jobs = failedJobs.Select(j => MapToBackgroundJobInfo(j)).ToList();
        
        // Filter by date range if specified
        if (request.FromDate.HasValue)
        {
            jobs = jobs.Where(j => j.EnqueuedAt >= request.FromDate.Value).ToList();
        }
        
        if (request.ToDate.HasValue)
        {
            jobs = jobs.Where(j => j.EnqueuedAt <= request.ToDate.Value).ToList();
        }
        
        var totalCount = monitoringApi.FailedCount();
        
        return new QueuedJobsResult
        {
            Jobs = jobs,
            TotalCount = (int)totalCount
        };
    }

    private static BackgroundJobInfo MapToBackgroundJobInfo(JobData jobData, string? jobId = null)
    {
        return new BackgroundJobInfo
        {
            Id = jobId ?? string.Empty,
            Method = jobData.Job?.ToString() ?? string.Empty,
            State = jobData.State ?? string.Empty,
            EnqueuedAt = jobData.CreatedAt,
            ScheduledAt = jobData.State == "Scheduled" ? jobData.CreatedAt : null,
            Arguments = jobData.Job?.Args != null ? string.Join(", ", jobData.Job.Args) : null
        };
    }

    private static BackgroundJobInfo MapToBackgroundJobInfo(KeyValuePair<string, EnqueuedJobDto> job)
    {
        return new BackgroundJobInfo
        {
            Id = job.Key,
            Method = job.Value.Job?.ToString() ?? string.Empty,
            State = job.Value.State ?? string.Empty,
            EnqueuedAt = job.Value.EnqueuedAt,
            ScheduledAt = null,
            Arguments = job.Value.Job?.Args != null ? string.Join(", ", job.Value.Job.Args) : null
        };
    }

    private static BackgroundJobInfo MapToBackgroundJobInfo(KeyValuePair<string, ScheduledJobDto> job)
    {
        return new BackgroundJobInfo
        {
            Id = job.Key,
            Method = job.Value.Job?.ToString() ?? string.Empty,
            State = "Scheduled",
            EnqueuedAt = null,
            ScheduledAt = job.Value.ScheduledAt,
            Arguments = job.Value.Job?.Args != null ? string.Join(", ", job.Value.Job.Args) : null
        };
    }

    private static BackgroundJobInfo MapToBackgroundJobInfo(KeyValuePair<string, FailedJobDto> job)
    {
        return new BackgroundJobInfo
        {
            Id = job.Key,
            Method = job.Value.Job?.ToString() ?? string.Empty,
            State = "Failed",
            EnqueuedAt = job.Value.FailedAt,
            ScheduledAt = null,
            Arguments = job.Value.Job?.Args != null ? string.Join(", ", job.Value.Job.Args) : null,
            Exception = job.Value.ExceptionMessage
        };
    }
}