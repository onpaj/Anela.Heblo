using Anela.Heblo.Domain.Features.Packaging;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Repositories.Packaging;

public class PackageRepository : IPackageRepository
{
    private readonly ApplicationDbContext _db;

    public PackageRepository(ApplicationDbContext db) => _db = db;

    public async Task<(List<Package> Items, int TotalCount)> GetPaginatedAsync(
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
        CancellationToken cancellationToken = default)
    {
        IQueryable<Package> q = _db.Packages.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(orderCode))
            q = q.Where(p => EF.Functions.ILike(p.OrderCode, $"%{EscapeLike(orderCode)}%", "\\"));
        if (!string.IsNullOrWhiteSpace(customerName))
            q = q.Where(p => EF.Functions.ILike(p.CustomerName, $"%{EscapeLike(customerName)}%", "\\"));
        if (!string.IsNullOrWhiteSpace(packageNumber))
            q = q.Where(p => EF.Functions.ILike(p.PackageNumber, $"%{EscapeLike(packageNumber)}%", "\\"));
        if (!string.IsNullOrWhiteSpace(shippingProviderCode))
            q = q.Where(p => p.ShippingProviderCode == shippingProviderCode);
        if (fromDate.HasValue)
            q = q.Where(p => p.PackedAt >= new DateTimeOffset(DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc)));
        if (toDate.HasValue)
            q = q.Where(p => p.PackedAt < new DateTimeOffset(DateTime.SpecifyKind(toDate.Value.Date.AddDays(1), DateTimeKind.Utc)));

        var total = await q.CountAsync(cancellationToken);

        q = ((sortBy ?? string.Empty).ToLowerInvariant(), sortDescending) switch
        {
            ("ordercode", true) => q.OrderByDescending(p => p.OrderCode),
            ("ordercode", false) => q.OrderBy(p => p.OrderCode),
            ("customername", true) => q.OrderByDescending(p => p.CustomerName),
            ("customername", false) => q.OrderBy(p => p.CustomerName),
            ("packagenumber", true) => q.OrderByDescending(p => p.PackageNumber),
            ("packagenumber", false) => q.OrderBy(p => p.PackageNumber),
            ("shippingprovider", true) => q.OrderByDescending(p => p.ShippingProviderName ?? p.ShippingProviderCode),
            ("shippingprovider", false) => q.OrderBy(p => p.ShippingProviderName ?? p.ShippingProviderCode),
            (_, true) => q.OrderByDescending(p => p.PackedAt),
            (_, false) => q.OrderBy(p => p.PackedAt),
        };

        var items = await q.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<Package?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Packages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task ReplacePackagesForOrderAsync(
        string orderCode,
        IReadOnlyCollection<Package> packages,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.Packages
            .Where(p => p.OrderCode == orderCode)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
            _db.Packages.RemoveRange(existing);

        await _db.Packages.AddRangeAsync(packages, cancellationToken);

        // Single save: EF orders deletes before inserts, so replacing rows that share
        // the (OrderCode, PackageNumber) unique key never trips a transient collision.
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Package package, CancellationToken cancellationToken = default)
    {
        _db.Packages.Remove(package);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
