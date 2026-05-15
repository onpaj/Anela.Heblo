using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.Leaflet;

public class LeafletGenerationRepository : ILeafletGenerationRepository
{
    private readonly ApplicationDbContext _context;

    public LeafletGenerationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task SaveGenerationAsync(LeafletGeneration generation, CancellationToken cancellationToken)
    {
        _context.LeafletGenerations.Add(generation);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken cancellationToken)
        => await _context.LeafletGenerations.FindAsync([id], cancellationToken);

    public async Task<(IReadOnlyList<LeafletGeneration> Items, int TotalCount)> GetGenerationsPagedAsync(
        bool? hasFeedback, string? userId, string sortBy, bool descending,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _context.LeafletGenerations
            .AsNoTracking()
            .AsQueryable();

        if (hasFeedback == true)
            query = query.Where(g => g.PrecisionScore != null || g.StyleScore != null);
        else if (hasFeedback == false)
            query = query.Where(g => g.PrecisionScore == null && g.StyleScore == null);

        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(g => g.UserId == userId);

        query = (sortBy, descending) switch
        {
            ("PrecisionScore", true) => query.OrderByDescending(g => g.PrecisionScore),
            ("PrecisionScore", false) => query.OrderBy(g => g.PrecisionScore),
            ("StyleScore", true) => query.OrderByDescending(g => g.StyleScore),
            ("StyleScore", false) => query.OrderBy(g => g.StyleScore),
            (_, true) => query.OrderByDescending(g => g.CreatedAt),
            _ => query.OrderBy(g => g.CreatedAt),
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken cancellationToken)
    {
        var total = await _context.LeafletGenerations.CountAsync(cancellationToken);
        var withFeedback = await _context.LeafletGenerations
            .CountAsync(g => g.PrecisionScore != null || g.StyleScore != null, cancellationToken);
        var avgPrecision = await _context.LeafletGenerations
            .Where(g => g.PrecisionScore != null)
            .AverageAsync(g => (double?)g.PrecisionScore, cancellationToken);
        var avgStyle = await _context.LeafletGenerations
            .Where(g => g.StyleScore != null)
            .AverageAsync(g => (double?)g.StyleScore, cancellationToken);
        return new LeafletFeedbackStats(total, withFeedback, avgPrecision, avgStyle);
    }

    public async Task<UpdateFeedbackResult> UpdateFeedbackAsync(
        Guid generationId,
        int? precisionScore,
        int? styleScore,
        string? comment,
        CancellationToken cancellationToken)
    {
        var generation = await _context.LeafletGenerations.FindAsync([generationId], cancellationToken);
        if (generation is null)
            return UpdateFeedbackResult.NotFound;

        if (generation.PrecisionScore is not null || generation.StyleScore is not null)
            return UpdateFeedbackResult.AlreadySubmitted;

        generation.PrecisionScore = precisionScore;
        generation.StyleScore = styleScore;
        generation.FeedbackComment = comment;

        await _context.SaveChangesAsync(cancellationToken);
        return UpdateFeedbackResult.Updated;
    }
}
