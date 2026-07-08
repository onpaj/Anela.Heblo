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
        RagFeature? feature,
        bool? hasFeedback,
        string? userId,
        string sortBy,
        bool sortDescending,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _context.RagInteractionLogs.AsQueryable();

        if (feature.HasValue)
            query = query.Where(l => l.Feature == feature.Value);

        if (hasFeedback.HasValue)
        {
            query = hasFeedback.Value
                ? query.Where(l => l.PrecisionScore != null || l.StyleScore != null)
                : query.Where(l => l.PrecisionScore == null && l.StyleScore == null);
        }

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(l => l.UserId == userId);

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
