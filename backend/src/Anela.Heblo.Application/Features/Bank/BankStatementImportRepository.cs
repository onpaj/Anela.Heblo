using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Bank;

public class BankStatementImportRepository : IBankStatementImportRepository
{
    private readonly ApplicationDbContext _context;

    public BankStatementImportRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<BankStatementImport>> GetAllAsync()
    {
        return await _context.BankStatements.ToListAsync();
    }

    public async Task<BankStatementImport?> GetByIdAsync(int id)
    {
        return await _context.BankStatements.FindAsync(id);
    }

    public async Task<IEnumerable<BankStatementImportStatistics>> GetImportStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _context.BankStatements.AsQueryable();

        if (startDate.HasValue)
        {
            query = query.Where(bs => bs.ImportDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(bs => bs.ImportDate <= endDate.Value);
        }

        var statistics = await query
            .GroupBy(bs => bs.ImportDate.Date)
            .Select(g => new BankStatementImportStatistics
            {
                Date = g.Key,
                ImportCount = g.Count(),
                TotalItemCount = g.Sum(bs => bs.ItemCount)
            })
            .OrderBy(stat => stat.Date)
            .ToListAsync();

        return statistics;
    }

    public async Task<BankStatementImport> AddAsync(BankStatementImport bankStatement)
    {
        _context.BankStatements.Add(bankStatement);
        await _context.SaveChangesAsync();
        return bankStatement;
    }

    public async Task UpdateAsync(BankStatementImport bankStatement)
    {
        _context.BankStatements.Update(bankStatement);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var bankStatement = await _context.BankStatements.FindAsync(id);
        if (bankStatement != null)
        {
            _context.BankStatements.Remove(bankStatement);
            await _context.SaveChangesAsync();
        }
    }
}