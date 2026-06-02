using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Wires the merge callback from the scheduler to the merge service at application startup.
/// This breaks the circular dependency between CatalogRepository and ICatalogMergeScheduler.
/// </summary>
public sealed class CatalogMergeCallbackWiring : IHostedService
{
    private readonly ICatalogMergeScheduler _scheduler;
    private readonly CatalogMergeService _mergeService;

    public CatalogMergeCallbackWiring(
        ICatalogMergeScheduler scheduler,
        CatalogMergeService mergeService)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _mergeService = mergeService ?? throw new ArgumentNullException(nameof(mergeService));
    }

    /// <summary>
    /// Called when the application starts. Registers the merge callback.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _scheduler.SetMergeCallback(_mergeService.ExecuteBackgroundMergeAsync);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the application stops. No cleanup needed.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
