using Anela.Heblo.Application.Shared;
using Anela.Heblo.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetWebhookAuditEntry;

public class GetWebhookAuditEntryHandler
    : IRequestHandler<GetWebhookAuditEntryRequest, GetWebhookAuditEntryResponse>
{
    private readonly ApplicationDbContext _context;

    public GetWebhookAuditEntryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetWebhookAuditEntryResponse> Handle(
        GetWebhookAuditEntryRequest request,
        CancellationToken cancellationToken)
    {
        var entry = await _context.SmartsuppWebhookAuditEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

        if (entry is null)
            return new GetWebhookAuditEntryResponse(ErrorCodes.ResourceNotFound);

        return new GetWebhookAuditEntryResponse
        {
            Entry = new WebhookAuditEntryDto
            {
                Id = entry.Id,
                ReceivedAt = entry.ReceivedAt,
                RemoteIp = entry.RemoteIp,
                SignatureHeader = entry.SignatureHeader,
                SignatureStatus = entry.SignatureStatus,
                HeadersJson = entry.HeadersJson,
                RawBody = entry.RawBody,
                BodySizeBytes = entry.BodySizeBytes,
                EventName = entry.EventName,
                AccountId = entry.AccountId,
                AppId = entry.AppId,
                EventTimestamp = entry.EventTimestamp,
                ProcessingStatus = entry.ProcessingStatus,
                ProcessingError = entry.ProcessingError,
                ProcessingDurationMs = entry.ProcessingDurationMs,
                ProcessedAt = entry.ProcessedAt,
                ReplayCount = entry.ReplayCount,
                LastReplayedAt = entry.LastReplayedAt,
                LastReplayedBy = entry.LastReplayedBy,
            },
        };
    }
}
