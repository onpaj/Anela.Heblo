using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Persistence.Smartsupp;

public interface ISmartsuppWebhookAuditWriter
{
    Task<Guid> CreateAsync(SmartsuppWebhookAuditEntry entry, CancellationToken cancellationToken);

    Task UpdateOutcomeAsync(
        Guid id,
        SmartsuppWebhookProcessingStatus status,
        string? error,
        int durationMs,
        CancellationToken cancellationToken);
}
