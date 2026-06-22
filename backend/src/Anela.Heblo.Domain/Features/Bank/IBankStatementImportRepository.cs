namespace Anela.Heblo.Domain.Features.Bank;

public interface IBankStatementImportRepository
{
    Task<(IEnumerable<BankStatementImport> Items, int TotalCount)> GetFilteredAsync(
        BankStatementListFilter filter,
        int skip = 0,
        int take = 50,
        string orderBy = "ImportDate",
        bool ascending = false,
        CancellationToken cancellationToken = default);

    Task<BankStatementImport?> GetByIdAsync(int id);
    Task<BankStatementImport> AddAsync(BankStatementImport bankStatement);

    Task<IReadOnlyDictionary<string, string>> GetExistingTransfersAsync(
        string account, DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default);

    Task<DateTime?> GetMaxStatementDateAsync(string account, CancellationToken cancellationToken = default);

    Task<BankStatementImport?> GetByTransferIdAsync(string transferId, CancellationToken cancellationToken = default);

    Task<BankStatementImport> UpdateAsync(BankStatementImport bankStatement);
}