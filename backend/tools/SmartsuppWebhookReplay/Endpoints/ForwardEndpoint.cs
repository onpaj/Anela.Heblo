using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using SmartsuppWebhookReplay.Services;

namespace SmartsuppWebhookReplay.Endpoints;

public static class ForwardEndpoint
{
    public static void MapForwardEndpoint(this WebApplication app)
    {
        app.MapPost("/api/audit/{id:guid}/forward", ForwardEntry);
    }

    private static async Task<IResult> ForwardEntry(
        Guid id,
        ApplicationDbContext db,
        WebhookForwarder forwarder,
        CancellationToken ct)
    {
        var entry = await db.SmartsuppWebhookAuditEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (entry is null)
            return Results.NotFound();

        var result = await forwarder.ForwardAsync(entry, ct);
        return Results.Ok(result);
    }
}
