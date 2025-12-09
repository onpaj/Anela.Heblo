namespace Anela.Heblo.Domain.Features.Bank;

public interface IBankStatementImportRepository
{
    Task<(IEnumerable<BankStatementImport> Items, int TotalCount)> GetFilteredAsync(
        int? id = null,
        DateTime? statementDate = null,
        DateTime? importDate = null,
        int skip = 0,
        int take = 50,
        string orderBy = "ImportDate",
        bool ascending = false);
    
    Task<BankStatementImport?> GetByIdAsync(int id);
    Task<BankStatementImport> AddAsync(BankStatementImport bankStatement);
}