using Microsoft.EntityFrameworkCore;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Persistence.InvoiceClassification;

public class ClassificationHistoryRepository : IClassificationHistoryRepository
{
    private readonly ApplicationDbContext _context;

    public ClassificationHistoryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ClassificationHistory> AddAsync(ClassificationHistory history)
    {
        _context.ClassificationHistory.Add(history);
        await _context.SaveChangesAsync();
        return history;
    }

    public async Task<List<ClassificationHistory>> GetHistoryAsync(int skip = 0, int take = 50)
    {
        return await _context.ClassificationHistory
            .Include(h => h.ClassificationRule)
            .OrderByDescending(h => h.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<List<ClassificationHistory>> GetHistoryByInvoiceIdAsync(string abraInvoiceId)
    {
        return await _context.ClassificationHistory
            .Include(h => h.ClassificationRule)
            .Where(h => h.AbraInvoiceId == abraInvoiceId)
            .OrderByDescending(h => h.Timestamp)
            .ToListAsync();
    }

    public async Task<ClassificationStatistics> GetStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.ClassificationHistory.AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(h => h.Timestamp >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(h => h.Timestamp <= toDate.Value);

        var totalProcessed = await query.CountAsync();
        var successCount = await query.CountAsync(h => h.Result == ClassificationResult.Success);
        var manualReviewCount = await query.CountAsync(h => h.Result == ClassificationResult.ManualReviewRequired);
        var errorCount = await query.CountAsync(h => h.Result == ClassificationResult.Error);

        var ruleUsage = await query
            .Where(h => h.ClassificationRuleId.HasValue && h.Result == ClassificationResult.Success)
            .Include(h => h.ClassificationRule)
            .GroupBy(h => new { h.ClassificationRuleId, h.ClassificationRule!.Name })
            .Select(g => new RuleUsageStatistic
            {
                RuleId = g.Key.ClassificationRuleId!.Value,
                RuleName = g.Key.Name,
                UsageCount = g.Count(),
                UsagePercentage = totalProcessed > 0 ? (decimal)g.Count() / totalProcessed * 100 : 0
            })
            .OrderByDescending(r => r.UsageCount)
            .ToListAsync();

        return new ClassificationStatistics
        {
            TotalInvoicesProcessed = totalProcessed,
            SuccessfulClassifications = successCount,
            ManualReviewRequired = manualReviewCount,
            Errors = errorCount,
            RuleUsage = ruleUsage
        };
    }
}