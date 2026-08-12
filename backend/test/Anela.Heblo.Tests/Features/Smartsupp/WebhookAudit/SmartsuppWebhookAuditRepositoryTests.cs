using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Smartsupp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.WebhookAudit;

public class SmartsuppWebhookAuditRepositoryTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"audit_{Guid.NewGuid()}").Options);

    [Fact]
    public async Task CreateAsync_PersistsEntry_WithGeneratedId()
    {
        using var ctx = CreateContext();
        var writer = new SmartsuppWebhookAuditRepository(ctx);

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
        var writer = new SmartsuppWebhookAuditRepository(ctx);

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

    [Fact]
    public async Task ListAsync_ReturnsRowsOrderedByReceivedAtDescending_WithTotal()
    {
        using var ctx = CreateContext();
        var repo = new SmartsuppWebhookAuditRepository(ctx);
        ctx.SmartsuppWebhookAuditEntries.AddRange(
            new SmartsuppWebhookAuditEntry
            {
                Id = Guid.NewGuid(),
                ReceivedAt = DateTime.UtcNow.AddMinutes(-2),
                EventName = "a",
                RawBody = "{}",
                SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
                ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
            },
            new SmartsuppWebhookAuditEntry
            {
                Id = Guid.NewGuid(),
                ReceivedAt = DateTime.UtcNow.AddMinutes(-1),
                EventName = "b",
                RawBody = "{}",
                SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
                ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
            });
        await ctx.SaveChangesAsync();

        var (items, total) = await repo.ListAsync(
            null, null, null, null, null, skip: 0, take: 50, default);

        items.Should().HaveCount(2);
        items[0].EventName.Should().Be("b");
        items[1].EventName.Should().Be("a");
        total.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_FiltersByEventNameAndProcessingStatus()
    {
        using var ctx = CreateContext();
        var repo = new SmartsuppWebhookAuditRepository(ctx);
        ctx.SmartsuppWebhookAuditEntries.AddRange(
            new SmartsuppWebhookAuditEntry
            {
                Id = Guid.NewGuid(),
                ReceivedAt = DateTime.UtcNow,
                EventName = "conv.opened",
                RawBody = "{}",
                SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
                ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
            },
            new SmartsuppWebhookAuditEntry
            {
                Id = Guid.NewGuid(),
                ReceivedAt = DateTime.UtcNow,
                EventName = "conv.opened",
                RawBody = "{}",
                SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
                ProcessingStatus = SmartsuppWebhookProcessingStatus.HandlerException,
            },
            new SmartsuppWebhookAuditEntry
            {
                Id = Guid.NewGuid(),
                ReceivedAt = DateTime.UtcNow,
                EventName = "conv.closed",
                RawBody = "{}",
                SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
                ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
            });
        await ctx.SaveChangesAsync();

        var (items, _) = await repo.ListAsync(
            null, null, "conv.opened", null, SmartsuppWebhookProcessingStatus.HandlerException,
            skip: 0, take: 50, default);

        items.Should().ContainSingle()
            .Which.ProcessingStatus.Should().Be(SmartsuppWebhookProcessingStatus.HandlerException);
    }

    [Fact]
    public async Task ListAsync_AppliesSkipAndTake()
    {
        using var ctx = CreateContext();
        var repo = new SmartsuppWebhookAuditRepository(ctx);
        for (var i = 0; i < 5; i++)
        {
            ctx.SmartsuppWebhookAuditEntries.Add(new SmartsuppWebhookAuditEntry
            {
                Id = Guid.NewGuid(),
                ReceivedAt = DateTime.UtcNow.AddSeconds(-i),
                EventName = $"e{i}",
                RawBody = "{}",
                SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
                ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
            });
        }
        await ctx.SaveChangesAsync();

        var (items, total) = await repo.ListAsync(
            null, null, null, null, null, skip: 1, take: 2, default);

        items.Should().HaveCount(2);
        total.Should().Be(5);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsEntry_WhenExists()
    {
        using var ctx = CreateContext();
        var repo = new SmartsuppWebhookAuditRepository(ctx);
        var id = Guid.NewGuid();
        ctx.SmartsuppWebhookAuditEntries.Add(new SmartsuppWebhookAuditEntry
        {
            Id = id,
            ReceivedAt = DateTime.UtcNow,
            RawBody = "{\"k\":1}",
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
        });
        await ctx.SaveChangesAsync();

        var entry = await repo.GetByIdAsync(id, default);

        entry.Should().NotBeNull();
        entry!.RawBody.Should().Be("{\"k\":1}");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
    {
        using var ctx = CreateContext();
        var repo = new SmartsuppWebhookAuditRepository(ctx);

        var entry = await repo.GetByIdAsync(Guid.NewGuid(), default);

        entry.Should().BeNull();
    }

    [Fact]
    public async Task GetForReplayAsync_ReturnsTrackedEntry_MutationsPersistOnSaveChanges()
    {
        using var ctx = CreateContext();
        var repo = new SmartsuppWebhookAuditRepository(ctx);
        var id = Guid.NewGuid();
        ctx.SmartsuppWebhookAuditEntries.Add(new SmartsuppWebhookAuditEntry
        {
            Id = id,
            ReceivedAt = DateTime.UtcNow,
            RawBody = "{}",
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
        });
        await ctx.SaveChangesAsync();

        var entry = await repo.GetForReplayAsync(id, default);
        entry.Should().NotBeNull();
        entry!.ReplayCount = 1;
        entry.LastReplayedAt = DateTime.UtcNow;
        entry.LastReplayedBy = "tester";
        await repo.SaveChangesAsync(default);

        var reloaded = await repo.GetByIdAsync(id, default);
        reloaded!.ReplayCount.Should().Be(1);
        reloaded.LastReplayedBy.Should().Be("tester");
    }

    [Fact]
    public async Task PurgeOlderThanAsync_DeletesOnlyEntriesOlderThanCutoff_AndReturnsCount()
    {
        using var ctx = CreateContext();
        var repo = new SmartsuppWebhookAuditRepository(ctx);
        var now = DateTime.UtcNow;
        ctx.SmartsuppWebhookAuditEntries.AddRange(
            new SmartsuppWebhookAuditEntry
            {
                Id = Guid.NewGuid(),
                ReceivedAt = now.AddDays(-1),
                RawBody = "{}",
                SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
                ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
            },
            new SmartsuppWebhookAuditEntry
            {
                Id = Guid.NewGuid(),
                ReceivedAt = now.AddDays(-8),
                RawBody = "{}",
                SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
                ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
            });
        await ctx.SaveChangesAsync();

        var deleted = await repo.PurgeOlderThanAsync(now.AddDays(-7), default);

        deleted.Should().Be(1);
        (await ctx.SmartsuppWebhookAuditEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PurgeOlderThanAsync_ReturnsZero_WhenNothingToDelete()
    {
        using var ctx = CreateContext();
        var repo = new SmartsuppWebhookAuditRepository(ctx);

        var deleted = await repo.PurgeOlderThanAsync(DateTime.UtcNow.AddDays(-7), default);

        deleted.Should().Be(0);
    }
}
