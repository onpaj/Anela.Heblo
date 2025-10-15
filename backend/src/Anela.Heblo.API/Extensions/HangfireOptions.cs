namespace Anela.Heblo.API.Extensions;

public class HangfireOptions
{
    public static string ConfigurationKey => "Hangfire";
    public string SchemaName { get; set; } = "hangfire_heblo";
    public string QueueName { get; set; } = "heblo";
    public bool SchedulerEnabled { get; set; } = false;
    public int WorkerCount { get; set; } = 1;
    public bool UseInMemoryStorage { get; set; } = false;
}