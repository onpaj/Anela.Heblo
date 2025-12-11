namespace Anela.Heblo.Domain.Accounting.Ledger;

public interface ILedgerService
{
    Task<IList<LedgerItem>> GetLedgerItems(DateOnly dateFrom, DateOnly dateTo, IEnumerable<string>? debitAccountPrefix = null, IEnumerable<string>? creditAccountPrefix = null, string? department = null, CancellationToken cancellationToken = default);

    Task<IList<CostStatistics>> GetPersonalCosts(DateOnly dateFrom, DateOnly dateTo, string? department = null, CancellationToken cancellationToken = default);

    Task<IList<CostStatistics>> GetDirectCosts(DateOnly dateFrom, DateOnly dateTo, string? department = null, CancellationToken cancellationToken = default);

    Task<IList<CostStatistics>> GetCosts(DateOnly dateFrom, DateOnly dateTo, IEnumerable<string> debitAccountPrefixes, string? department = null, CancellationToken cancellationToken = default);
}
