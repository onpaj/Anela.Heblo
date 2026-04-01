using System.Linq.Expressions;
using Anela.Heblo.Xcc.Services;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using System.ComponentModel;

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

    public IList<BackgroundJobInfo> GetPendingJobs()
    {
        using var connection = JobStorage.Current.GetConnection();
        var monitoring = JobStorage.Current.GetMonitoringApi();

        var enqueuedJobs = monitoring.EnqueuedJobs("default", 0, int.MaxValue);
        var scheduledJobs = monitoring.ScheduledJobs(0, int.MaxValue);

        var result = new List<BackgroundJobInfo>();

        foreach (var job in enqueuedJobs)
        {
            result.Add(new BackgroundJobInfo
            {
                Id = job.Key,
                JobName = GetJobDisplayName(job.Key, job.Value.Job),
                State = "Enqueued",
                CreatedAt = job.Value.EnqueuedAt,
                Queue = "default"
            });
        }

        foreach (var job in scheduledJobs)
        {
            var jobDetails = connection.GetJobData(job.Key);
            if (jobDetails?.Job != null)
            {
                result.Add(new BackgroundJobInfo
                {
                    Id = job.Key,
                    JobName = GetJobDisplayName(job.Key, job.Value.Job),
                    State = "Scheduled",
                    CreatedAt = job.Value.EnqueueAt,
                    Queue = jobDetails.Job.Queue ?? "default"
                });
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
            using var connection = JobStorage.Current.GetConnection();
            var jobDetails = connection.GetJobData(job.Key);

            if (jobDetails?.Job != null)
            {
                result.Add(new BackgroundJobInfo
                {
                    Id = job.Key,
                    JobName = GetJobDisplayName(job.Key, job.Value.Job),
                    State = "Processing",
                    CreatedAt = jobDetails.CreatedAt,
                    StartedAt = job.Value.StartedAt,
                    Queue = jobDetails.Job.Queue ?? "default"
                });
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

            var state = GetJobState(connection, jobId);

            return new BackgroundJobInfo
            {
                Id = jobId,
                JobName = GetJobDisplayName(jobId, jobDetails.Job),
                State = state,
                CreatedAt = jobDetails.CreatedAt,
                StartedAt = GetJobStartedAt(connection, jobId),
                Queue = jobDetails.Job.Queue ?? "default"
            };
        }
        catch (Exception)
        {
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

        var displayNameAttribute = job.Method.GetCustomAttributes(typeof(System.ComponentModel.DisplayNameAttribute), false)
            .FirstOrDefault() as System.ComponentModel.DisplayNameAttribute;

        if (displayNameAttribute != null)
        {
            var displayName = displayNameAttribute.DisplayName;
            for (int i = 0; i < job.Args.Count; i++)
            {
                displayName = displayName.Replace($"{{{i}}}", job.Args[i]?.ToString() ?? "null");
            }
            return displayName;
        }

        var methodName = job.Method.Name;

        if (job.Args?.Count > 0)
        {
            var argsDisplay = string.Join(", ", job.Args.Select(arg =>
            {
                if (arg == null)
                    return "null";

                if (arg is string stringArg)
                    return $"\"{stringArg}\"";

                if (arg.GetType().IsPrimitive || arg is decimal || arg is DateTime || arg is DateTimeOffset)
                    return arg.ToString();

                return $"<{arg.GetType().Name}>";
            }));

            return $"{methodName}({argsDisplay})";
        }

        return $"{methodName}()";
    }
}
