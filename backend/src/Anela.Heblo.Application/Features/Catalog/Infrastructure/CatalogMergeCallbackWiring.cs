using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

public sealed class CatalogMergeCallbackWiring : IHostedService
{
    private readonly ICatalogMergeScheduler _scheduler;
    private readonly CatalogMergeService _mergeService;

    public CatalogMergeCallbackWiring(
        ICatalogMergeScheduler scheduler,
        CatalogMergeService mergeService)
    {
        _scheduler = scheduler;
        _mergeService = mergeService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _scheduler.SetMergeCallback(_mergeService.ExecuteBackgroundMergeAsync);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
