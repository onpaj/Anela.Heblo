namespace Anela.Heblo.Domain.Features.Packaging;

public interface IPackageRepository
{
    Task<(List<Package> Items, int TotalCount)> GetPaginatedAsync(
        string? orderCode,
        string? customerName,
        string? packageNumber,
        IReadOnlyList<string>? shippingProviderCodes,
        DateTime? fromDate,
        DateTime? toDate,
        int pageNumber,
        int pageSize,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken = default);

    Task<Package?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically replaces all persisted packages for an order with the supplied set
    /// (delete existing rows for the order, then insert the new ones, in a single save).
    /// Makes re-scanning an order idempotent and avoids partial/duplicate audit rows.
    /// </summary>
    Task ReplacePackagesForOrderAsync(
        string orderCode,
        IReadOnlyCollection<Package> packages,
        CancellationToken cancellationToken = default);

    Task AddAsync(Package package, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the given packages, skipping any whose (OrderCode, PackageNumber) already exists.
    /// Idempotent: safe to call on a reprint that already has rows.
    /// </summary>
    Task AddMissingAsync(IReadOnlyList<Package> packages, CancellationToken cancellationToken = default);

    Task DeleteAsync(Package package, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns packages created within the last <paramref name="daysBack"/> days
    /// whose <see cref="Package.TrackingNumber"/> is null.
    /// </summary>
    Task<List<Package>> GetWithNullTrackingNumberAsync(int daysBack, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a tracking number onto an existing package row.
    /// No-ops silently if the row no longer exists.
    /// </summary>
    Task SetTrackingNumberAsync(int id, string trackingNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <paramref name="trackingNumber"/> on every package row for the given order
    /// whose <see cref="Package.TrackingNumber"/> is currently null. No-ops if there are none.
    /// </summary>
    Task SetTrackingNumberByOrderCodeAsync(string orderCode, string trackingNumber, CancellationToken cancellationToken = default);

    Task<(int TotalDistinctOrders, IReadOnlyList<PackerPackingSummary> ByPacker)>
        GetPackedTodayByPackerAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>
    /// Aggregates packing activity in the half-open window [fromUtc, toUtc) into a
    /// <see cref="PackingStatistics"/>. Day-of-week and hour-of-day buckets are computed
    /// in <paramref name="localZone"/> so they reflect the warehouse's local working hours.
    /// </summary>
    Task<PackingStatistics> GetPackingStatisticsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        TimeZoneInfo localZone,
        CancellationToken ct = default);
}
