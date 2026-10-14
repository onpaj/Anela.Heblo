using Anela.Heblo.Domain.Features.MarketingAds;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.MarketingAds;

/// <summary>Append-only access to AdAuditEvents. Rows are saved by the caller's next SaveChanges on the shared context.</summary>
public class AdAuditLog : IAdAuditLog
{
    private const int MaxPageSize = 200;
    private readonly ApplicationDbContext _context;

    public AdAuditLog(ApplicationDbContext context) => _context = context;

    public void Append(AdAuditEvent auditEvent) => _context.AdAuditEvents.Add(auditEvent);

    public async Task<(IReadOnlyList<AdAuditEvent> Items, int TotalCount)> ListAsync(AdAuditQuery query, CancellationToken cancellationToken)
    {
        var events = _context.AdAuditEvents.AsNoTracking();
        if (query.ProposalId is { } proposalId) events = events.Where(e => e.ProposalId == proposalId);
        if (query.EventType is { } type) events = events.Where(e => e.EventType == type);
        var total = await events.CountAsync(cancellationToken);
        var size = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var items = await events.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Skip(AdPaging.Skip(query.Page, size)).Take(size).ToListAsync(cancellationToken);
        return (items, total);
    }
}
