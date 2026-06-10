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
        IReadOnlyList<string>? shippingProviderCodes,
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
        if (shippingProviderCodes != null)
            q = q.Where(p => shippingProviderCodes.Contains(p.ShippingProviderCode));
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

    public async Task AddAsync(Package package, CancellationToken cancellationToken = default)
    {
        await _db.Packages.AddAsync(package, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Package package, CancellationToken cancellationToken = default)
    {
        _db.Packages.Remove(package);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(int TotalDistinctOrders, IReadOnlyList<PackerPackingSummary> ByPacker)>
        GetPackedTodayByPackerAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        // Npgsql requires UTC-zero DateTimeOffset values for timestamptz comparisons.
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();

        // SELECT DISTINCT (packer, orderCode) triples — far fewer rows than loading all packages.
        var pairs = await _db.Packages
            .AsNoTracking()
            .Where(p => p.PackedAt >= from && p.PackedAt < to)
            .Select(p => new { p.PackedByUserId, p.PackedBy, p.OrderCode })
            .Distinct()
            .ToListAsync(ct);

        var byPacker = pairs
            .GroupBy(p => new { p.PackedByUserId, p.PackedBy })
            .Select(g => new PackerPackingSummary(g.Key.PackedByUserId, g.Key.PackedBy, g.Count()))
            .OrderByDescending(x => x.DistinctOrderCount)
            .ToList();

        var total = pairs.Select(p => p.OrderCode).Distinct().Count();

        return (total, byPacker);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
