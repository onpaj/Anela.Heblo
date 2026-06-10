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

    Task<(int TotalDistinctOrders, IReadOnlyList<PackerPackingSummary> ByPacker)>
        GetPackedTodayByPackerAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}
