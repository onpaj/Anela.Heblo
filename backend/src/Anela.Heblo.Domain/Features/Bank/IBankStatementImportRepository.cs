namespace Anela.Heblo.Domain.Features.Bank;

public interface IBankStatementImportRepository
{
    Task<IEnumerable<BankStatementImport>> GetAllAsync();
    Task<BankStatementImport?> GetByIdAsync(int id);
    Task<IEnumerable<BankStatementImportStatistics>> GetImportStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null);
    Task<BankStatementImport> AddAsync(BankStatementImport bankStatement);
    Task UpdateAsync(BankStatementImport bankStatement);
    Task DeleteAsync(int id);
}