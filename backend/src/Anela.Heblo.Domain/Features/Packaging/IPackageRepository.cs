namespace Anela.Heblo.Domain.Features.Packaging;

public interface IPackageRepository
{
    Task<(List<Package> Items, int TotalCount)> GetPaginatedAsync(
        string? orderCode,
        string? customerName,
        string? packageNumber,
        string? shippingProviderCode,
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

    Task DeleteAsync(Package package, CancellationToken cancellationToken = default);
}
