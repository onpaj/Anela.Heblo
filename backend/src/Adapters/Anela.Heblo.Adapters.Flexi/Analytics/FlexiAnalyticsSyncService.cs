using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public record FlexiAnalyticsSyncReport(
    int TotalFetched,
    int TotalUpserted,
    int FailedServices,
    bool IsFullSuccess);

public sealed class FlexiAnalyticsSyncService : IFlexiAnalyticsSyncService
{
    private readonly IEnumerable<IEntitySyncService> _services;
    private readonly ILogger<FlexiAnalyticsSyncService> _logger;

    public FlexiAnalyticsSyncService(
        IEnumerable<IEntitySyncService> services,
        ILogger<FlexiAnalyticsSyncService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task<FlexiAnalyticsSyncReport> SyncAllAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("FlexiAnalyticsSync.Started");

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
                    "FlexiAnalyticsSync.ServiceCompleted {ServiceName} rowsFetched={RowsFetched} rowsUpserted={RowsUpserted} isSuccess={IsSuccess}",
                    serviceName, result.RowsFetched, result.RowsUpserted, result.IsSuccess);

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
                _logger.LogError(ex,
                    "FlexiAnalyticsSync.ServiceFailed {ServiceName}",
                    serviceName);

                failedServices++;
            }
        }

        _logger.LogInformation(
            "FlexiAnalyticsSync.Completed totalFetched={TotalFetched} totalUpserted={TotalUpserted} failedServices={FailedServices}",
            totalFetched, totalUpserted, failedServices);

        return new FlexiAnalyticsSyncReport(
            TotalFetched: totalFetched,
            TotalUpserted: totalUpserted,
            FailedServices: failedServices,
            IsFullSuccess: failedServices == 0);
    }
}
