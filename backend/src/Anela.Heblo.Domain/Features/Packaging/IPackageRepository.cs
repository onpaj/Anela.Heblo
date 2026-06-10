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
    Task AddAsync(Package package, CancellationToken cancellationToken = default);
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
}
