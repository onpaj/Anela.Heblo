namespace Anela.Heblo.Xcc;

public class HangfireOptions
{
    public const string ConfigurationKey = "Hangfire";
    public string SchemaName { get; set; } = "hangfire_heblo";
    public bool SchedulerEnabled { get; set; } = false;
    public int WorkerCount { get; set; } = 1;
    public bool UseInMemoryStorage { get; set; } = false;
    public int ConnectionLimit { get; set; } = 5;

    // Page cap applied to Hangfire monitoring API calls (ProcessingJobs, EnqueuedJobs, ScheduledJobs).
    // Replaces previous use of int.MaxValue.
    public int MaxPendingJobsPageSize { get; set; } = 200;

    // TTL (seconds) for the in-memory cache of GetRunningInvoiceImportJobs responses.
    // Set to 0 (or any non-positive value) to disable caching entirely.
    public int RunningJobsCacheSeconds { get; set; } = 2;
}
