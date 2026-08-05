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

    public async Task<int> GetMaxOrderAsync()
    {
        return await _context.ClassificationRules.MaxAsync(r => (int?)r.Order) ?? 0;
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

        var orderedRules = ruleIds
            .Select(id => rules.FirstOrDefault(r => r.Id == id))
            .Where(r => r != null)
            .Select(r => r!)
            .ToList();

        if (orderedRules.Count == 0)
        {
            return;
        }

        // The fixed set of Order values already legally owned by exactly these rows.
        // Redistributing this set (instead of renumbering to 1..N) guarantees every
        // write stays collision-free against rows outside ruleIds (e.g. inactive rows),
        // since Order is unique across the whole table, not just active rows.
        var valuesToRedistribute = orderedRules
            .Select(r => r.Order)
            .OrderBy(o => o)
            .ToList();

        // Phase 1: move to a disjoint negative range. Order is always >= 1 for real
        // rows, so negative values can never collide with an existing row, active or
        // inactive. This avoids the transient unique_violation a non-deferrable unique
        // index throws when a permutation is written row-by-row.
        for (int i = 0; i < orderedRules.Count; i++)
        {
            orderedRules[i].SetOrder(-(i + 1));
        }
        await _context.SaveChangesAsync();

        // Phase 2: assign the redistributed final values.
        for (int i = 0; i < orderedRules.Count; i++)
        {
            orderedRules[i].SetOrder(valuesToRedistribute[i]);
        }
        await _context.SaveChangesAsync();
    }
}