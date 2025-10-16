using System.Linq.Expressions;
using Anela.Heblo.API.Extensions;
using Anela.Heblo.Xcc.Services;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Options;
using System.ComponentModel;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

public class HangfireBackgroundWorker : IBackgroundWorker
{
    private readonly string _queueName;

    public HangfireBackgroundWorker(IOptions<HangfireOptions> hangfireOptions)
    {
        _queueName = hangfireOptions.Value.QueueName;
    }
    public string Enqueue<T>(Expression<Func<T, Task>> methodCall)
    {
        return BackgroundJob.Enqueue(_queueName, methodCall);
    }

    public string Enqueue<T>(Expression<Action<T>> methodCall)
    {
        return BackgroundJob.Enqueue(_queueName, methodCall);
    }



    public string Schedule<T>(Expression<Func<T, Task>> methodCall, TimeSpan delay)
    {
        return BackgroundJob.Schedule(_queueName, methodCall, delay);
    }

    public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
    {
        return BackgroundJob.Schedule(_queueName, methodCall, delay);
    }

    public string Schedule<T>(Expression<Func<T, Task>> methodCall, DateTimeOffset enqueueAt)
    {
        return BackgroundJob.Schedule(_queueName, methodCall, enqueueAt);
    }

    public string Schedule<T>(Expression<Action<T>> methodCall, DateTimeOffset enqueueAt)
    {
        return BackgroundJob.Schedule(_queueName, methodCall, enqueueAt);
    }

    public IList<BackgroundJobInfo> GetPendingJobs()
    {
        using var connection = JobStorage.Current.GetConnection();
        var monitoring = JobStorage.Current.GetMonitoringApi();

        var enqueuedJobs = monitoring.EnqueuedJobs(_queueName, 0, int.MaxValue);
        var scheduledJobs = monitoring.ScheduledJobs(0, int.MaxValue);

        var result = new List<BackgroundJobInfo>();

        // Add enqueued jobs from the specific queue
        foreach (var job in enqueuedJobs)
        {
            result.Add(new BackgroundJobInfo
            {
                Id = job.Key,
                JobName = GetJobDisplayName(job.Key, job.Value.Job),
                State = "Enqueued",
                CreatedAt = job.Value.EnqueuedAt,
                Queue = _queueName
            });
        }

        // Add scheduled jobs that belong to our queue
        foreach (var job in scheduledJobs)
        {
            var jobDetails = connection.GetJobData(job.Key);
            if (jobDetails?.Job != null)
            {
                var jobQueue = jobDetails.Job.Queue ?? "default";
                if (jobQueue == _queueName)
                {
                    result.Add(new BackgroundJobInfo
                    {
                        Id = job.Key,
                        JobName = GetJobDisplayName(job.Key, job.Value.Job),
                        State = "Scheduled",
                        CreatedAt = job.Value.EnqueueAt,
                        Queue = jobQueue
                    });
                }
            }
        }

        return result;
    }

    public IList<BackgroundJobInfo> GetRunningJobs()
    {
        var monitoring = JobStorage.Current.GetMonitoringApi();
        var processingJobs = monitoring.ProcessingJobs(0, int.MaxValue);

        var result = new List<BackgroundJobInfo>();

        foreach (var job in processingJobs)
        {
            // Check if this job belongs to our queue by examining the job data
            using var connection = JobStorage.Current.GetConnection();
            var jobDetails = connection.GetJobData(job.Key);

            if (jobDetails?.Job != null)
            {
                var jobQueue = jobDetails.Job.Queue ?? "default";
                if (jobQueue == _queueName)
                {
                    result.Add(new BackgroundJobInfo
                    {
                        Id = job.Key,
                        JobName = GetJobDisplayName(job.Key, job.Value.Job),
                        State = "Processing",
                        CreatedAt = jobDetails.CreatedAt,
                        StartedAt = job.Value.StartedAt,
                        Queue = jobQueue
                    });
                }
            }
        }

        return result;
    }

    public BackgroundJobInfo? GetJobById(string jobId)
    {
        try
        {
            using var connection = JobStorage.Current.GetConnection();
            var jobDetails = connection.GetJobData(jobId);

            if (jobDetails?.Job == null)
                return null;

            var jobQueue = jobDetails.Job.Queue ?? "default";

            // Only return jobs from our queue
            if (jobQueue != _queueName)
                return null;

            // Determine job state by checking various job states
            var state = GetJobState(connection, jobId);

            return new BackgroundJobInfo
            {
                Id = jobId,
                JobName = GetJobDisplayName(jobId, jobDetails.Job),
                State = state,
                CreatedAt = jobDetails.CreatedAt,
                StartedAt = GetJobStartedAt(connection, jobId),
                Queue = jobQueue
            };
        }
        catch (Exception)
        {
            // If Hangfire monitoring fails, return null gracefully
            return null;
        }
    }

    private static string GetJobState(IStorageConnection connection, string jobId)
    {
        var stateData = connection.GetStateData(jobId);
        return stateData?.Name ?? "Unknown";
    }

    private static DateTime? GetJobStartedAt(IStorageConnection connection, string jobId)
    {
        try
        {
            // Try to get job details and check for processing state time
            var monitoring = JobStorage.Current.GetMonitoringApi();
            var processingJobs = monitoring.ProcessingJobs(0, int.MaxValue);

            var processingJob = processingJobs.FirstOrDefault(j => j.Key == jobId);
            if (processingJob.Value != null)
                return processingJob.Value.StartedAt;

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string GetJobDisplayName(string jobId, Job job)
    {
        if (job?.Method?.Name == null)
            return "Unknown Job";

        // First try to get custom display name from job parameters
        try
        {
            using var connection = JobStorage.Current.GetConnection();
            var customDisplayName = connection.GetJobParameter(jobId, "DisplayName");
            if (!string.IsNullOrEmpty(customDisplayName))
                return customDisplayName;
        }
        catch
        {
            // Fall back if parameter retrieval fails
        }

        // Use DisplayName attribute if available
        var displayNameAttribute = job.Method.GetCustomAttributes(typeof(System.ComponentModel.DisplayNameAttribute), false)
            .FirstOrDefault() as System.ComponentModel.DisplayNameAttribute;

        if (displayNameAttribute != null)
        {
            // Replace placeholders with actual argument values
            var displayName = displayNameAttribute.DisplayName;
            for (int i = 0; i < job.Args.Count; i++)
            {
                displayName = displayName.Replace($"{{{i}}}", job.Args[i]?.ToString() ?? "null");
            }
            return displayName;
        }

        // Fallback to method name with args
        var methodName = job.Method.Name;

        if (job.Args?.Count > 0)
        {
            var argsDisplay = string.Join(", ", job.Args.Select(arg =>
            {
                if (arg == null)
                    return "null";

                // For strings, show them in quotes
                if (arg is string stringArg)
                    return $"\"{stringArg}\"";

                // For primitives, show their string representation
                if (arg.GetType().IsPrimitive || arg is decimal || arg is DateTime || arg is DateTimeOffset)
                    return arg.ToString();

                // For complex objects, show their type name
                return $"<{arg.GetType().Name}>";
            }));

            return $"{methodName}({argsDisplay})";
        }

        return $"{methodName}()";
    }
}