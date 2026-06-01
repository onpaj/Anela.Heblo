using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class LedgerSyncServiceTests
{
    private static AnalyticsDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IOptions<FlexiAnalyticsSyncOptions> DefaultOptions() =>
        Options.Create(new FlexiAnalyticsSyncOptions
        {
            BatchSize = 10,
            InitialBackfillFrom = "2024-01-01"
        });

    private static LedgerSyncService CreateService(
        ILedgerClient ledgerClient,
        AnalyticsDbContext ctx,
        IOptions<FlexiAnalyticsSyncOptions>? opts = null)
    {
        var repo = new SyncWatermarkRepository(ctx);
        return new LedgerSyncService(
            ledgerClient,
            repo,
            ctx,
            opts ?? DefaultOptions(),
            Mock.Of<ILogger<LedgerSyncService>>());
    }

    // SDK 0.1.136: LedgerItemFlexiDto has LastUpdate as DateTime? but NO PeriodRef/DocumentTypeRef/ContactRef/AccountingTemplateRef
    private static LedgerItemFlexiDto MakeLedgerDto(int id, DateTime accountingDate, double amount = 100.0) =>
        new()
        {
            Id = id,
            AccountingDate = accountingDate,
            LastUpdate = accountingDate.AddHours(1),
            AmountLocal = amount,
            ParSymbol = $"CODE{id}",
            DebitAccountShowAs = "501000",
            CreditAccountShowAs = "221000",
            CurrencyRef = "code:CZK",
            Description = "Test entry",
        };

    [Fact]
    public async Task SyncAsync_WhenNoWatermark_FetchesFromInitialBackfillDate()
    {
        // Arrange
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();
        var opts = Options.Create(new FlexiAnalyticsSyncOptions
        {
            BatchSize = 10,
            InitialBackfillFrom = "2024-01-01"
        });

        // First call returns one item, second returns empty (end of pagination)
        client.SetupSequence(c => c.GetChangedSinceAsync(
                It.IsAny<DateTime>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItemFlexiDto> { MakeLedgerDto(1, new DateTime(2024, 6, 1)) })
            .ReturnsAsync(new List<LedgerItemFlexiDto>());

        var svc = CreateService(client.Object, ctx, opts);

        // Act
        var result = await svc.SyncAsync();

        // Assert — lastUpdateFrom should equal InitialBackfillFrom when no watermark
        client.Verify(c => c.GetChangedSinceAsync(
            new DateTime(2024, 1, 1), 10, 0, It.IsAny<CancellationToken>()), Times.Once);
        result.IsSuccess.Should().BeTrue();
        result.RowsFetched.Should().Be(1);
    }

    [Fact]
    public async Task SyncAsync_WhenWatermarkExists_FetchesFromWatermarkMinus1Hour()
    {
        // Arrange
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();
        var watermark = new DateTimeOffset(2025, 3, 10, 12, 0, 0, TimeSpan.Zero);
        ctx.SyncStates.Add(new SyncState
        {
            EntityName = "ledger_entry",
            Watermark = watermark,
            LastRunStatus = "OK"
        });
        await ctx.SaveChangesAsync();

        client.Setup(c => c.GetChangedSinceAsync(
                It.IsAny<DateTime>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItemFlexiDto>());

        var svc = CreateService(client.Object, ctx);

        // Act
        await svc.SyncAsync();

        // Assert — lastUpdateFrom should be watermark minus 1 hour
        var expectedFrom = watermark.AddHours(-1).UtcDateTime;
        client.Verify(c => c.GetChangedSinceAsync(
            expectedFrom, It.IsAny<int?>(), 0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_UpsertsRowsAndAdvancesWatermark()
    {
        // Arrange
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();

        client.SetupSequence(c => c.GetChangedSinceAsync(
                It.IsAny<DateTime>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItemFlexiDto>
            {
                MakeLedgerDto(10, new DateTime(2025, 1, 1)),
                MakeLedgerDto(11, new DateTime(2025, 1, 2))
            })
            .ReturnsAsync(new List<LedgerItemFlexiDto>());

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var entries = await ctx.LedgerEntries.ToListAsync();
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.FlexiId == 10);
        entries.Should().Contain(e => e.FlexiId == 11);
        entries.Should().AllSatisfy(e => e.LastModified.Should().NotBeNull());

        var state = await ctx.SyncStates.FindAsync("ledger_entry");
        state!.LastRunStatus.Should().Be("OK");
        state.Watermark.Should().NotBeNull();
        state.LastRunRowsFetched.Should().Be(2);
    }

    [Fact]
    public async Task SyncAsync_OnClientError_RecordsFailedStatusAndKeepsWatermarkUnchanged()
    {
        // Arrange
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();
        var originalWatermark = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        ctx.SyncStates.Add(new SyncState
        {
            EntityName = "ledger_entry",
            Watermark = originalWatermark,
            LastRunStatus = "OK"
        });
        await ctx.SaveChangesAsync();

        client.Setup(c => c.GetChangedSinceAsync(
                It.IsAny<DateTime>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Flexi unreachable"));

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        var state = await ctx.SyncStates.FindAsync("ledger_entry");
        state!.LastRunStatus.Should().Be("FAILED");
        state.Watermark.Should().Be(originalWatermark);
        state.LastErrorMessage.Should().Contain("Flexi unreachable");
    }
}
