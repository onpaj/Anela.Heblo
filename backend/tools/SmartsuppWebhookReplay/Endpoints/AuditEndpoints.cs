using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using SmartsuppWebhookReplay.Models;

namespace SmartsuppWebhookReplay.Endpoints;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/api/audit", ListAuditEntries);
        app.MapGet("/api/audit/{id:guid}", GetAuditEntry);
    }

    private static async Task<IResult> ListAuditEntries(
        ApplicationDbContext db,
        string? @event,
        string? processingStatus,
        string? signatureStatus,
        DateTime? from,
        DateTime? to,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        take = Math.Min(take, 500);
        skip = Math.Max(skip, 0);

        var query = db.SmartsuppWebhookAuditEntries.AsNoTracking();

        if (!string.IsNullOrEmpty(@event))
            query = query.Where(e => e.EventName == @event);

        if (!string.IsNullOrEmpty(processingStatus)
            && Enum.TryParse<SmartsuppWebhookProcessingStatus>(processingStatus, out var procEnum))
            query = query.Where(e => e.ProcessingStatus == procEnum);

        if (!string.IsNullOrEmpty(signatureStatus)
            && Enum.TryParse<SmartsuppWebhookSignatureStatus>(signatureStatus, out var sigEnum))
            query = query.Where(e => e.SignatureStatus == sigEnum);

        if (from.HasValue)
            query = query.Where(e => e.ReceivedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.ReceivedAt <= to.Value);

        var items = await query
            .OrderBy(e => e.ReceivedAt)
            .Skip(skip)
            .Take(take)
            .Select(e => new AuditSummary
            {
                Id = e.Id,
                ReceivedAt = e.ReceivedAt,
                EventName = e.EventName,
                AccountId = e.AccountId,
                SignatureStatus = e.SignatureStatus.ToString(),
                ProcessingStatus = e.ProcessingStatus.ToString(),
                BodySizeBytes = e.BodySizeBytes,
                ProcessingDurationMs = e.ProcessingDurationMs,
            })
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> GetAuditEntry(
        Guid id,
        ApplicationDbContext db,
        CancellationToken ct)
    {
        var entry = await db.SmartsuppWebhookAuditEntries
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new AuditDetail
            {
                Id = e.Id,
                ReceivedAt = e.ReceivedAt,
                EventName = e.EventName,
                AccountId = e.AccountId,
                SignatureStatus = e.SignatureStatus.ToString(),
                SignatureHeader = e.SignatureHeader,
                ProcessingStatus = e.ProcessingStatus.ToString(),
                RawBody = e.RawBody,
                BodySizeBytes = e.BodySizeBytes,
                HeadersJson = e.HeadersJson,
                ProcessingDurationMs = e.ProcessingDurationMs,
                ProcessingError = e.ProcessingError,
            })
            .FirstOrDefaultAsync(ct);

        return entry is null ? Results.NotFound() : Results.Ok(entry);
    }
}
