using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppWebhookAuditRepository : ISmartsuppWebhookAuditRepository
{
    private readonly ApplicationDbContext _db;

    public SmartsuppWebhookAuditRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> CreateAsync(SmartsuppWebhookAuditEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Id == Guid.Empty)
            entry.Id = Guid.NewGuid();

        _db.SmartsuppWebhookAuditEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return entry.Id;
    }

    public async Task UpdateOutcomeAsync(
        Guid id,
        SmartsuppWebhookProcessingStatus status,
        string? error,
        int durationMs,
        CancellationToken cancellationToken)
    {
        var entry = await _db.SmartsuppWebhookAuditEntries
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entry is null) return;

        entry.ProcessingStatus = status;
        entry.ProcessingError = error;
        entry.ProcessingDurationMs = durationMs;
        entry.ProcessedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(List<SmartsuppWebhookAuditEntry> Items, int Total)> ListAsync(
        DateTime? from,
        DateTime? to,
        string? eventName,
        SmartsuppWebhookSignatureStatus? signatureStatus,
        SmartsuppWebhookProcessingStatus? processingStatus,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.SmartsuppWebhookAuditEntries.AsNoTracking().AsQueryable();
        if (from.HasValue) query = query.Where(e => e.ReceivedAt >= from.Value);
        if (to.HasValue) query = query.Where(e => e.ReceivedAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(eventName)) query = query.Where(e => e.EventName == eventName);
        if (signatureStatus.HasValue) query = query.Where(e => e.SignatureStatus == signatureStatus.Value);
        if (processingStatus.HasValue) query = query.Where(e => e.ProcessingStatus == processingStatus.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(e => e.ReceivedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<SmartsuppWebhookAuditEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.SmartsuppWebhookAuditEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<SmartsuppWebhookAuditEntry?> GetForReplayAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.SmartsuppWebhookAuditEntries
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await _db.SaveChangesAsync(cancellationToken);

    public async Task<int> PurgeOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken)
    {
        var stale = await _db.SmartsuppWebhookAuditEntries
            .Where(e => e.ReceivedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
            return 0;

        _db.SmartsuppWebhookAuditEntries.RemoveRange(stale);
        await _db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }
}
