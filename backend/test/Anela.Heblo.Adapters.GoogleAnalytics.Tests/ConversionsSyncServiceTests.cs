using Anela.Heblo.Adapters.GoogleAnalytics.Sync;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Tests;

/// <summary>
/// ConversionsSyncService.SyncChunkAsync's own body — (other)-bucket filtering, positional
/// row-to-entity mapping, and the upsert key/update lambda — is not exercised by
/// SyncOrchestrationTests, which only drives Ga4SyncService against a stub IGa4EntitySyncService.
/// These tests drive the concrete ConversionsSyncService end to end, same pattern as
/// BackfillChunkingTests uses for TrafficSyncService.
/// </summary>
public class ConversionsSyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 4, 10, 0, TimeSpan.Zero);

    [Fact]
    public async Task drops_rows_whose_date_is_the_other_bucket_but_keeps_the_rest()
    {
        // Arrange — WithoutOtherBucket only checks DimensionValues[0], which for
        // ConversionsSyncService is the "date" dimension (Dimensions = ["date", "sessionDefaultChannelGroup"]).
        var database = Ga4TestHarness.NewDatabaseName();
        var dbContext = Ga4TestHarness.NewDbContext(database);
        var options = Ga4TestHarness.Options(o => o.BackfillFrom = "2026-03-09");
        var client = new FakeGa4ReportClient(_ => new[]
        {
            Ga4TestHarness.ConversionsRow("20260309", "Direct", 5, 100m),
            Ga4TestHarness.ConversionsRow("(other)", "Direct", 1, 1m),
        });

        // Act
        var result = await Ga4TestHarness.ConversionsSync(dbContext, client, options, Now).SyncAsync();

        // Assert
        result.RowsFetched.Should().Be(2, "the raw GA4 row count is unfiltered");
        result.RowsUpserted.Should().Be(1, "only the row whose date is not the (other) bucket is written");

        await using var verify = Ga4TestHarness.NewDbContext(database);
        verify.ConversionsDaily.Should().ContainSingle(x => x.Date == new DateOnly(2026, 3, 9) && x.ChannelGroup == "Direct");
    }

    [Fact]
    public async Task maps_dimensions_and_metrics_by_position_without_transposing_them()
    {
        // Arrange — distinguishable values so a Transactions/PurchaseRevenue swap, or a
        // Date/ChannelGroup swap, would fail this assertion instead of passing by coincidence.
        var database = Ga4TestHarness.NewDatabaseName();
        var dbContext = Ga4TestHarness.NewDbContext(database);
        var options = Ga4TestHarness.Options(o => o.BackfillFrom = "2026-03-09");
        var client = new FakeGa4ReportClient(_ => new[]
        {
            Ga4TestHarness.ConversionsRow("20260309", "Paid Search", 7, 123.45m),
        });

        // Act
        await Ga4TestHarness.ConversionsSync(dbContext, client, options, Now).SyncAsync();

        // Assert
        await using var verify = Ga4TestHarness.NewDbContext(database);
        var row = verify.ConversionsDaily.Should().ContainSingle().Subject;
        row.Date.Should().Be(new DateOnly(2026, 3, 9));
        row.ChannelGroup.Should().Be("Paid Search");
        row.Transactions.Should().Be(7);
        row.PurchaseRevenue.Should().Be(123.45m);
    }

    [Fact]
    public async Task inserts_a_new_row_on_a_fresh_sync_window()
    {
        // Arrange — empty DB, one report row.
        var database = Ga4TestHarness.NewDatabaseName();
        var dbContext = Ga4TestHarness.NewDbContext(database);
        var options = Ga4TestHarness.Options(o => o.BackfillFrom = "2026-03-09");
        var client = new FakeGa4ReportClient(_ => new[]
        {
            Ga4TestHarness.ConversionsRow("20260309", "Organic Search", 3, 50m),
        });

        // Act
        var result = await Ga4TestHarness.ConversionsSync(dbContext, client, options, Now).SyncAsync();

        // Assert
        result.RowsUpserted.Should().Be(1);
        await using var verify = Ga4TestHarness.NewDbContext(database);
        verify.ConversionsDaily.Should().ContainSingle(
            x => x.Date == new DateOnly(2026, 3, 9) && x.ChannelGroup == "Organic Search" && x.Transactions == 3);
    }

    [Fact]
    public async Task updates_the_existing_row_in_place_on_a_resync_instead_of_duplicating()
    {
        // Arrange — BackfillFrom == the fixture date, so both the first run (start = BackfillFrom)
        // and the second run (start = max(BackfillFrom, watermark - TrailingReprocessDays)) ask
        // GA4 about the same single day, 2026-03-09.
        var database = Ga4TestHarness.NewDatabaseName();
        var dbContext = Ga4TestHarness.NewDbContext(database);
        var options = Ga4TestHarness.Options(o => o.BackfillFrom = "2026-03-09");

        var revised = false;
        var client = new FakeGa4ReportClient(_ => new[]
        {
            revised
                ? Ga4TestHarness.ConversionsRow("20260309", "Organic Search", 9, 250m)
                : Ga4TestHarness.ConversionsRow("20260309", "Organic Search", 3, 50m),
        });

        var service = Ga4TestHarness.ConversionsSync(dbContext, client, options, Now);

        // Act
        await service.SyncAsync();
        revised = true;
        await service.SyncAsync();

        // Assert — still exactly one row for the key, holding the revised values.
        await using var verify = Ga4TestHarness.NewDbContext(database);
        var row = verify.ConversionsDaily.Should().ContainSingle(
            x => x.Date == new DateOnly(2026, 3, 9) && x.ChannelGroup == "Organic Search").Subject;
        row.Transactions.Should().Be(9, "the second sync's values must overwrite the first, not sit beside them");
        row.PurchaseRevenue.Should().Be(250m);
    }

    [Fact]
    public async Task keeps_both_rows_when_channel_group_differs_on_the_same_date()
    {
        // Arrange — proves the upsert key is (Date, ChannelGroup), not Date alone.
        var database = Ga4TestHarness.NewDatabaseName();
        var dbContext = Ga4TestHarness.NewDbContext(database);
        var options = Ga4TestHarness.Options(o => o.BackfillFrom = "2026-03-09");
        var client = new FakeGa4ReportClient(_ => new[]
        {
            Ga4TestHarness.ConversionsRow("20260309", "Organic Search", 3, 50m),
            Ga4TestHarness.ConversionsRow("20260309", "Direct", 1, 20m),
        });

        // Act
        var result = await Ga4TestHarness.ConversionsSync(dbContext, client, options, Now).SyncAsync();

        // Assert
        result.RowsUpserted.Should().Be(2);
        await using var verify = Ga4TestHarness.NewDbContext(database);
        verify.ConversionsDaily.Should().HaveCount(2);
        verify.ConversionsDaily.Should().Contain(x => x.ChannelGroup == "Organic Search" && x.Transactions == 3);
        verify.ConversionsDaily.Should().Contain(x => x.ChannelGroup == "Direct" && x.Transactions == 1);
    }
}
