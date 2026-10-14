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

    public async Task<HashSet<string>> GetExistingTransactionIdsAsync(
        string platform, IEnumerable<string> transactionIds, CancellationToken ct)
    {
        var ids = transactionIds is ICollection<string> c ? c : transactionIds.ToList();
        if (ids.Count == 0)
            return new HashSet<string>();

        var existing = await DbSet
            .Where(x => x.Platform == platform && ids.Contains(x.TransactionId))
            .Select(x => x.TransactionId)
            .ToListAsync(ct);

        return existing.ToHashSet();
    }

}
