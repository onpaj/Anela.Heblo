using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Campaigns;

/// <summary>
/// Encapsulates the common sync lifecycle: create sync log, execute platform sync, complete or fail.
/// Subclasses implement <see cref="SyncAsync"/> for platform-specific work.
/// </summary>
public abstract class AdSyncHandlerBase
{
    protected abstract AdPlatform Platform { get; }

    protected abstract Task<(int Campaigns, int AdSets, int Ads, int Metrics)> SyncAsync(
        CancellationToken cancellationToken);

    protected async Task ExecuteSyncAsync(
        ICampaignRepository repository,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var syncLog = new AdSyncLog
        {
            Id = Guid.NewGuid(),
            Platform = Platform,
            Status = AdSyncStatus.Running,
            StartedAt = DateTime.UtcNow,
        };

        await repository.AddSyncLogAsync(syncLog, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        try
        {
            var (campaigns, adSets, ads, metrics) = await SyncAsync(cancellationToken);

            syncLog.Complete(campaigns, adSets, ads, metrics);
            await repository.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "{Platform} sync completed: {Campaigns} campaigns, {AdSets} ad sets, {Ads} ads, {Metrics} metric rows",
                Platform, campaigns, adSets, ads, metrics);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Platform} sync failed", Platform);
            syncLog.Fail(ex.Message);
            await repository.SaveChangesAsync(cancellationToken);
            throw;
        }
    }
}
