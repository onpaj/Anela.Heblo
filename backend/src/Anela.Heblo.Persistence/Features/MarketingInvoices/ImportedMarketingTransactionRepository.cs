using Anela.Heblo.Domain.Features.MarketingInvoices;
using Anela.Heblo.Persistence.Repositories;

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

    public new async Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct)
    {
        await base.AddAsync(entity, ct);
    }

    public async Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct)
    {
        return (await FindAsync(x => !x.IsSynced, ct)).ToList();
    }
}
