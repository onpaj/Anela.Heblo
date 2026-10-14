using Anela.Heblo.Domain.Features.Rag;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Rag;

public class RagInteractionLogRepository : IRagInteractionLogRepository
{
    private readonly ApplicationDbContext _context;

    public RagInteractionLogRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task SaveAsync(RagInteractionLog log, CancellationToken ct = default)
    {
        _context.RagInteractionLogs.Add(log);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<RagInteractionLog?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.RagInteractionLogs.FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateSentAsync(
        Guid id,
        string sentAnswer,
        DateTimeOffset sentAt,
        CancellationToken ct = default)
    {
        var log = await _context.RagInteractionLogs.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (log is null)
            return;

        log.SentAnswer = sentAnswer;
        log.WasEdited = !string.Equals(sentAnswer, log.Answer, StringComparison.Ordinal);
        log.SentAt = sentAt;

        await _context.SaveChangesAsync(ct);
    }

    public async Task<(List<RagInteractionLog> Logs, int TotalCount)> GetFeedbackLogsPagedAsync(
        RagFeedbackLogFilter filter,
        string sortBy,
        bool sortDescending,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = ApplyFilter(_context.RagInteractionLogs.AsQueryable(), filter);

        query = sortBy switch
        {
            "PrecisionScore" => sortDescending
                ? query.OrderByDescending(l => l.PrecisionScore)
                : query.OrderBy(l => l.PrecisionScore),
            "StyleScore" => sortDescending
                ? query.OrderByDescending(l => l.StyleScore)
                : query.OrderBy(l => l.StyleScore),
            _ => sortDescending
                ? query.OrderByDescending(l => l.CreatedAt)
                : query.OrderBy(l => l.CreatedAt),
        };

        var totalCount = await query.CountAsync(ct);
        var logs = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (logs, totalCount);
    }

    public async Task<IReadOnlyList<RagTagCount>> GetTagCountsAsync(
        RagFeature feature,
        DateTimeOffset? from,
        DateTimeOffset? toExclusive,
        CancellationToken ct = default)
    {
        var filter = new RagFeedbackLogFilter(feature, CreatedFrom: from, CreatedToExclusive: toExclusive);

        // Projects only the three arrays and groups in memory: KB volume is ~120 questions/month, so this
        // stays small, and it avoids depending on EF translating SelectMany+GroupBy over primitive collections.
        var rows = await ApplyFilter(_context.RagInteractionLogs.AsNoTracking(), filter)
            .Select(l => new { l.ProblemTags, l.ProductTags, l.RecommendedProductCodes })
            .ToListAsync(ct);

        return Count(RagTagKind.Problem, rows.SelectMany(r => r.ProblemTags))
            .Concat(Count(RagTagKind.Product, rows.SelectMany(r => r.ProductTags)))
            .Concat(Count(RagTagKind.RecommendedProduct, rows.SelectMany(r => r.RecommendedProductCodes)))
            .ToList();
    }

    private static IEnumerable<RagTagCount> Count(RagTagKind kind, IEnumerable<string> tags) =>
        tags.GroupBy(t => t, StringComparer.Ordinal)
            .Select(g => new RagTagCount(kind, g.Key, g.Count()));

    private static IQueryable<RagInteractionLog> ApplyFilter(IQueryable<RagInteractionLog> query, RagFeedbackLogFilter filter)
    {
        if (filter.Feature.HasValue)
            query = query.Where(l => l.Feature == filter.Feature.Value);

        if (filter.HasFeedback.HasValue)
        {
            query = filter.HasFeedback.Value
                ? query.Where(l => l.PrecisionScore != null || l.StyleScore != null)
                : query.Where(l => l.PrecisionScore == null && l.StyleScore == null);
        }

        if (!string.IsNullOrEmpty(filter.UserId))
            query = query.Where(l => l.UserId == filter.UserId);

        if (filter.CreatedFrom.HasValue)
            query = query.Where(l => l.CreatedAt >= filter.CreatedFrom.Value);

        if (filter.CreatedToExclusive.HasValue)
            query = query.Where(l => l.CreatedAt < filter.CreatedToExclusive.Value);

        if (!string.IsNullOrEmpty(filter.ProblemTag))
            query = query.Where(l => l.ProblemTags.Contains(filter.ProblemTag));

        if (!string.IsNullOrEmpty(filter.ProductTag))
            query = query.Where(l => l.ProductTags.Contains(filter.ProductTag));

        return query;
    }

    public async Task<RagFeedbackAggregateStats> GetFeedbackStatsAsync(RagFeature? feature, CancellationToken ct = default)
    {
        var query = _context.RagInteractionLogs.AsQueryable();

        if (feature.HasValue)
            query = query.Where(l => l.Feature == feature.Value);

        var row = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalQuestions = g.Count(),
                TotalWithFeedback = g.Count(l => l.PrecisionScore != null || l.StyleScore != null),
                AvgPrecision = g.Average(l => (double?)l.PrecisionScore),
                AvgStyle = g.Average(l => (double?)l.StyleScore),
            })
            .FirstOrDefaultAsync(ct);

        return new RagFeedbackAggregateStats
        {
            TotalQuestions = row?.TotalQuestions ?? 0,
            TotalWithFeedback = row?.TotalWithFeedback ?? 0,
            AvgPrecisionScore = row?.AvgPrecision.HasValue == true ? Math.Round(row.AvgPrecision.Value, 1) : null,
            AvgStyleScore = row?.AvgStyle.HasValue == true ? Math.Round(row.AvgStyle.Value, 1) : null,
        };
    }
}
