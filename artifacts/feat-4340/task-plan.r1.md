# ConversionsSyncService Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the 0%-coverage gap on `ConversionsSyncService.SyncChunkAsync` by adding unit tests that exercise `(other)`-bucket filtering, positional row-to-entity mapping, and upsert insert/update/composite-key behaviour through the service's real `SyncAsync()` entry point.

**Architecture:** Add a `ConversionsSync`/`ConversionsRow` factory pair to `Ga4TestHarness.cs`, mirroring the existing `TrafficSync`/`TrafficRow` pair, then add a new `ConversionsSyncServiceTests.cs` test class that drives `ConversionsSyncService` via `FakeGa4ReportClient` and asserts against a second, independent `Ga4DbContext`. No production code changes.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, EF Core InMemory provider (existing test project `Anela.Heblo.Adapters.GoogleAnalytics.Tests`).

---

### task: add-ga4-harness-conversions-helpers

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Ga4TestHarness.cs`

- [ ] **Step 1: Add `ConversionsSync` and `ConversionsRow` to `Ga4TestHarness`**

Open `backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Ga4TestHarness.cs`. Immediately after the existing `TrafficRow` method (the one returning `new([date, channel], [sessions.ToString(), "0", "0", "0", "0", "0"])`), insert:

```csharp
    public static ConversionsSyncService ConversionsSync(
        Ga4DbContext dbContext, IGa4ReportClient client, Ga4SyncOptions options, DateTimeOffset now) =>
        new(client,
            new Ga4SyncWatermarkRepository(dbContext),
            dbContext,
            Microsoft.Extensions.Options.Options.Create(options),
            new FixedTimeProvider(now),
            NullLogger<ConversionsSyncService>.Instance);

    /// <summary>
    /// Row order matches ConversionsSyncService's Dimensions = ["date", "sessionDefaultChannelGroup"]
    /// and Metrics = ["transactions", "purchaseRevenue"] exactly — DimensionValues[0] is the date,
    /// DimensionValues[1] the channel group, MetricValues[0] transactions, MetricValues[1] revenue.
    /// </summary>
    public static Ga4Row ConversionsRow(string date, string channelGroup, long transactions, decimal purchaseRevenue) =>
        new([date, channelGroup], [transactions.ToString(), purchaseRevenue.ToString(CultureInfo.InvariantCulture)]);
```

No new `using` is required: `System.Globalization` (for `CultureInfo`), `Anela.Heblo.Adapters.GoogleAnalytics.Sync` (for `ConversionsSyncService`), and `Microsoft.Extensions.Logging.Abstractions` (for `NullLogger<T>`) are already imported at the top of this file.

- [ ] **Step 2: Build the test project to verify it compiles**

Run: `cd backend && dotnet build test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Anela.Heblo.Adapters.GoogleAnalytics.Tests.csproj`
Expected: `Build succeeded.` with no errors. (There is no test yet that calls these helpers, so this step only proves the new methods compile against the existing `ConversionsSyncService` constructor and `Ga4Row` record.)

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Ga4TestHarness.cs
git commit -m "test(ga4): add ConversionsSync/ConversionsRow test harness helpers"
```

---

### task: add-conversions-sync-service-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/ConversionsSyncServiceTests.cs`

This task depends on `add-ga4-harness-conversions-helpers` being complete (it uses `Ga4TestHarness.ConversionsSync`/`ConversionsRow`).

- [ ] **Step 1: Create the test file with its scaffold and the first test — `(other)`-bucket filtering**

Create `backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/ConversionsSyncServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the first test to confirm it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Anela.Heblo.Adapters.GoogleAnalytics.Tests.csproj --filter "FullyQualifiedName~ConversionsSyncServiceTests.drops_rows_whose_date_is_the_other_bucket_but_keeps_the_rest"`
Expected: `Passed! - Failed: 0, Passed: 1`. `ConversionsSyncService` already filters correctly in production — this test is coverage, not a bug fix, so it should pass on the first run. If it fails, do not "fix" the production code to make it pass (out of scope per spec NFR-2); stop and re-read `ConversionsSyncService.SyncChunkAsync` and `Ga4EntitySyncServiceBase.WithoutOtherBucket` to find the mistake in the test fixture instead.

- [ ] **Step 3: Add the mapping test — no transposed columns**

Add inside the `ConversionsSyncServiceTests` class, after the first test:

```csharp
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
```

- [ ] **Step 4: Run the mapping test to confirm it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Anela.Heblo.Adapters.GoogleAnalytics.Tests.csproj --filter "FullyQualifiedName~ConversionsSyncServiceTests.maps_dimensions_and_metrics_by_position_without_transposing_them"`
Expected: `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 5: Add the fresh-insert test**

Add after the mapping test:

```csharp
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
```

- [ ] **Step 6: Run the insert test to confirm it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Anela.Heblo.Adapters.GoogleAnalytics.Tests.csproj --filter "FullyQualifiedName~ConversionsSyncServiceTests.inserts_a_new_row_on_a_fresh_sync_window"`
Expected: `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 7: Add the resync/update test**

Add after the insert test. This drives `SyncAsync()` twice over the same single-day window: the first run inserts a baseline row, the second run — with the trailing-reprocess window covering the same date because `BackfillFrom` equals that date — re-fetches it with revised values and must update in place, not duplicate:

```csharp
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
```

- [ ] **Step 8: Run the resync test to confirm it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Anela.Heblo.Adapters.GoogleAnalytics.Tests.csproj --filter "FullyQualifiedName~ConversionsSyncServiceTests.updates_the_existing_row_in_place_on_a_resync_instead_of_duplicating"`
Expected: `Passed! - Failed: 0, Passed: 1`. If the second `SyncAsync()` does not re-fetch 2026-03-09 (e.g. `RowsFetched` assertions elsewhere would show a 0-row second chunk), double-check `options.BackfillFrom` is exactly `"2026-03-09"` — a later date would make the second run's rewound-start clamp to a day whose report the `FakeGa4ReportClient` closure still returns (since it ignores `request` and always returns one row), but the window computation depends on this date for the *first* run only if it's before `Yesterday()`; keep `BackfillFrom` at `"2026-03-09"` and `Now` as declared above so both runs' end date is `2026-03-09` (== `Yesterday()` relative to `Now`).

- [ ] **Step 9: Add the composite-key test and close the class**

Add after the resync test, then close the class with `}`:

```csharp
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
```

- [ ] **Step 10: Run the full new test class**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Anela.Heblo.Adapters.GoogleAnalytics.Tests.csproj --filter "FullyQualifiedName~ConversionsSyncServiceTests"`
Expected: `Passed! - Failed: 0, Passed: 5`.

- [ ] **Step 11: Run the whole GoogleAnalytics adapter test project to confirm no regressions**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Anela.Heblo.Adapters.GoogleAnalytics.Tests.csproj`
Expected: all tests pass (this project's full suite, including `SyncOrchestrationTests`, `BackfillChunkingTests`, `TrailingReprocessWindowTests`, `DegradedReportTests`, `ReportingBoundaryTests`, `AdapterRegistrationTests`, `FailedWriteIsolationTests`, `Ga4ValueParserTests`, plus the new `ConversionsSyncServiceTests`) — 0 failures.

- [ ] **Step 12: Format and commit**

```bash
cd backend && dotnet format --include test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/ConversionsSyncServiceTests.cs
git add test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/ConversionsSyncServiceTests.cs
git commit -m "test(ga4): cover ConversionsSyncService.SyncChunkAsync — filtering, mapping, upsert"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (harness helpers) → `add-ga4-harness-conversions-helpers`.
- FR-2 ((other)-bucket filtering, fetched-vs-upserted divergence) → `drops_rows_whose_date_is_the_other_bucket_but_keeps_the_rest`.
- FR-3 (column-order-safe mapping) → `maps_dimensions_and_metrics_by_position_without_transposing_them`.
- FR-4 (insert / update / composite key) → `inserts_a_new_row_on_a_fresh_sync_window`, `updates_the_existing_row_in_place_on_a_resync_instead_of_duplicating`, `keeps_both_rows_when_channel_group_differs_on_the_same_date`.
- FR-5 (`ChunkOutcome` tuple) → asserted via `RowsFetched`/`RowsUpserted` in the FR-2 and composite-key tests, per the spec's own note that FR-5 needs no separate test.
- NFR-1 (isolation/determinism) → every test uses a fresh `NewDatabaseName()`, asserts through a second `Ga4DbContext`, and uses the fixed `Now`/`FixedTimeProvider`.
- NFR-2 (no production changes) → explicitly called out in Step 2 of the second task; this plan touches only the two test-project files listed above.

**Placeholder scan:** No TBD/TODO markers; every step has complete, runnable code and exact `dotnet` commands with expected output.

**Type consistency:** `ConversionsRow`, `ConversionsSync`, `ConversionsSyncServiceTests`, and all field names (`Date`, `ChannelGroup`, `Transactions`, `PurchaseRevenue`) are used identically across both tasks and match the actual `ConversionsDaily`/`ConversionsSyncService`/`Ga4TestHarness` definitions read directly from the repository.
