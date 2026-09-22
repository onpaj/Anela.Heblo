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

    // Shaped the way FlexiBee actually answers `ucetni-denik`: `id` is -1 on every row of this
    // view, the identity sits in `idUcetniDenik` (SDK: JournalId), and the account / cost centre /
    // currency arrive as nested arrays rather than the @showAs scalars. See
    // LedgerSyncServiceMappingTests for the transcribed live response.
    private static LedgerItemFlexiDto MakeLedgerDto(long id, DateTime accountingDate, double amount = 100.0) =>
        new()
        {
            Id = -1,
            JournalId = id.ToString(),
            AccountingDate = accountingDate,
            LastUpdate = accountingDate.AddHours(1),
            AmountLocal = amount,
            ParSymbol = $"CODE{id}",
            DebitAccountList = [new AccountFlexiDto { Code = "501000" }],
            CreditAccountList = [new AccountFlexiDto { Code = "221000" }],
            Currency = [new CurrencyFlexiDto { Code = "CZK" }],
            DepartmentList = [new DepartmentFlexiDto { Code = "C" }],
            Period = $"{accountingDate:yyyy/MM}",
            DocumentIdEvidencePath = "faktura-prijata",
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
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10, 0, It.IsAny<CancellationToken>()), Times.Once);
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

    [Fact]
    public async Task SyncAsync_WhenAPageFailsPartWayThrough_KeepsTheProgressItAlreadyMade()
    {
        // The 2020-onward delta is ~680k rows against a 1-vCore server and the job cancels after
        // RequestTimeoutSeconds. Without a high-water mark, a run that cannot finish leaves the
        // watermark untouched, the next run restarts from InitialBackfillFrom, and the sync never
        // converges.
        //
        // Keeping the mark is sound because the pages are ordered: LedgerRequest(DateTime since)
        // sets Order = "lastUpdate" alongside the filter — asserted directly in
        // LedgerRequestOrderingTests, against both the property and the serialised wire form, so
        // this assumption cannot silently rot. Everything below the mark has therefore landed.
        //
        // Tied timestamps are the one wrinkle (FlexiBee bulk-touches the ledger — 288k rows share
        // lastUpdate = 2025-06-03) and `order` carries no tiebreaker, so a tie group can split
        // across a page boundary. The next run's -1h overlap re-fetches the whole group, and the
        // upsert is idempotent.
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();

        client.SetupSequence(c => c.GetChangedSinceAsync(
                It.IsAny<DateTime>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(1, 10)
                .Select(i => MakeLedgerDto(i, new DateTime(2025, 1, i)))
                .ToList())
            .ThrowsAsync(new HttpRequestException("Flexi went away mid-backfill"));

        var svc = CreateService(client.Object, ctx);

        var result = await svc.SyncAsync();

        result.IsSuccess.Should().BeFalse();
        (await ctx.LedgerEntries.CountAsync()).Should().Be(10);

        var state = await ctx.SyncStates.FindAsync("ledger_entry");
        state!.LastRunStatus.Should().Be("FAILED");
        // Highest LastUpdate of the page that did land: 2025-01-10 + 1h, Prague-local -> UTC.
        var expected = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2025, 1, 10, 1, 0, 0, DateTimeKind.Unspecified), TimeZoneInfo.Local);
        state.Watermark!.Value.UtcDateTime.Should().Be(expected);
        state.LastRunRowsUpserted.Should().Be(10);
    }

    [Fact]
    public async Task SyncAsync_OnFailureLeavesAnExistingWatermarkExactlyWhereItWas()
    {
        // A failed run must neither rewind an already-advanced watermark nor push it forward past
        // rows it did not ingest.
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();
        var originalWatermark = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        ctx.SyncStates.Add(new SyncState
        {
            EntityName = "ledger_entry",
            Watermark = originalWatermark,
            LastRunStatus = "OK"
        });
        await ctx.SaveChangesAsync();

        client.SetupSequence(c => c.GetChangedSinceAsync(
                It.IsAny<DateTime>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(1, 10)
                .Select(i => MakeLedgerDto(i, new DateTime(2020, 1, i)))
                .ToList())
            .ThrowsAsync(new HttpRequestException("Flexi went away"));

        var svc = CreateService(client.Object, ctx);

        await svc.SyncAsync();

        var state = await ctx.SyncStates.FindAsync("ledger_entry");
        state!.Watermark.Should().Be(originalWatermark);
    }

    [Fact]
    public void Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified()
    {
        // Regression test: SDK returns Kind=Unspecified representing Prague local time.
        // Map() must call ConvertTimeToUtc, not ToUniversalTime().
        // DateTimeOffset.DateTime always strips offset and returns Kind=Unspecified,
        // so passing any DateTimeOffset exercises the Unspecified path in ConvertTimeToUtc.
        var unspecified = new DateTime(2025, 6, 19, 10, 0, 0, DateTimeKind.Unspecified);
        var dto = new LedgerItemFlexiDto
        {
            Id = -1,
            JournalId = "99",
            AccountingDate = unspecified,
            LastUpdate = new DateTimeOffset(unspecified, TimeSpan.Zero),
            AmountLocal = 100.0,
            ParSymbol = "CODE99",
            DebitAccountList = [new AccountFlexiDto { Code = "501000" }],
            CreditAccountList = [new AccountFlexiDto { Code = "221000" }],
            Currency = [new CurrencyFlexiDto { Code = "CZK" }],
            Description = "Regression test entry",
        };

        var entry = LedgerSyncService.Map(dto);

        Assert.NotNull(entry.LastModified);
        Assert.Equal(TimeSpan.Zero, entry.LastModified!.Value.Offset);
        var expected = TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
        Assert.Equal(expected, entry.LastModified.Value.UtcDateTime);
    }
}
