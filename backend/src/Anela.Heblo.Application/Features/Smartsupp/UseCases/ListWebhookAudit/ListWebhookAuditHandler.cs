using Anela.Heblo.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ListWebhookAudit;

public class ListWebhookAuditHandler
    : IRequestHandler<ListWebhookAuditRequest, ListWebhookAuditResponse>
{
    private const int MaxTake = 200;

    private readonly ApplicationDbContext _context;

    public ListWebhookAuditHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ListWebhookAuditResponse> Handle(
        ListWebhookAuditRequest request,
        CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, MaxTake);

        var query = _context.SmartsuppWebhookAuditEntries.AsQueryable();
        if (request.From.HasValue) query = query.Where(e => e.ReceivedAt >= request.From.Value);
        if (request.To.HasValue) query = query.Where(e => e.ReceivedAt <= request.To.Value);
        if (!string.IsNullOrWhiteSpace(request.EventName)) query = query.Where(e => e.EventName == request.EventName);
        if (request.SignatureStatus.HasValue) query = query.Where(e => e.SignatureStatus == request.SignatureStatus.Value);
        if (request.ProcessingStatus.HasValue) query = query.Where(e => e.ProcessingStatus == request.ProcessingStatus.Value);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(e => e.ReceivedAt)
            .Skip(skip)
            .Take(take)
            .Select(e => new WebhookAuditSummaryDto
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
            })
            .ToListAsync(cancellationToken);

        return new ListWebhookAuditResponse
        {
            Items = rows,
            Total = total,
            Skip = skip,
            PageSize = take,
        };
    }
}
