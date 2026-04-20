using Anela.Heblo.Domain.Features.MarketingInvoices;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.MarketingInvoices;

public class ImportedMarketingTransactionRepository
    : BaseRepository<ImportedMarketingTransaction, int>, IImportedMarketingTransactionRepository
{
    public ImportedMarketingTransactionRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct)
    {
        return await AnyAsync(
            x => x.Platform == platform && x.TransactionId == transactionId,
            ct);
    }

    async Task IImportedMarketingTransactionRepository.AddAsync(ImportedMarketingTransaction entity, CancellationToken ct)
    {
        await base.AddAsync(entity, ct);
    }

    async Task IImportedMarketingTransactionRepository.AddRangeAsync(IEnumerable<ImportedMarketingTransaction> entities, CancellationToken ct)
    {
        await base.AddRangeAsync(entities, ct);
    }

    public async Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct)
    {
        return (await FindAsync(x => !x.IsSynced, ct)).ToList();
    }

    public override async Task<ImportedMarketingTransaction?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await DbSet
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<(List<ImportedMarketingTransaction> Items, int TotalCount)> GetPagedAsync(
        string? platform,
        DateTime? dateFrom,
        DateTime? dateTo,
        bool? isSynced,
        string? sortBy,
        bool sortDescending,
        int pageNumber,
        int pageSize,
        CancellationToken ct)
    {
        var query = DbSet.AsQueryable();

        if (!string.IsNullOrEmpty(platform))
            query = query.Where(x => x.Platform == platform);

        if (dateFrom.HasValue)
            query = query.Where(x => x.TransactionDate >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(x => x.TransactionDate <= dateTo.Value);

        if (isSynced.HasValue)
            query = query.Where(x => x.IsSynced == isSynced.Value);

        var totalCount = await query.CountAsync(ct);

        query = sortBy?.ToLower() switch
        {
            "amount" => sortDescending ? query.OrderByDescending(x => x.Amount) : query.OrderBy(x => x.Amount),
            "transactiondate" => sortDescending ? query.OrderByDescending(x => x.TransactionDate) : query.OrderBy(x => x.TransactionDate),
            "importedat" => sortDescending ? query.OrderByDescending(x => x.ImportedAt) : query.OrderBy(x => x.ImportedAt),
            _ => query.OrderByDescending(x => x.TransactionDate)
        };

        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
