# Specification: Unit tests for PackingStatsTile failure isolation

## Summary
`PackingStatsTile.LoadDataAsync` has 0% line coverage. It contains two distinct
failure paths — a graceful-degradation inner catch around the Shoptet order-count
calls, and a total-failure outer catch around the whole load — that produce
different response shapes and are never exercised by tests. This spec defines a
small unit-test suite that pins down both paths plus the happy path, protecting
the dashboard's API contract against refactors.

## Background
`PackingStatsTile` (`backend/src/Anela.Heblo.Application/Features/Packaging/DashboardTiles/PackingStatsTile.cs`)
implements `ITile`. `LoadDataAsync` does two things:

1. Loads today's packer breakdown from `IPackageRepository.GetPackedTodayByPackerAsync`
   (returns `(int TotalDistinctOrders, IReadOnlyList<PackerPackingSummary> ByPacker)`).
2. Enriches the response with live Shoptet counts from
   `IPackingOrderClient.GetOrdersBeingPackedCountAsync` /
   `GetOrdersBeingProcessedCountAsync`, wrapped in an **inner** try/catch.

The inner catch is deliberate graceful degradation: if Shoptet is unreachable the
tile still returns `status: "success"` with the packer breakdown and leaves the
order counts `null`. Only a failure of the repository call (outer catch) turns the
tile into `status: "error"`. The weekly coverage-gap routine flagged that nothing
verifies these two paths produce different shapes, or that a Shoptet failure is
truly isolated from the packer data. If a refactor promoted the Shoptet exception
to the outer scope, `ordersBeingPackedCount` would flip from `null` to an error
response — a silent breaking change for the dashboard client.

This is a test-only change. **No production code is modified.**

## Functional Requirements

### FR-1: Happy path returns success with counts and packer breakdown
Covers the mainline where both the repository and the Shoptet client succeed.
Establishes the baseline shape the failure-path tests contrast against.

**Acceptance criteria:**
- `GetPackedTodayByPackerAsync` returns a non-empty `ByPacker` list and a total.
- Shoptet client returns concrete counts (e.g. packed = 4, processed = 9).
- Response `status` is `"success"`.
- `data.ordersBeingPackedCount` and `data.ordersBeingProcessedCount` equal the
  values the client returned.
- `data.ordersBeingPackedCountLastSync` is non-null (set to `now`).
- `data.totalOrdersPackedToday` equals the repository total.
- `data.packedByPacker` has one entry per `PackerPackingSummary`, mapping
  `packerId`, `packerName`, and `orderCount` correctly.

### FR-2: Shoptet failure is isolated (graceful degradation)
This is the primary gap from the brief. Verifies the inner catch keeps the tile
successful and only nulls the Shoptet-derived fields.

**Acceptance criteria:**
- `GetPackedTodayByPackerAsync` returns valid packer data.
- `GetOrdersBeingPackedCountAsync` throws (any exception).
- Response `status` is `"success"`.
- `data.ordersBeingPackedCount` is `null`.
- `data.ordersBeingProcessedCount` is `null`.
- `data.ordersBeingPackedCountLastSync` is `null`.
- `data.packedByPacker` is still fully populated and `totalOrdersPackedToday`
  still reflects the repository total (proving isolation).
- The exception does not propagate out of `LoadDataAsync`.

### FR-3: `packedName` null-coalescing fallback
`PackerPackingSummary.PackedBy` is nullable; the tile maps a null name to
`"Neznámý"`. Cheap to assert while the mocks are already set up.

**Acceptance criteria:**
- Given a `PackerPackingSummary` with `PackedBy = null`, the corresponding
  `packedByPacker[i].packerName` is `"Neznámý"`.

### FR-4: Repository failure returns error response
Covers the outer catch — total failure, not degradation.

**Acceptance criteria:**
- `GetPackedTodayByPackerAsync` throws (any exception).
- Response `status` is `"error"`.
- Response contains `error` = `"Nepodařilo se načíst data balení"`.
- No `data` payload is required/expected on this shape.
- The exception does not propagate out of `LoadDataAsync`.

## Non-Functional Requirements

### NFR-1: Performance
Pure in-memory unit tests with mocked dependencies. No I/O, no database, no
network. Each test should complete in milliseconds.

### NFR-2: Security
None. No auth, secrets, or PII involved. `RequiredPermissions` is empty and out
of scope for these tests.

### NFR-3: Determinism
Time must be deterministic. Use `Microsoft.Extensions.Time.Testing.FakeTimeProvider`
(or an equivalent fixed `TimeProvider`) so `GetLocalNow()` is fixed, making the
`start`/`end` window and `lastSync`/`lastUpdated` assertions stable. Since
`LoadDataAsync` returns an anonymous `object`, assert against it by serializing to
JSON (`System.Text.Json`) and reading properties from the `JsonDocument`, matching
the existing convention in `FailedJobsTileTests`.

## Data Model
No schema changes. Relevant existing types (read-only for the tests):
- `PackerPackingSummary(Guid? PackedByUserId, string? PackedBy, int DistinctOrderCount)`
- Repository tuple: `(int TotalDistinctOrders, IReadOnlyList<PackerPackingSummary> ByPacker)`
- Tile response (anonymous object): `{ status, data: { ordersBeingPackedCount,
  ordersBeingProcessedCount, ordersBeingPackedCountLastSync, totalOrdersPackedToday,
  packedByPacker[] }, metadata, drillDown }` on success; `{ status, error }` on error.

## API / Interface Design
No production interface changes. New test file only:

- **File:** `backend/test/Anela.Heblo.Tests/Features/Packaging/DashboardTiles/PackingStatsTileTests.cs`
- **Namespace:** `Anela.Heblo.Tests.Features.Packaging.DashboardTiles`
- **SUT constructor:** `new PackingStatsTile(IPackageRepository, IPackingOrderClient, TimeProvider, ILogger<PackingStatsTile>)`
- **Mocks:** `Mock<IPackageRepository>`, `Mock<IPackingOrderClient>`; logger via
  `NullLogger<PackingStatsTile>.Instance`; `TimeProvider` via `FakeTimeProvider`.
- **Framework:** xUnit + Moq + FluentAssertions, matching sibling tile tests.
- Suggested test methods:
  - `LoadDataAsync_AllDependenciesSucceed_ReturnsSuccessWithCountsAndPackers` (FR-1)
  - `LoadDataAsync_ShoptetClientThrows_ReturnsSuccessWithNullCountsAndPackersPopulated` (FR-2)
  - `LoadDataAsync_NullPackerName_MapsToNeznamy` (FR-3, may fold into FR-1)
  - `LoadDataAsync_RepositoryThrows_ReturnsError` (FR-4)

## Dependencies
- xUnit, Moq, FluentAssertions (already used across `Anela.Heblo.Tests`).
- `Microsoft.Extensions.Time.Testing` for `FakeTimeProvider` — already referenced
  and used by `Anela.Heblo.Tests` (e.g. the sibling `GetPackingDashboardHandlerTests`,
  which shares the same dependency set). No new package needed.

## Out of Scope
- Any change to `PackingStatsTile` production code.
- Testing the partial-failure sub-case where `GetOrdersBeingPackedCountAsync`
  succeeds but `GetOrdersBeingProcessedCountAsync` throws (the brief calls for two
  primary paths; may be added opportunistically but is not required).
- `metadata` and `drillDown` block assertions beyond what FR-1 needs (these are
  static and low-risk).
- Integration/E2E tests; DI registration; the parallel `GetPackingDashboardHandler`
  which shares logic but is a separate unit.
- Tile property assertions (`Title`, `Size`, `DefaultEnabled`, etc.).

## Open Questions
None.

## Status: COMPLETE
