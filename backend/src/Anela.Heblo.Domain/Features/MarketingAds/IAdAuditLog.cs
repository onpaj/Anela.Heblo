namespace Anela.Heblo.Domain.Features.MarketingAds;

public sealed class AdAuditQuery
{
    public int? ProposalId { get; init; }
    public AdAuditEventType? EventType { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public interface IAdAuditLog
{
    void Append(AdAuditEvent auditEvent);
    Task<(IReadOnlyList<AdAuditEvent> Items, int TotalCount)> ListAsync(AdAuditQuery query, CancellationToken cancellationToken);
}
