using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ReplayWebhookEvent;

public class ReplayWebhookEventHandler
    : IRequestHandler<ReplayWebhookEventRequest, ReplayWebhookEventResponse>
{
    private readonly ApplicationDbContext _context;
    private readonly IMediator _mediator;

    public ReplayWebhookEventHandler(ApplicationDbContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator;
    }

    public async Task<ReplayWebhookEventResponse> Handle(
        ReplayWebhookEventRequest request,
        CancellationToken cancellationToken)
    {
        var entry = await _context.SmartsuppWebhookAuditEntries
            .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

        if (entry is null)
            return new ReplayWebhookEventResponse(ErrorCodes.ResourceNotFound);

        JsonElement data;
        try
        {
            using var doc = JsonDocument.Parse(entry.RawBody);
            data = doc.RootElement.TryGetProperty("data", out var d) ? d.Clone() : default;
        }
        catch (JsonException)
        {
            return new ReplayWebhookEventResponse(ErrorCodes.InvalidOperation);
        }

        var timestamp = entry.EventTimestamp ?? DateTime.UtcNow;

        await _mediator.Send(new ProcessWebhookEventRequest
        {
            EventName = entry.EventName ?? "",
            Timestamp = timestamp,
            AccountId = entry.AccountId ?? "",
            AppId = entry.AppId ?? "",
            Data = data,
        }, cancellationToken);

        entry.ReplayCount += 1;
        entry.LastReplayedAt = DateTime.UtcNow;
        entry.LastReplayedBy = request.ReplayedBy;
        await _context.SaveChangesAsync(cancellationToken);

        return new ReplayWebhookEventResponse
        {
            ReplayCount = entry.ReplayCount,
            LastReplayedAt = entry.LastReplayedAt,
        };
    }
}
