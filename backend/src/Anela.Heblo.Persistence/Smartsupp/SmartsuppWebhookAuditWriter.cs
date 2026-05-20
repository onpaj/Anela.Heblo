using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppWebhookAuditWriter : ISmartsuppWebhookAuditWriter
{
    private readonly ApplicationDbContext _context;

    public SmartsuppWebhookAuditWriter(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> CreateAsync(SmartsuppWebhookAuditEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Id == Guid.Empty)
            entry.Id = Guid.NewGuid();

        _context.SmartsuppWebhookAuditEntries.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);
        return entry.Id;
    }

    public async Task UpdateOutcomeAsync(
        Guid id,
        SmartsuppWebhookProcessingStatus status,
        string? error,
        int durationMs,
        CancellationToken cancellationToken)
    {
        var entry = await _context.SmartsuppWebhookAuditEntries
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entry is null) return;

        entry.ProcessingStatus = status;
        entry.ProcessingError = error;
        entry.ProcessingDurationMs = durationMs;
        entry.ProcessedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
