# Specification: Test coverage for `ConversionsSyncService.SyncChunkAsync`

## Summary
`ConversionsSyncService` (in `Anela.Heblo.Adapters.GoogleAnalytics`) feeds the `conversions_daily`
table used for e-commerce revenue reporting, but its `SyncChunkAsync` body — row filtering,
row-to-entity mapping, and the upsert call — has 0% line coverage. `SyncOrchestrationTests` only
exercises orchestration (`Ga4SyncService`/`Ga4EntitySyncServiceBase`) against a stub
`IGa4EntitySyncService`, never against the real `ConversionsSyncService`. This spec defines the
unit tests needed to close that gap using the existing `Ga4TestHarness` pattern, with no
production code changes.

## Background
`ConversionsSyncService` derives from `Ga4EntitySyncServiceBase`, which already has behavioral
coverage (chunking, watermark advancement, failure isolation) via `TrafficSyncService` in
`BackfillChunkingTests.cs`, `TrailingReprocessWindowTests.cs`, and `FailedWriteIsolationTests.cs`.
What is untested is specific to `ConversionsSyncService.SyncChunkAsync` itself:

- Filtering GA4 rows through the inherited `WithoutOtherBucket` helper (rows whose **first
  dimension**, i.e. `date`, is the literal string `"(other)"`, are dropped).
- Mapping `Ga4Row.DimensionValues[0..1]` and `MetricValues[0..1]` positionally to
  `ConversionsDaily.Date`, `ChannelGroup`, `Transactions`, `PurchaseRevenue` via
  `Ga4ValueParser`.
- Calling `Ga4ChunkUpsert.ReplaceRangeAsync` with `(Date, ChannelGroup)` as the composite key and
  an update lambda that reassigns `Transactions`, `PurchaseRevenue`, and `SyncedAt`.
- Returning `ChunkOutcome(report.Rows.Count, upserted)` — note `RowsFetched` is the **raw**
  row count from GA4 (before `WithoutOtherBucket` filtering), while `RowsUpserted` is the
  post-filter, post-dedupe count `Ga4ChunkUpsert.ReplaceRangeAsync` returns.

A silent regression here (e.g. transposing `transactions`/`purchaseRevenue` in the `Metrics`
array, or swapping the upsert key) would store wrong or duplicate revenue figures with no
observable error, since nothing currently asserts on it.

## Functional Requirements

### FR-1: `Ga4TestHarness` gains a `ConversionsSync` factory and a `ConversionsRow` builder
Add to `Ga4TestHarness.cs`, mirroring the existing `TrafficSync`/`TrafficRow` pair:
- `ConversionsSync(Ga4DbContext dbContext, IGa4ReportClient client, Ga4SyncOptions options, DateTimeOffset now) : ConversionsSyncService`
- `ConversionsRow(string date, string channelGroup, long transactions, decimal purchaseRevenue) : Ga4Row`
  — builds a `Ga4Row(["date", channelGroup], ["transactions", "purchaseRevenue"])` in the exact
  positional order `ConversionsSyncService` reads (`DimensionValues[0]`=date,
  `DimensionValues[1]`=channelGroup, `MetricValues[0]`=transactions, `MetricValues[1]`=purchaseRevenue).

**Acceptance criteria:**
- New helpers compile and are usable from a new test class the same way `TrafficSync`/`TrafficRow`
  are used in `BackfillChunkingTests.cs`.
- No existing test or production file is modified beyond this additive change to
  `Ga4TestHarness.cs`.

### FR-2: `(other)`-bucket filtering is verified through the real service
A report containing one normal row and one row whose **date** dimension is `"(other)"` results in
exactly one row reaching the database; the kept row's fields are correct.

**Acceptance criteria:**
- Given `FakeGa4ReportClient` returns two rows for a chunk — one valid, one with
  `DimensionValues[0] == "(other)"` — after `SyncAsync()`, `ConversionsDaily` in a second,
  independent `Ga4DbContext` (per the `StoredStateAsync` pattern) contains exactly one row.
- The returned `Ga4SyncResult.RowsFetched` equals 2 (raw GA4 row count is unfiltered) and
  `RowsUpserted` equals 1 (only the kept row is written) — this distinction must be asserted
  explicitly since it is easy to invert.

### FR-3: Row-to-entity mapping is column-order-safe
Transactions and purchase revenue are not silently swapped, and dimensions map to the correct
entity fields.

**Acceptance criteria:**
- A single-row report with distinguishable values (e.g. `transactions=7`,
  `purchaseRevenue=123.45m`, a channel group that is not also a valid number) results in a stored
  `ConversionsDaily` row where `Transactions == 7` and `PurchaseRevenue == 123.45m` — not
  transposed.
- `Date` and `ChannelGroup` are asserted against the same fixture row to confirm dimension
  ordering (`DimensionValues[0]` → `Date`, `DimensionValues[1]` → `ChannelGroup`).

### FR-4: Upsert key and update-lambda behaviour
The composite key is `(Date, ChannelGroup)`; a fresh row inserts, a matching key updates in place
without duplicating.

**Acceptance criteria:**
- **Insert case:** syncing into an empty DB window creates exactly one new `ConversionsDaily` row
  per distinct `(Date, ChannelGroup)` pair in the report.
- **Update case:** a pre-existing `ConversionsDaily` row for `(Date, ChannelGroup)` is present
  before a second `SyncAsync()` run with revised `Transactions`/`PurchaseRevenue` for the same
  key; after the run, there is still exactly one row for that key (no duplicate), and its
  `Transactions`/`PurchaseRevenue`/`SyncedAt` reflect the new values, not the old ones.
- Two rows sharing the same `Date` but different `ChannelGroup` values are both kept (proves the
  key is composite, not `Date` alone).

### FR-5: `ChunkOutcome` tuple construction
The outcome returned from `SyncChunkAsync` (surfaced via `Ga4SyncResult` from the public
`SyncAsync()`) reflects `(report.Rows.Count, upserted)` — fetched is the raw count, upserted is
what `Ga4ChunkUpsert.ReplaceRangeAsync` reports back.

**Acceptance criteria:**
- Covered by the assertions in FR-2 (fetched vs. upserted diverge under filtering) and FR-4
  (upserted count matches distinct keys written, not raw row count, when duplicate keys appear
  within one chunk — GA4 should not do this in practice, but the dedupe behavior in
  `Ga4ChunkUpsert.ReplaceRangeAsync` already exists and is currently exercised only for other
  tables).

## Non-Functional Requirements

### NFR-1: Test isolation and determinism
Tests use the EF Core in-memory provider via `Ga4TestHarness.NewDbContext`, a fresh
`NewDatabaseName()` per test where state is asserted after the fact (per the existing
`Ga4TestHarness.StoredStateAsync` convention — never assert through the same context instance the
service under test wrote through), and `FixedTimeProvider` for deterministic `SyncedAt`/watermark
timestamps. No network calls, no wall-clock dependency, no test ordering dependency.

### NFR-2: No production code changes
This is a coverage-only task. `ConversionsSyncService.cs`, `Ga4ChunkUpsert.cs`,
`Ga4ValueParser.cs`, and `Ga4EntitySyncServiceBase.cs` are not modified. If, during
implementation, a genuine bug is discovered (e.g. an actual column-order defect), that is out of
scope for this issue and must be filed separately rather than silently fixed alongside the tests.

## Data Model
No schema changes. Tests operate against the existing `ConversionsDaily` entity
(`Date`, `ChannelGroup`, `Transactions`, `PurchaseRevenue`, `SyncedAt`) and `SyncState`
(read via `Ga4TestHarness.StoredStateAsync` where a test wants to assert `RowsFetched`/
`RowsUpserted` bookkeeping, though the primary assertions here go through the `Ga4SyncResult`
returned directly from `SyncAsync()`).

## API / Interface Design
No public interface changes. All new code is test-only:
- One new test file, e.g.
  `backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/ConversionsSyncServiceTests.cs`,
  following the existing `BackfillChunkingTests.cs` / `FailedWriteIsolationTests.cs` style
  (arrange via `Ga4TestHarness`, `FakeGa4ReportClient`, `FluentAssertions`).
- Additive helpers in `Ga4TestHarness.cs` (FR-1).

## Dependencies
- Existing `Ga4TestHarness`, `FakeGa4ReportClient`, `FixedTimeProvider` (all in
  `SyncOrchestrationTests.cs`/`Ga4TestHarness.cs` today — no new test infrastructure project).
- `FluentAssertions`, `xUnit` — already used throughout this test project.
- No new NuGet packages.

## Out of Scope
- Any change to production sync logic, mapping, or upsert semantics.
- Coverage for the other five GA4 entity sync services (`TrafficSyncService`,
  `TrafficMonthlySyncService`, `TrafficSyncService` variants, `LandingPageSyncService`,
  `PageSyncService`) beyond what they already have — each already has some coverage via
  `BackfillChunkingTests.cs`/`TrailingReprocessWindowTests.cs`.
- Integration-level testing against the real Google Analytics Data API — `IGa4ReportClient` stays
  faked, per existing convention in this test project.
- Raising or changing the 60% coverage filter threshold itself.

## Open Questions

None.

## Status: COMPLETE
