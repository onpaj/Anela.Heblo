using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public static class ManufactureCatalogSourceExtensions
{
    /// <summary>
    /// Refreshes planned manufacture quantities without letting a cache failure fail the caller.
    /// The order write has already been committed by the time this runs, so a failure here only
    /// means the catalog stays stale until the scheduled background refresh catches up.
    /// </summary>
    public static async Task RefreshPlannedDataSafelyAsync(
        this IManufactureCatalogSource catalogSource,
        ILogger logger,
        int orderId,
        CancellationToken cancellationToken)
    {
        try
        {
            await catalogSource.RefreshPlannedDataAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Failed to refresh planned catalog data after updating manufacture order {OrderId}. " +
                "The scheduled background refresh will pick the change up.",
                orderId);
        }
    }
}
