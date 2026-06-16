using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppWebhookAuditWriter : ISmartsuppWebhookAuditWriter
{
    private const int RemoteIpMaxLength = 64;
    private const int SignatureHeaderMaxLength = 256;
    private const int EventNameMaxLength = 100;
    private const int AccountIdMaxLength = 100;
    private const int AppIdMaxLength = 100;
    private const int LastReplayedByMaxLength = 200;

    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly ILogger<SmartsuppWebhookAuditWriter> _logger;

    public SmartsuppWebhookAuditWriter(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        ILogger<SmartsuppWebhookAuditWriter> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<Guid> CreateAsync(SmartsuppWebhookAuditEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Id == Guid.Empty)
            entry.Id = Guid.NewGuid();

        TruncateBoundedFields(entry);

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        ctx.SmartsuppWebhookAuditEntries.Add(entry);
        await ctx.SaveChangesAsync(cancellationToken);
        return entry.Id;
    }

    public async Task UpdateOutcomeAsync(
        Guid id,
        SmartsuppWebhookProcessingStatus status,
        string? error,
        int durationMs,
        CancellationToken cancellationToken)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entry = await ctx.SmartsuppWebhookAuditEntries
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entry is null) return;

        entry.ProcessingStatus = status;
        entry.ProcessingError = error;
        entry.ProcessingDurationMs = durationMs;
        entry.ProcessedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(cancellationToken);
    }

    private void TruncateBoundedFields(SmartsuppWebhookAuditEntry entry)
    {
        entry.RemoteIp = TruncateField(entry.RemoteIp, RemoteIpMaxLength, "audit.remote_ip", entry.Id);
        entry.SignatureHeader = TruncateField(entry.SignatureHeader, SignatureHeaderMaxLength, "audit.signature_header", entry.Id);
        entry.EventName = TruncateField(entry.EventName, EventNameMaxLength, "audit.event_name", entry.Id);
        entry.AccountId = TruncateField(entry.AccountId, AccountIdMaxLength, "audit.account_id", entry.Id);
        entry.AppId = TruncateField(entry.AppId, AppIdMaxLength, "audit.app_id", entry.Id);
        entry.LastReplayedBy = TruncateField(entry.LastReplayedBy, LastReplayedByMaxLength, "audit.last_replayed_by", entry.Id);
    }

    private string? TruncateField(string? value, int max, string field, Guid auditId)
    {
        if (value is null || value.Length <= max) return value;
        var cut = max;
        if (cut > 0 && char.IsHighSurrogate(value[cut - 1])) cut--;
        var truncated = value[..cut];
        _logger.LogWarning(
            "smartsupp audit field {Field} truncated original={OriginalLength} truncated={TruncatedLength} auditId={AuditId}",
            field, value.Length, truncated.Length, auditId);
        return truncated;
    }
}
