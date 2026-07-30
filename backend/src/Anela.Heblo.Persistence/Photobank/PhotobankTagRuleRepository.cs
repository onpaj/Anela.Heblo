using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Photobank;

public class PhotobankTagRuleRepository : IPhotobankTagRuleRepository
{
    private readonly ApplicationDbContext _context;

    public PhotobankTagRuleRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<TagRule>> GetRulesAsync(CancellationToken cancellationToken)
    {
        return await _context.PhotobankTagRules
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TagRule>> GetActiveTagRulesAsync(CancellationToken cancellationToken)
    {
        return await _context.PhotobankTagRules
            .Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public Task<TagRule> AddRuleAsync(TagRule rule, CancellationToken cancellationToken)
    {
        _context.PhotobankTagRules.Add(rule);
        return Task.FromResult(rule);
    }

    public async Task<TagRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken)
    {
        return await _context.PhotobankTagRules.FindAsync(new object[] { id }, cancellationToken);
    }

    public Task UpdateRuleAsync(TagRule rule, CancellationToken cancellationToken)
    {
        _context.PhotobankTagRules.Update(rule);
        return Task.CompletedTask;
    }

    public async Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken)
    {
        var rule = await _context.PhotobankTagRules.FindAsync(new object[] { id }, cancellationToken);
        if (rule == null)
            return false;
        _context.PhotobankTagRules.Remove(rule);
        return true;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
