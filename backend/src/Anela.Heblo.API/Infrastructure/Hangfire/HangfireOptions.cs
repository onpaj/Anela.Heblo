namespace Anela.Heblo.API.Infrastructure.Hangfire;

public class HangfireOptions
{
    public static string ConfigurationKey =>  "Hangfire";
    public string SchemaName { get; set; } = "hangfire_heblo";
    public string QueueName { get; set; } = "default";
    public bool SchedulerEnabled { get; set; } = false;
    public int WorkerCount { get; set; } = 1;
    public bool UseInMemoryStorage { get; set; } = false;
}