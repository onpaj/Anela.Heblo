namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public interface IImportedMarketingTransactionRepository
{
    Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct);
    Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
    Task AddRangeAsync(IEnumerable<ImportedMarketingTransaction> entities, CancellationToken ct);
    Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct);
    Task<ImportedMarketingTransaction?> GetByIdAsync(int id, CancellationToken ct);
    Task<(List<ImportedMarketingTransaction> Items, int TotalCount)> GetPagedAsync(
        string? platform,
        DateTime? dateFrom,
        DateTime? dateTo,
        bool? isSynced,
        string? sortBy,
        bool sortDescending,
        int pageNumber,
        int pageSize,
        CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
