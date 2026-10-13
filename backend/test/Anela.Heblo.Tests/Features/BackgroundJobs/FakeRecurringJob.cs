using Anela.Heblo.Domain.Features.BackgroundJobs;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

/// <summary>
/// Minimal <see cref="IRecurringJob"/> stand-in used to supply job metadata
/// (notably <see cref="RecurringJobCategory"/>) to handlers under test.
/// </summary>
public class FakeRecurringJob : IRecurringJob
{
    public FakeRecurringJob(string jobName, RecurringJobCategory category)
    {
        Metadata = new RecurringJobMetadata
        {
            JobName = jobName,
            DisplayName = jobName,
            Description = $"{jobName} description",
            Category = category,
            CronExpression = "0 0 * * *"
        };
    }

    public RecurringJobMetadata Metadata { get; }

    public Task ExecuteAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
