using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Smartsupp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.WebhookAudit;

public class SmartsuppWebhookAuditWriterTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"audit_{Guid.NewGuid()}").Options);

    [Fact]
    public async Task CreateAsync_PersistsEntry_WithGeneratedId()
    {
        using var ctx = CreateContext();
        var writer = new SmartsuppWebhookAuditWriter(ctx);

        var entry = new SmartsuppWebhookAuditEntry
        {
            ReceivedAt = DateTime.UtcNow,
            RemoteIp = "1.2.3.4",
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            RawBody = "{}",
            BodySizeBytes = 2,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.NotProcessed,
        };

        var id = await writer.CreateAsync(entry, default);

        id.Should().NotBeEmpty();
        var fromDb = await ctx.SmartsuppWebhookAuditEntries.SingleAsync();
        fromDb.Id.Should().Be(id);
        fromDb.RawBody.Should().Be("{}");
    }

    [Fact]
    public async Task UpdateOutcomeAsync_SetsProcessingStatusAndDuration()
    {
        using var ctx = CreateContext();
        var writer = new SmartsuppWebhookAuditWriter(ctx);

        var id = await writer.CreateAsync(new SmartsuppWebhookAuditEntry
        {
            ReceivedAt = DateTime.UtcNow,
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            RawBody = "{}",
            ProcessingStatus = SmartsuppWebhookProcessingStatus.NotProcessed,
        }, default);

        await writer.UpdateOutcomeAsync(
            id, SmartsuppWebhookProcessingStatus.Success, error: null, durationMs: 42, default);

        var fromDb = await ctx.SmartsuppWebhookAuditEntries.SingleAsync();
        fromDb.ProcessingStatus.Should().Be(SmartsuppWebhookProcessingStatus.Success);
        fromDb.ProcessingDurationMs.Should().Be(42);
        fromDb.ProcessedAt.Should().NotBeNull();
    }
}
