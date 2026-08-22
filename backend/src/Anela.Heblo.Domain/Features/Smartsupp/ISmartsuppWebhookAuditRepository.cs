namespace Anela.Heblo.Domain.Features.Smartsupp;

public interface ISmartsuppWebhookAuditRepository
{
    Task<Guid> CreateAsync(SmartsuppWebhookAuditEntry entry, CancellationToken cancellationToken);

    Task UpdateOutcomeAsync(
        Guid id,
        SmartsuppWebhookProcessingStatus status,
        string? error,
        int durationMs,
        CancellationToken cancellationToken);

    Task<(List<SmartsuppWebhookAuditEntry> Items, int Total)> ListAsync(
        DateTime? from,
        DateTime? to,
        string? eventName,
        SmartsuppWebhookSignatureStatus? signatureStatus,
        SmartsuppWebhookProcessingStatus? processingStatus,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<SmartsuppWebhookAuditEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Tracked read for the replay flow — caller mutates ReplayCount/LastReplayedAt/LastReplayedBy
    /// on the returned entity, then calls SaveChangesAsync.
    /// </summary>
    Task<SmartsuppWebhookAuditEntry?> GetForReplayAsync(Guid id, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Deletes entries with ReceivedAt older than <paramref name="cutoff"/>; returns the count deleted.</summary>
    Task<int> PurgeOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken);
}
