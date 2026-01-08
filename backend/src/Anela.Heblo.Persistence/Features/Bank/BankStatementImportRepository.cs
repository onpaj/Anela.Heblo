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
        int? id = null,
        DateTime? statementDate = null,
        DateTime? importDate = null,
        int skip = 0,
        int take = 50,
        string orderBy = "ImportDate",
        bool ascending = false)
    {
        // Use AsNoTracking to improve performance and potentially avoid mapping issues
        var query = _context.BankStatements.AsNoTracking().AsQueryable();

        // Apply filters
        if (id.HasValue)
            query = query.Where(bs => bs.Id == id.Value);

        if (statementDate.HasValue)
            query = query.Where(bs => bs.StatementDate.Date == statementDate.Value.Date);

        if (importDate.HasValue)
            query = query.Where(bs => bs.ImportDate.Date == importDate.Value.Date);

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Apply sorting with secondary sort by Id (always ascending) for deterministic ordering
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

        // Apply pagination
        var items = await query
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<BankStatementImport?> GetByIdAsync(int id)
    {
        return await _context.BankStatements.FindAsync(id);
    }

    public async Task<BankStatementImport> AddAsync(BankStatementImport bankStatement)
    {
        _context.BankStatements.Add(bankStatement);
        await _context.SaveChangesAsync();
        return bankStatement;
    }
}