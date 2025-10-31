using Microsoft.EntityFrameworkCore;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Persistence.InvoiceClassification;

public class ClassificationRuleRepository : IClassificationRuleRepository
{
    private readonly ApplicationDbContext _context;

    public ClassificationRuleRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ClassificationRule>> GetAllAsync()
    {
        return await _context.ClassificationRules
            .OrderBy(r => r.Order)
            .ToListAsync();
    }

    public async Task<List<ClassificationRule>> GetActiveRulesOrderedAsync()
    {
        return await _context.ClassificationRules
            .Where(r => r.IsActive)
            .OrderBy(r => r.Order)
            .ToListAsync();
    }

    public async Task<ClassificationRule?> GetByIdAsync(Guid id)
    {
        return await _context.ClassificationRules
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<ClassificationRule> AddAsync(ClassificationRule rule)
    {
        _context.ClassificationRules.Add(rule);
        await _context.SaveChangesAsync();
        return rule;
    }

    public async Task<ClassificationRule> UpdateAsync(ClassificationRule rule)
    {
        _context.ClassificationRules.Update(rule);
        await _context.SaveChangesAsync();
        return rule;
    }

    public async Task DeleteAsync(Guid id)
    {
        var rule = await GetByIdAsync(id);
        if (rule != null)
        {
            _context.ClassificationRules.Remove(rule);
            await _context.SaveChangesAsync();
        }
    }

    public async Task ReorderRulesAsync(List<Guid> ruleIds)
    {
        var rules = await _context.ClassificationRules
            .Where(r => ruleIds.Contains(r.Id))
            .ToListAsync();

        for (int i = 0; i < ruleIds.Count; i++)
        {
            var rule = rules.FirstOrDefault(r => r.Id == ruleIds[i]);
            if (rule != null)
            {
                rule.SetOrder(i + 1);
            }
        }

        await _context.SaveChangesAsync();
    }
}