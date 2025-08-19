namespace Anela.Heblo.Domain.Accounting.Ledger;

public interface ILedgerService
{
    Task<IList<LedgerItem>> GetLedgerItems(DateTime dateFrom, DateTime dateTo, IEnumerable<string>? debitAccountPrefix = null, IEnumerable<string>? creditAccountPrefix = null, string? department = null, CancellationToken cancellationToken = default);

    Task<IList<CostStatistics>> GetPersonalCosts(DateTime dateFrom, DateTime dateTo, string? department = null, CancellationToken cancellationToken = default);
    
    Task<IList<CostStatistics>> GetDirectCosts(DateTime dateFrom, DateTime dateTo, string? department = null, CancellationToken cancellationToken = default);
    
    Task<IList<CostStatistics>> GetCosts(DateTime dateFrom, DateTime dateTo, IEnumerable<string> debitAccountPrefixes, string? department = null, CancellationToken cancellationToken = default);
}
