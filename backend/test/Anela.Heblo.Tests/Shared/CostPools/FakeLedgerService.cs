using Anela.Heblo.Domain.Accounting.Ledger;

namespace Anela.Heblo.Tests.Shared.CostPools;

/// <summary>
/// An ILedgerService backed by a single in-memory entry list, so that
/// GetLedgerItems and GetDirectCosts cannot disagree the way two independently
/// stubbed Moq setups can. Mirrors the real LedgerService: GetDirectCosts is
/// GetLedgerItems on prefixes 51+52, grouped by date and department.
/// </summary>
public class FakeLedgerService : ILedgerService
{
    private readonly IReadOnlyList<LedgerItem> _entries;

    public FakeLedgerService(IReadOnlyList<LedgerItem> entries)
    {
        _entries = entries;
    }

    public Task<IList<LedgerItem>> GetLedgerItems(
        DateTime dateFrom,
        DateTime dateTo,
        IEnumerable<string>? debitAccountPrefix = null,
        IEnumerable<string>? creditAccountPrefix = null,
        string? department = null,
        CancellationToken cancellationToken = default)
    {
        var prefixes = debitAccountPrefix?.ToList() ?? new List<string>();

        var matches = _entries
            .Where(e => e.Date >= dateFrom && e.Date <= dateTo)
            .Where(e => prefixes.Count == 0
                        || prefixes.Any(p => e.DebitAccountNumber?.StartsWith(p) == true))
            .Where(e => department == null
                        || string.Equals(e.Department, department, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult<IList<LedgerItem>>(matches);
    }

    public Task<IList<CostStatistics>> GetPersonalCosts(
        DateTime dateFrom, DateTime dateTo, string? department = null,
        CancellationToken cancellationToken = default) =>
        GetCosts(dateFrom, dateTo, new[] { "52" }, department, cancellationToken);

    public Task<IList<CostStatistics>> GetDirectCosts(
        DateTime dateFrom, DateTime dateTo, string? department = null,
        CancellationToken cancellationToken = default) =>
        GetCosts(dateFrom, dateTo, new[] { "51", "52" }, department, cancellationToken);

    public async Task<IList<CostStatistics>> GetCosts(
        DateTime dateFrom,
        DateTime dateTo,
        IEnumerable<string> debitAccountPrefixes,
        string? department = null,
        CancellationToken cancellationToken = default)
    {
        var items = await GetLedgerItems(
            dateFrom, dateTo, debitAccountPrefixes, null, department, cancellationToken);

        var grouped = items
            .GroupBy(item => new { Date = item.Date.Date, item.Department })
            .Select(g => new CostStatistics
            {
                Date = g.Key.Date,
                Department = g.Key.Department,
                Cost = g.Sum(item => item.Amount)
            })
            .OrderBy(cs => cs.Date)
            .ToList();

        return grouped;
    }
}
