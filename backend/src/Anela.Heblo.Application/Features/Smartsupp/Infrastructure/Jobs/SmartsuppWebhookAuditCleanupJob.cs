using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.Infrastructure.Jobs;

public class SmartsuppWebhookAuditCleanupJob : IRecurringJob
{
    private const int RetentionDays = 7;

    private readonly ApplicationDbContext _context;
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
        ILogger<SmartsuppWebhookAuditCleanupJob> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
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
