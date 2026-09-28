# Design: Test coverage for `ConversionsSyncService.SyncChunkAsync`

## Component Design

### `Ga4TestHarness` (modified)
`backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Ga4TestHarness.cs`

Two additive static members, placed alongside the existing `TrafficSync`/`TrafficRow` pair:

- **`ConversionsSync(Ga4DbContext dbContext, IGa4ReportClient client, Ga4SyncOptions options, DateTimeOffset now) -> ConversionsSyncService`**
  Responsibility: construct a `ConversionsSyncService` wired to test doubles, identical in shape
  to `TrafficSync`/`LandingPageSync`/`PageSync`/`TrafficMonthlySync` — same five-argument
  constructor call (`client`, `new Ga4SyncWatermarkRepository(dbContext)`, `dbContext`,
  `Options.Create(options)`, `new FixedTimeProvider(now)`, `NullLogger<ConversionsSyncService>.Instance`).

- **`ConversionsRow(string date, string channelGroup, long transactions, decimal purchaseRevenue) -> Ga4Row`**
  Responsibility: build a `Ga4Row` whose `DimensionValues`/`MetricValues` are in the exact
  positional order `ConversionsSyncService` reads them
  (`Dimensions = ["date", "sessionDefaultChannelGroup"]`, `Metrics = ["transactions", "purchaseRevenue"]`),
  formatting the decimal with `CultureInfo.InvariantCulture` to match how `Ga4ValueParser.ToDecimal`
  parses it back.

### `ConversionsSyncServiceTests` (new)
`backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/ConversionsSyncServiceTests.cs`

One xUnit test class, structured like `BackfillChunkingTests`/`FailedWriteIsolationTests`: each
`[Fact]` builds a `Ga4DbContext` + `Ga4SyncOptions` via `Ga4TestHarness`, feeds rows through
`FakeGa4ReportClient`, drives the service under test through its public `SyncAsync()`, and asserts
against a second, independently-opened `Ga4DbContext` on the same database name plus the
`Ga4SyncResult` returned by `SyncAsync()`.

Responsibilities of each test (mapped from `spec.r1.md`'s FR-2 through FR-5):

| Test | Responsibility |
|------|----------------|
| `drops_rows_whose_date_is_the_other_bucket_but_keeps_the_rest` | FR-2: one valid row + one row with `DimensionValues[0] == "(other)"` → exactly one row persisted; `RowsFetched == 2`, `RowsUpserted == 1`. |
| `maps_dimensions_and_metrics_by_position_without_transposing_them` | FR-3: single distinguishable row → `Transactions`/`PurchaseRevenue`/`Date`/`ChannelGroup` all land in the correct field, not swapped. |
| `inserts_a_new_row_on_a_fresh_sync_window` | FR-4 (insert case): empty DB, one report row → exactly one new `ConversionsDaily` row. |
| `updates_the_existing_row_in_place_on_a_resync_instead_of_duplicating` | FR-4 (update case): pre-existing row for a `(Date, ChannelGroup)` key, second `SyncAsync()` with revised values for the same key → still one row, values updated, not duplicated. |
| `keeps_both_rows_when_channel_group_differs_on_the_same_date` | FR-4 (composite key): two rows, same `Date`, different `ChannelGroup` → both kept, proving the key is `(Date, ChannelGroup)` and not `Date` alone. |

`ChunkOutcome`/`Ga4SyncResult` fetched-vs-upserted assertions (FR-5) are folded into the tests
above rather than given a separate test, per the spec's own note that FR-5 is "covered by the
assertions in FR-2 and FR-4" — no additional component needed.

## Data Schemas

No schema changes. Tests assert against the existing shapes only:

```csharp
// Anela.Heblo.Persistence.Ga4.Entities.ConversionsDaily (unchanged)
public class ConversionsDaily
{
    public DateOnly Date { get; set; }
    public string ChannelGroup { get; set; } = "";
    public long Transactions { get; set; }
    public decimal PurchaseRevenue { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}

// Anela.Heblo.Adapters.GoogleAnalytics.Sync.IGa4EntitySyncService (unchanged)
public sealed record Ga4SyncResult(string EntityName, int RowsFetched, int RowsUpserted, bool IsSuccess);
```

Row fixture shape produced by the new `ConversionsRow` harness helper (test-only, not persisted
anywhere):

```csharp
new Ga4Row(
    DimensionValues: [date, channelGroup],
    MetricValues: [transactions.ToString(), purchaseRevenue.ToString(CultureInfo.InvariantCulture)]);
```
