using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Sync;

public sealed record Ga4SyncReport(
    int TotalFetched,
    int TotalUpserted,
    int FailedServices,
    bool IsFullSuccess);

public interface IGa4SyncService
{
    Task<Ga4SyncReport> SyncAllAsync(CancellationToken ct = default);
}

/// <summary>
/// Runs every table's sync in turn. Sequential on purpose: the Data API caps concurrent requests
/// per property, and the destination is a shared single-vCore Postgres.
/// </summary>
public sealed class Ga4SyncService : IGa4SyncService
{
    private readonly IEnumerable<IGa4EntitySyncService> _services;
    private readonly ILogger<Ga4SyncService> _logger;

    public Ga4SyncService(IEnumerable<IGa4EntitySyncService> services, ILogger<Ga4SyncService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task<Ga4SyncReport> SyncAllAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Ga4Sync.Started");

        var totalFetched = 0;
        var totalUpserted = 0;
        var failedServices = 0;

        foreach (var service in _services)
        {
            var serviceName = service.GetType().Name;

            try
            {
                var result = await service.SyncAsync(ct);

                _logger.LogInformation(
                    "Ga4Sync.ServiceCompleted {ServiceName} entity={EntityName} rowsFetched={RowsFetched} rowsUpserted={RowsUpserted} isSuccess={IsSuccess}",
                    serviceName, result.EntityName, result.RowsFetched, result.RowsUpserted, result.IsSuccess);

                if (result.IsSuccess)
                {
                    totalFetched += result.RowsFetched;
                    totalUpserted += result.RowsUpserted;
                }
                else
                {
                    failedServices++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ga4Sync.ServiceFailed {ServiceName}", serviceName);
                failedServices++;
            }
        }

        _logger.LogInformation(
            "Ga4Sync.Completed totalFetched={TotalFetched} totalUpserted={TotalUpserted} failedServices={FailedServices}",
            totalFetched, totalUpserted, failedServices);

        return new Ga4SyncReport(totalFetched, totalUpserted, failedServices, failedServices == 0);
    }
}
