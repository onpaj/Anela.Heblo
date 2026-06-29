using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Bank;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.Bank;

public class BankStatementImportRepository : IBankStatementImportRepository
{
    private readonly ApplicationDbContext _context;

    public BankStatementImportRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<(IEnumerable<BankStatementImport> Items, int TotalCount)> GetFilteredAsync(
        BankStatementListFilter filter,
        int skip = 0,
        int take = 50,
        string orderBy = "ImportDate",
        bool ascending = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _context.BankStatements.AsNoTracking().AsQueryable();

        if (filter.Id.HasValue)
            query = query.Where(bs => bs.Id == filter.Id.Value);

        if (!string.IsNullOrWhiteSpace(filter.TransferId))
        {
            var pattern = $"%{EscapeLike(filter.TransferId.Trim())}%";
            query = query.Where(bs => EF.Functions.ILike(bs.TransferId, pattern, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(filter.Account))
        {
            var pattern = $"%{EscapeLike(filter.Account.Trim())}%";
            query = query.Where(bs => EF.Functions.ILike(bs.Account, pattern, "\\"));
        }

        if (filter.StatementDate.HasValue)
            query = query.Where(bs => bs.StatementDate.Date == filter.StatementDate.Value.Date);

        if (filter.ImportDate.HasValue)
            query = query.Where(bs => bs.ImportDate.Date == filter.ImportDate.Value.Date);

        if (filter.ErrorsOnly == true)
            query = query.Where(bs => bs.ImportResult != ImportStatus.Success);

        if (filter.DateFrom.HasValue)
            query = query.Where(bs => bs.StatementDate.Date >= filter.DateFrom.Value.Date);

        if (filter.DateTo.HasValue)
            query = query.Where(bs => bs.StatementDate.Date <= filter.DateTo.Value.Date);

        var totalCount = await query.CountAsync(cancellationToken);

        query = orderBy.ToLowerInvariant() switch
        {
            "id" => ascending ? query.OrderBy(x => x.Id) : query.OrderByDescending(x => x.Id),
            "statementdate" => ascending
                ? query.OrderBy(x => x.StatementDate).ThenBy(x => x.Id)
                : query.OrderByDescending(x => x.StatementDate).ThenBy(x => x.Id),
            "importdate" => ascending
                ? query.OrderBy(x => x.ImportDate).ThenBy(x => x.Id)
                : query.OrderByDescending(x => x.ImportDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.ImportDate).ThenBy(x => x.Id)
        };

        var items = await query
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<BankStatementImport?> GetByIdAsync(int id)
    {
        return await _context.BankStatements.FindAsync(id);
    }

    public async Task<BankStatementImport> AddAsync(BankStatementImport bankStatement)
    {
        var entry = _context.BankStatements.Add(bankStatement);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // A failed INSERT leaves the entity tracked as Added; detach it so it cannot
            // be re-attempted by a later SaveChanges on the shared scoped DbContext.
            entry.State = EntityState.Detached;
            throw;
        }
        return bankStatement;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetExistingResultsByTransferIdsAsync(
        IReadOnlyCollection<string> transferIds, CancellationToken cancellationToken = default)
    {
        if (transferIds.Count == 0)
            return new Dictionary<string, string>();

        // Dedup against the GLOBAL unique constraint (IX_BankStatements_TransferId), not by
        // account/date, so a statement already stored under a different window is still detected.
        return await _context.BankStatements
            .AsNoTracking()
            .Where(bs => transferIds.Contains(bs.TransferId))
            .Select(bs => new { bs.TransferId, bs.ImportResult })
            .ToDictionaryAsync(x => x.TransferId, x => x.ImportResult, cancellationToken);
    }

    public async Task<DateTime?> GetMaxStatementDateAsync(string account, CancellationToken cancellationToken = default)
        => await _context.BankStatements
            .AsNoTracking()
            .Where(bs => bs.Account == account)
            .Select(bs => (DateTime?)bs.StatementDate)
            .MaxAsync(cancellationToken);

    public async Task<BankStatementImport?> GetByTransferIdAsync(
        string transferId, CancellationToken cancellationToken = default)
        => await _context.BankStatements.FirstOrDefaultAsync(bs => bs.TransferId == transferId, cancellationToken);

    public async Task<BankStatementImport> UpdateAsync(BankStatementImport bankStatement)
    {
        var entry = _context.BankStatements.Update(bankStatement);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            entry.State = EntityState.Detached;
            throw;
        }
        return bankStatement;
    }

    public async Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        BankStatementDateType dateType,
        CancellationToken cancellationToken = default)
    {
        var startUnspecified = DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified);
        var endUnspecified = DateTime.SpecifyKind(endDate, DateTimeKind.Unspecified);

        var rawResults = dateType switch
        {
            BankStatementDateType.StatementDate => await _context.BankStatements
                .AsNoTracking()
                .Where(b => b.StatementDate >= startUnspecified && b.StatementDate <= endUnspecified)
                .GroupBy(b => new { b.StatementDate.Year, b.StatementDate.Month, b.StatementDate.Day })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    g.Key.Day,
                    ImportCount = g.Count(),
                    TotalItemCount = g.Sum(b => b.ItemCount)
                })
                .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                .ToListAsync(cancellationToken),

            BankStatementDateType.ImportDate => await _context.BankStatements
                .AsNoTracking()
                .Where(b => b.ImportDate >= startUnspecified && b.ImportDate <= endUnspecified)
                .GroupBy(b => new { b.ImportDate.Year, b.ImportDate.Month, b.ImportDate.Day })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    g.Key.Day,
                    ImportCount = g.Count(),
                    TotalItemCount = g.Sum(b => b.ItemCount)
                })
                .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                .ToListAsync(cancellationToken),

            _ => throw new ArgumentOutOfRangeException(nameof(dateType), dateType, null)
        };

        return rawResults
            .Select(r => new DailyBankStatementStatistics
            {
                Date = DateTime.SpecifyKind(new DateTime(r.Year, r.Month, r.Day), DateTimeKind.Utc),
                ImportCount = r.ImportCount,
                TotalItemCount = r.TotalItemCount
            })
            .ToList();
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}