using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ListWebhookAudit;

public class ListWebhookAuditHandler
    : IRequestHandler<ListWebhookAuditRequest, ListWebhookAuditResponse>
{
    private const int MaxTake = 200;

    private readonly ISmartsuppWebhookAuditRepository _repository;

    public ListWebhookAuditHandler(ISmartsuppWebhookAuditRepository repository)
    {
        _repository = repository;
    }

    public async Task<ListWebhookAuditResponse> Handle(
        ListWebhookAuditRequest request,
        CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, MaxTake);

        var (entries, total) = await _repository.ListAsync(
            request.From,
            request.To,
            request.EventName,
            request.SignatureStatus,
            request.ProcessingStatus,
            skip,
            take,
            cancellationToken);

        var rows = entries.Select(e => new WebhookAuditSummaryDto
        {
            Id = e.Id,
            ReceivedAt = e.ReceivedAt,
            EventName = e.EventName,
            AccountId = e.AccountId,
            AppId = e.AppId,
            SignatureStatus = e.SignatureStatus,
            ProcessingStatus = e.ProcessingStatus,
            BodySizeBytes = e.BodySizeBytes,
            ProcessingDurationMs = e.ProcessingDurationMs,
            ReplayCount = e.ReplayCount,
            LastReplayedAt = e.LastReplayedAt,
            ProcessedAt = e.ProcessedAt,
        }).ToList();

        return new ListWebhookAuditResponse
        {
            Items = rows,
            Total = total,
            Skip = skip,
            PageSize = take,
        };
    }
}
