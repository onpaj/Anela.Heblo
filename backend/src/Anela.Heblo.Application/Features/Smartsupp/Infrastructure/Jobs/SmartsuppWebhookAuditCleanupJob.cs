using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.Infrastructure.Jobs;

public class SmartsuppWebhookAuditCleanupJob : IRecurringJob
{
    private const int RetentionDays = 7;

    // Presence rows expire on read within minutes (heartbeat/webhook TTLs). Anything older than a
    // day is certainly dead — purge it so the table never accumulates abandoned rows.
    private const int PresenceRetentionDays = 1;

    private readonly ApplicationDbContext _context;
    private readonly ISmartsuppPresenceRepository _presenceRepository;
    private readonly ILogger<SmartsuppWebhookAuditCleanupJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "smartsupp-webhook-audit-cleanup",
        DisplayName = "Smartsupp Webhook Audit Cleanup",
        Description = "Deletes Smartsupp webhook audit entries older than 7 days.",
        CronExpression = "30 3 * * *",
        DefaultIsEnabled = true,
    };

    public SmartsuppWebhookAuditCleanupJob(
        ApplicationDbContext context,
        ISmartsuppPresenceRepository presenceRepository,
        ILogger<SmartsuppWebhookAuditCleanupJob> logger)
    {
        _context = context;
        _presenceRepository = presenceRepository;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var presenceCutoff = DateTime.SpecifyKind(
            DateTime.UtcNow.AddDays(-PresenceRetentionDays), DateTimeKind.Unspecified);
        var deletedPresence = await _presenceRepository.PurgeExpiredAsync(
            presenceCutoff, presenceCutoff, cancellationToken);
        if (deletedPresence > 0)
            _logger.LogInformation("smartsupp presence cleanup: deleted {Count} stale rows", deletedPresence);

        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

        var stale = await _context.SmartsuppWebhookAuditEntries
            .Where(e => e.ReceivedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            _logger.LogInformation("smartsupp webhook audit cleanup: nothing to delete");
            return;
        }

        _context.SmartsuppWebhookAuditEntries.RemoveRange(stale);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("smartsupp webhook audit cleanup: deleted {Count} entries older than {Cutoff:o}",
            stale.Count, cutoff);
    }
}
