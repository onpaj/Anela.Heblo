using System.Linq.Expressions;

namespace Anela.Heblo.Xcc.Services;

public interface IBackgroundWorker
{
    string Enqueue<T>(Expression<Func<T, Task>> methodCall);
    string Enqueue<T>(Expression<Action<T>> methodCall);
    string Schedule<T>(Expression<Func<T, Task>> methodCall, TimeSpan delay);
    string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay);
    string Schedule<T>(Expression<Func<T, Task>> methodCall, DateTimeOffset enqueueAt);
    string Schedule<T>(Expression<Action<T>> methodCall, DateTimeOffset enqueueAt);
    
    Task<QueuedJobsResult> GetQueuedJobsAsync(GetQueuedJobsRequest request);
    Task<QueuedJobsResult> GetScheduledJobsAsync(GetScheduledJobsRequest request);
    Task<QueuedJobsResult> GetFailedJobsAsync(GetFailedJobsRequest request);
    Task<BackgroundJobInfo?> GetJobAsync(GetJobRequest request);
}