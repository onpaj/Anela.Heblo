using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Smartsupp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.WebhookAudit;

public class SmartsuppWebhookAuditWriterTests
{
    private static (IDbContextFactory<ApplicationDbContext> Factory, ServiceProvider Provider) CreateFactory()
    {
        var dbName = $"audit_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContextFactory<ApplicationDbContext>(opts =>
            opts.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(), sp);
    }

    [Fact]
    public async Task CreateAsync_PersistsEntry_WithGeneratedId()
    {
        var (factory, provider) = CreateFactory();
        await using var _ = provider;
        var writer = new SmartsuppWebhookAuditWriter(factory, NullLogger<SmartsuppWebhookAuditWriter>.Instance);

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
        await using var ctx = await factory.CreateDbContextAsync();
        var fromDb = await ctx.SmartsuppWebhookAuditEntries.SingleAsync();
        fromDb.Id.Should().Be(id);
        fromDb.RawBody.Should().Be("{}");
    }

    [Fact]
    public async Task UpdateOutcomeAsync_SetsProcessingStatusAndDuration()
    {
        var (factory, provider) = CreateFactory();
        await using var _ = provider;
        var writer = new SmartsuppWebhookAuditWriter(factory, NullLogger<SmartsuppWebhookAuditWriter>.Instance);

        var id = await writer.CreateAsync(new SmartsuppWebhookAuditEntry
        {
            ReceivedAt = DateTime.UtcNow,
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            RawBody = "{}",
            ProcessingStatus = SmartsuppWebhookProcessingStatus.NotProcessed,
        }, default);

        await writer.UpdateOutcomeAsync(
            id, SmartsuppWebhookProcessingStatus.Success, error: null, durationMs: 42, default);

        await using var ctx = await factory.CreateDbContextAsync();
        var fromDb = await ctx.SmartsuppWebhookAuditEntries.SingleAsync();
        fromDb.ProcessingStatus.Should().Be(SmartsuppWebhookProcessingStatus.Success);
        fromDb.ProcessingDurationMs.Should().Be(42);
        fromDb.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_TruncatesOverlongRemoteIp()
    {
        var (factory, provider) = CreateFactory();
        await using var _ = provider;
        var writer = new SmartsuppWebhookAuditWriter(factory, NullLogger<SmartsuppWebhookAuditWriter>.Instance);

        var entry = new SmartsuppWebhookAuditEntry
        {
            ReceivedAt = DateTime.UtcNow,
            RemoteIp = new string('x', 200),
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            RawBody = "{}",
            ProcessingStatus = SmartsuppWebhookProcessingStatus.NotProcessed,
        };

        await writer.CreateAsync(entry, default);

        await using var ctx = await factory.CreateDbContextAsync();
        var fromDb = await ctx.SmartsuppWebhookAuditEntries.SingleAsync();
        fromDb.RemoteIp.Length.Should().Be(64);
    }

    [Fact]
    public async Task CreateAsync_TruncatesOverlongEventName_AndAppId()
    {
        var (factory, provider) = CreateFactory();
        await using var _ = provider;
        var writer = new SmartsuppWebhookAuditWriter(factory, NullLogger<SmartsuppWebhookAuditWriter>.Instance);

        var entry = new SmartsuppWebhookAuditEntry
        {
            ReceivedAt = DateTime.UtcNow,
            EventName = new string('e', 150),
            AppId = new string('a', 200),
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            RawBody = "{}",
            ProcessingStatus = SmartsuppWebhookProcessingStatus.NotProcessed,
        };

        await writer.CreateAsync(entry, default);

        await using var ctx = await factory.CreateDbContextAsync();
        var fromDb = await ctx.SmartsuppWebhookAuditEntries.SingleAsync();
        fromDb.EventName!.Length.Should().Be(100);
        fromDb.AppId!.Length.Should().Be(100);
    }
}
