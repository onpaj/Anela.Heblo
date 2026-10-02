# Architecture Review: Test coverage for `ConversionsSyncService.SyncChunkAsync`

## Skip Design: true

This is a backend-only, test-only change with no new or changed UI, API surface, or visual
component. No design phase is needed.

## Architectural Fit Assessment

This is a pure test-addition task against an existing, stable vertical slice
(`Anela.Heblo.Adapters.GoogleAnalytics`). The spec already correctly identifies that
`SyncOrchestrationTests.cs` only exercises `Ga4SyncService` against a stub
`IGa4EntitySyncService`, and that the concrete `ConversionsSyncService.SyncChunkAsync` — row
filtering, positional mapping, and upsert — is untouched by any existing test.

Verified against the actual code (`backend/src/Adapters/Anela.Heblo.Adapters.GoogleAnalytics/Sync/`
and `backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/`):

- `ConversionsSyncService` is a thin subclass of `Ga4EntitySyncServiceBase`. The base class's
  orchestration (chunking, watermark advancement, failure isolation, `WithoutOtherBucket` filter
  mechanics) is already covered elsewhere (`BackfillChunkingTests.cs`,
  `TrailingReprocessWindowTests.cs`, `FailedWriteIsolationTests.cs`, `SyncOrchestrationTests.cs`)
  using `TrafficSyncService` as the concrete instance under test.
- The established pattern for testing a *concrete* sync service end-to-end is: build it via a
  `Ga4TestHarness` factory method, feed it rows through `FakeGa4ReportClient`, run `SyncAsync()`,
  then assert against a **second**, independent `Ga4DbContext` opened on the same in-memory
  database name (`Ga4TestHarness.StoredStateAsync` / `NewDbContext(databaseName)`). This is
  deliberate — see the doc comment on `Ga4TestHarness.NewDatabaseName()` — and any new test must
  follow it; asserting through the same context instance the service wrote through would pass
  even if persistence were removed entirely.
- `Ga4TestHarness` already has exactly this factory/row-builder pair for `TrafficSyncService`
  (`TrafficSync` + `TrafficRow`) and equivalents for `LandingPageSyncService`, `PageSyncService`,
  `TrafficMonthlySyncService`. `ConversionsSyncService` has neither. Adding them is additive and
  fits the existing shape exactly — no new test infrastructure concept is required.

The spec's proposed approach (FR-1 through FR-5) is architecturally sound and requires no
deviation. I confirmed the exact field-to-index mapping by reading
`ConversionsSyncService.SyncChunkAsync` directly:

```
DimensionValues[0] -> Ga4ValueParser.ToDate      -> ConversionsDaily.Date
DimensionValues[1] -> Ga4ValueParser.ToDimension -> ConversionsDaily.ChannelGroup
MetricValues[0]    -> Ga4ValueParser.ToLong       -> ConversionsDaily.Transactions
MetricValues[1]    -> Ga4ValueParser.ToDecimal    -> ConversionsDaily.PurchaseRevenue
```

and that `WithoutOtherBucket` (defined on `Ga4EntitySyncServiceBase`) filters on
`DimensionValues[0]` — i.e. the **date** dimension being the literal `"(other)"` — not the channel
group. This matters for FR-2's fixture: the "(other)" row must have `"(other)"` as its **first**
dimension value, not its channel group, to exercise the actual filter path
`ConversionsSyncService` runs through.

## Proposed Architecture

### Component Overview

No new components. The test targets the existing chain:

```
FakeGa4ReportClient (test double)
        |
        v
ConversionsSyncService.SyncChunkAsync   <-- under test (0% covered today)
        |  WithoutOtherBucket (inherited, already covered elsewhere for the filter mechanics
        |                      themselves; here we assert ConversionsSyncService actually calls it)
        |  Ga4ValueParser.ToDate/ToDimension/ToLong/ToDecimal (already unit-tested in isolation
        |                      via Ga4ValueParserTests.cs; here we assert correct index wiring)
        v
Ga4ChunkUpsert.ReplaceRangeAsync (inherited call site; already covered for its own
        |                          insert/update/delete mechanics via other tables — here we
        |                          assert ConversionsSyncService wires the right key + lambda)
        v
Ga4DbContext.ConversionsDaily (EF Core in-memory provider)
```

The new test file asserts the **wiring** between these already-covered pieces as executed by
`ConversionsSyncService` specifically — it deliberately does not re-test
`Ga4ValueParser`, `Ga4ChunkUpsert`, or `WithoutOtherBucket` in isolation (that coverage already
exists and stays where it is).

### Key Design Decisions

#### Decision 1: Test through the public `SyncAsync()` entry point, not `SyncChunkAsync` directly
**Options considered:**
- (a) Make `SyncChunkAsync` `internal` + `[InternalsVisibleTo]` and call it directly in tests.
- (b) Drive the service through its public `SyncAsync()` (inherited from
  `Ga4EntitySyncServiceBase`), same as every other concrete sync service is tested today.

**Chosen approach:** (b).

**Rationale:** Every existing concrete-service test (`BackfillChunkingTests.cs`,
`TrailingReprocessWindowTests.cs`, `FailedWriteIsolationTests.cs`) drives through `SyncAsync()`.
Introducing `InternalsVisibleTo` or a protected-method test seam for this one service would be an
unjustified deviation from the established pattern and adds a maintenance surface for no benefit
— `SyncAsync()` with a single-chunk window (`ChunkDays` wide enough to cover the whole fixture
date range, as `Ga4TestHarness.Options()`'s default `ChunkDays = 31` already does) exercises
`SyncChunkAsync` exactly once, which is all these tests need.

#### Decision 2: Where the row-order/channel-group fixture values live
**Options considered:**
- (a) Hard-code `Ga4Row` construction inline in each test.
- (b) Add a `ConversionsRow` builder to `Ga4TestHarness`, mirroring `TrafficRow`/others.

**Chosen approach:** (b), per spec FR-1.

**Rationale:** Consistency with every other entity's test harness helper, and it centralizes the
one place a future column-order change in `ConversionsSyncService` would need a matching harness
update — making such a change visible in review rather than silently invalidating tests.

## Implementation Guidance

### Directory / Module Structure
- New test file: `backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/ConversionsSyncServiceTests.cs`
  (sibling to `BackfillChunkingTests.cs`, same namespace `Anela.Heblo.Adapters.GoogleAnalytics.Tests`).
- Modified file: `backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Ga4TestHarness.cs`
  — add `ConversionsSync(...)` and `ConversionsRow(...)` following the exact shape of the existing
  `TrafficSync`/`TrafficRow` pair (see lines 90-100 of that file for the pattern to mirror).
- No production files change.

### Interfaces and Contracts
```csharp
// In Ga4TestHarness — mirrors TrafficSync exactly, only the concrete type differs.
public static ConversionsSyncService ConversionsSync(
    Ga4DbContext dbContext, IGa4ReportClient client, Ga4SyncOptions options, DateTimeOffset now) =>
    new(client,
        new Ga4SyncWatermarkRepository(dbContext),
        dbContext,
        Microsoft.Extensions.Options.Options.Create(options),
        new FixedTimeProvider(now),
        NullLogger<ConversionsSyncService>.Instance);

// Row order matches ConversionsSyncService's Dimensions = ["date", "sessionDefaultChannelGroup"]
// and Metrics = ["transactions", "purchaseRevenue"] exactly.
public static Ga4Row ConversionsRow(string date, string channelGroup, long transactions, decimal purchaseRevenue) =>
    new([date, channelGroup], [transactions.ToString(), purchaseRevenue.ToString(CultureInfo.InvariantCulture)]);
```
`ConversionsSyncService`'s constructor is `internal`-friendly (same accessibility as
`TrafficSyncService`'s, confirmed by the existing `TrafficSync` harness method compiling against
it) — no visibility changes needed.

Note `ConversionsRow`'s decimal-to-string conversion must use `CultureInfo.InvariantCulture`
(`Ga4ValueParser.ToDecimal` parses with `NumberStyles.Float, CultureInfo.InvariantCulture`) to
avoid a locale-dependent decimal separator breaking the fixture on non-`en-US`/non-`invariant`
build agents.

### Data Flow
1. Test builds one or more `Ga4Row` via `Ga4TestHarness.ConversionsRow(...)`.
2. `FakeGa4ReportClient` returns them, filtered to the request window (existing harness
   behavior — no change needed).
3. `Ga4TestHarness.ConversionsSync(...).SyncAsync()` runs one chunk (fixture dates must fit inside
   the default `ChunkDays = 31` window used by `Ga4TestHarness.Options()`, or the test must widen
   `ChunkDays`/narrow `BackfillFrom` explicitly so the whole fixture lands in a single chunk —
   otherwise a two-row fixture spanning a chunk boundary would call `SyncChunkAsync` twice and
   complicate the fetched/upserted assertions).
4. Test opens a second `Ga4DbContext` via the same database name (`Ga4TestHarness.NewDbContext(databaseName)`,
   per the `StoredStateAsync` convention) and asserts on `ConversionsDaily` rows, and asserts on
   the `Ga4SyncResult` returned directly from `SyncAsync()` for `RowsFetched`/`RowsUpserted`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Fixture spans more than one chunk (`ChunkDays`), making `RowsFetched`/`RowsUpserted` assertions ambiguous across multiple `SyncChunkAsync` calls | Medium | Keep all fixture dates within the default 31-day chunk, or explicitly set `ChunkDays` wide enough in the test's `Ga4TestHarness.Options()` call; assert against the single `Ga4SyncResult` returned by `SyncAsync()`, which is a nightly cumulative total. |
| Decimal fixture value round-trips incorrectly through `ToString()` due to culture | Low | Use `CultureInfo.InvariantCulture` in `ConversionsRow`'s formatting, as noted above. |
| Update-case test (FR-4) accidentally reads back through the same `Ga4DbContext` instance the service wrote through, masking a real persistence bug | Medium | Follow the existing `StoredStateAsync`/second-context convention already used elsewhere in this test project; do not introduce a new pattern. |
| `WithoutOtherBucket` fixture accidentally puts `"(other)"` on `ChannelGroup` (`DimensionValues[1]`) instead of `Date` (`DimensionValues[0]`), silently testing a no-op filter path | Medium | Explicit in FR-2 and this review: the "(other)" bucket check is on `DimensionValues[0]` only (per `Ga4EntitySyncServiceBase.WithoutOtherBucket`). The fixture must put `"(other)"` as the row's first (date) value. |

## Specification Amendments

None — the spec (FR-1 through FR-5) is implementable as written. This review adds only
implementation-level precision (exact index-to-field mapping, the `CultureInfo.InvariantCulture`
formatting detail, and the single-chunk fixture constraint) that a developer needs but that does
not change any acceptance criterion.

## Prerequisites

None. No migrations, no config, no new infrastructure. The change can start immediately against
`main`/this feature branch as-is.
