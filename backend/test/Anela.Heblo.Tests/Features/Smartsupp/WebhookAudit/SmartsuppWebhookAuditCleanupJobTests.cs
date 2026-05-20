using Anela.Heblo.Application.Features.Smartsupp.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.WebhookAudit;

public class SmartsuppWebhookAuditCleanupJobTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"audit_{Guid.NewGuid()}").Options);

    private static SmartsuppWebhookAuditEntry MakeEntry(DateTime receivedAt) => new()
    {
        Id = Guid.NewGuid(),
        ReceivedAt = receivedAt,
        RawBody = "{}",
        SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
        ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
    };

    [Fact]
    public async Task Execute_DeletesEntriesOlderThanSevenDays()
    {
        using var ctx = CreateContext();
        var now = DateTime.UtcNow;
        ctx.SmartsuppWebhookAuditEntries.AddRange(
            MakeEntry(now.AddDays(-1)),
            MakeEntry(now.AddDays(-6)),
            MakeEntry(now.AddDays(-8)));
        await ctx.SaveChangesAsync();

        var job = new SmartsuppWebhookAuditCleanupJob(ctx, NullLogger<SmartsuppWebhookAuditCleanupJob>.Instance);
        await job.ExecuteAsync(default);

        var ages = await ctx.SmartsuppWebhookAuditEntries
            .Select(e => (now - e.ReceivedAt).TotalDays)
            .ToListAsync();
        ages.Should().HaveCount(2);
        ages.Should().OnlyContain(d => d < 7);
    }

    [Fact]
    public void Metadata_HasExpectedJobNameAndCron()
    {
        using var ctx = CreateContext();
        var job = new SmartsuppWebhookAuditCleanupJob(ctx, NullLogger<SmartsuppWebhookAuditCleanupJob>.Instance);

        job.Metadata.JobName.Should().Be("smartsupp-webhook-audit-cleanup");
        job.Metadata.CronExpression.Should().NotBeNullOrWhiteSpace();
    }
}
