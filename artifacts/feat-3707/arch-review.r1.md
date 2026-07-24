# Architecture Review: Unit tests for PackingStatsTile failure isolation

## Skip Design: true
Pure backend test addition. No production code, no UI components, no visual or layout
decisions. A single new xUnit test file exercising an existing `ITile` implementation.

## Architectural Fit Assessment

The feature aligns cleanly with existing conventions — this is a test-only change filling
a 0% coverage gap on `PackingStatsTile.LoadDataAsync`. The codebase already contains three
directly analogous test files that establish every pattern this task needs:

- **`FailedJobsTileTests`** (`Features/BackgroundJobs/DashboardTiles/`) — the canonical
  dashboard-tile test. Establishes the "serialize the anonymous `object` to JSON and read
  properties from `JsonDocument`" convention that the spec correctly names. Uses
  `NullLogger<T>.Instance` and `Mock<T>` for dependencies.
- **`GetPackingDashboardHandlerTests`** (`Features/Packaging/`) — the closest analogue by
  dependency shape. It mocks the *identical* dependency set the tile takes
  (`IPackageRepository` + `IPackingOrderClient` + `TimeProvider` + logger) and already
  covers the same three logical paths (happy, Shoptet-throws, null-packer-name) against the
  sibling handler that shares the tile's business logic.
- **`GetPackingStatisticsHandlerTests`** (`Features/Packaging/`) — reinforces the
  `TimeProvider` handling pattern.

Integration points: the SUT is instantiated directly (no DI container), all four
constructor dependencies are mocked/faked, and there is no I/O. The test file will live
under `Anela.Heblo.Tests`, which already references every needed package (xUnit, Moq,
FluentAssertions, `Microsoft.Extensions.TimeProvider.Testing`). No project or package
changes are required.

**One material discrepancy with the spec** (see Specification Amendments): the spec's
NFR-3 prescribes `Microsoft.Extensions.Time.Testing.FakeTimeProvider`, but the two nearest
siblings that use this exact dependency set deliberately roll their own
`FakeTimeProvider : TimeProvider` subclass. That choice is not arbitrary and should be
honored.

## Proposed Architecture

### Component Overview

```
PackingStatsTileTests  (new, sealed test class)
    ├── Mock<IPackageRepository>      → GetPackedTodayByPackerAsync(start, end, ct)
    │                                    returns (int TotalDistinctOrders,
    │                                             IReadOnlyList<PackerPackingSummary> ByPacker)
    ├── Mock<IPackingOrderClient>     → GetOrdersBeingPackedCountAsync(ct)     : Task<int>
    │                                    GetOrdersBeingProcessedCountAsync(ct) : Task<int>
    ├── TimeProvider (fake, fixed)    → GetLocalNow()  [drives start/end window + lastSync]
    └── NullLogger<PackingStatsTile>.Instance
                │
                ▼
        new PackingStatsTile(repo, client, time, logger)
                │  LoadDataAsync()
                ▼
        object (anonymous) ──serialize──▶ JsonDocument ──assert──▶ FluentAssertions
```

### Key Design Decisions

#### Decision 1: Assert against serialized JSON, not the anonymous object
**Options considered:** (a) reflection over the anonymous type's properties; (b) cast to
`dynamic`; (c) serialize with `System.Text.Json` and read `JsonDocument`.
**Chosen approach:** (c) — serialize and read from `JsonDocument`, exactly as
`FailedJobsTileTests.ToJsonDoc` does.
**Rationale:** `LoadDataAsync` returns `Task<object>` wrapping an anonymous type, so there
is no compile-time contract to bind to. Serializing to JSON asserts against the *wire shape
the dashboard client actually consumes*, which is precisely the contract the brief says
must be protected against refactors. Copy the `ToJsonDoc` helper verbatim.

#### Decision 2: Fake time via a `TimeProvider` subclass, not the framework `FakeTimeProvider`
**Options considered:** (a) `Microsoft.Extensions.Time.Testing.FakeTimeProvider` (spec's
recommendation); (b) the nested `private sealed class FakeTimeProvider : TimeProvider`
subclass used by both Packaging sibling tests.
**Chosen approach:** (b) — reuse the subclass pattern from `GetPackingDashboardHandlerTests`.
**Rationale:** The tile calls `_timeProvider.GetLocalNow()`, whose result depends on both
`GetUtcNow()` and `LocalTimeZone`. The framework `FakeTimeProvider` defaults `LocalTimeZone`
to UTC (offset zero) unless `SetLocalTimeZone` is called; the sibling tests exist to pin a
concrete non-UTC local offset (Prague +02:00) deterministically. Using the identical
subclass keeps the file consistent with the two nearest tests and avoids a subtle
timezone-default footgun. Framework `FakeTimeProvider` would still *pass* the spec's
assertions (none of which check the exact window), so (a) is acceptable — but (b) is the
lower-risk, convention-matching choice for a file sitting beside those two.

#### Decision 3: Fold FR-3 (null-name → "Neznámý") into the FR-1 happy-path test
**Options considered:** (a) a dedicated `LoadDataAsync_NullPackerName_MapsToNeznamy` test;
(b) include one `PackedBy = null` entry in the FR-1 `ByPacker` list and assert the mapping
there.
**Chosen approach:** (b), matching how `GetPackingDashboardHandlerTests` keeps a separate
tiny test but the spec explicitly permits folding.
**Rationale:** The mocks are already configured in FR-1; adding one null-name packer costs
one list entry and one assertion, avoids duplicate setup, and still pins the fallback. A
separate test is equally acceptable if the author prefers one-assertion-per-test. Either
satisfies the spec.

## Implementation Guidance

### Directory / Module Structure
Create exactly one file, mirroring the source namespace under the test project:

```
backend/test/Anela.Heblo.Tests/Features/Packaging/DashboardTiles/PackingStatsTileTests.cs
```

Namespace: `Anela.Heblo.Tests.Features.Packaging.DashboardTiles`
(The `DashboardTiles/` folder does not yet exist under `Features/Packaging/` in the test
project — create it. This matches the source layout and the `BackgroundJobs/DashboardTiles/`
precedent.)

No changes to any `.csproj`, no new packages, no production files.

### Interfaces and Contracts
Developers must honor these existing signatures (verified in source):

- **SUT constructor:**
  `PackingStatsTile(IPackageRepository repo, IPackingOrderClient packingOrderClient, TimeProvider timeProvider, ILogger<PackingStatsTile> logger)`
- **`IPackageRepository.GetPackedTodayByPackerAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)`**
  → `Task<(int TotalDistinctOrders, IReadOnlyList<PackerPackingSummary> ByPacker)>`
- **`IPackingOrderClient.GetOrdersBeingPackedCountAsync(CancellationToken ct)`** → `Task<int>`
- **`IPackingOrderClient.GetOrdersBeingProcessedCountAsync(CancellationToken ct)`** → `Task<int>`
- **`PackerPackingSummary(Guid? PackedByUserId, string? PackedBy, int DistinctOrderCount)`**
  — positional record; construct as `new(guid, "Alice", 4)`.

Serialized success shape (property names come from the anonymous object, so JSON keys are
verbatim): `status`, `data.{ordersBeingPackedCount, ordersBeingProcessedCount,
ordersBeingPackedCountLastSync, totalOrdersPackedToday, packedByPacker[].{packerId,
packerName, orderCount}}`, `metadata.{lastUpdated, source}`, `drillDown.{filters, enabled,
tooltip}`.

Serialized error shape: `status = "error"`, `error = "Nepodařilo se načíst data balení"`.
**Note:** the error object contains **no `data` property at all** — it is omitted, not set
to null (this differs from `FailedJobsTile`, which returns `data = null`). Assert with
`root.TryGetProperty("data", out _).Should().BeFalse()` — do **not** copy
`FailedJobsTileTests`' `data` ValueKind.Null assertion.

### Data Flow
1. **Happy (FR-1/FR-3):** repo mock returns `(6, [ (guid,"Alice",4), (null,null,1) ])`;
   client mock returns packed=4, processed=9. `LoadDataAsync` computes the window from the
   fake `GetLocalNow()`, reads both counts, sets `lastSync = now`, maps packers
   (null name → `"Neznámý"`), returns `status:"success"`. Assert counts equal the mocked
   values, `ordersBeingPackedCountLastSync` is a non-null JSON string,
   `totalOrdersPackedToday == 6`, one `packedByPacker` entry per summary with correct
   `packerId`/`packerName`/`orderCount`, and the null-name entry maps to `"Neznámý"`.
2. **Shoptet isolation (FR-2):** repo mock returns valid packer data; client's
   `GetOrdersBeingPackedCountAsync` set to `.ThrowsAsync(new HttpRequestException(...))`.
   Inner catch swallows it → `status:"success"`, all three Shoptet-derived fields
   (`ordersBeingPackedCount`, `ordersBeingProcessedCount`, `ordersBeingPackedCountLastSync`)
   serialize as JSON null (`ValueKind == Null`), while `packedByPacker` and
   `totalOrdersPackedToday` remain fully populated — proving isolation. Assert no exception
   escapes (`await tile.LoadDataAsync()` simply returns).
3. **Repository failure (FR-4):** repo mock `.ThrowsAsync(...)`. Outer catch →
   `status:"error"`, `error` message equals the Czech string, no `data` property, no
   exception propagates.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Author uses framework `FakeTimeProvider` with default UTC zone, then later adds a window/offset assertion that breaks | Low | Reuse the sibling `FakeTimeProvider : TimeProvider` subclass; spec asserts no exact window so current tests are safe either way |
| Copying `FailedJobsTileTests`' `data == Null` assertion into FR-4, which fails because the tile omits `data` entirely | Medium | Assert `TryGetProperty("data", ...).Should().BeFalse()` instead (documented above) |
| Serialization of `DateTimeOffset?` / `Guid?` asserted with the wrong `JsonValueKind` | Low | Non-null `lastSync`/`packerId` serialize as `String`; null serializes as `Null` — assert accordingly |
| `partial-failure` sub-case (packed succeeds, processed throws) left uncovered | Low | Explicitly out of scope per spec; may be added opportunistically as a 5th test |
| New `DashboardTiles/` test folder not picked up | Very Low | xUnit discovers by assembly, not path; folder is organizational only |

## Specification Amendments
1. **NFR-3 / API design — TimeProvider choice:** Change the recommendation from
   `Microsoft.Extensions.Time.Testing.FakeTimeProvider` to reusing the nested
   `private sealed class FakeTimeProvider : TimeProvider` subclass from
   `GetPackingDashboardHandlerTests` (same folder, same dependency set). The framework type
   is acceptable but defaults `LocalTimeZone` to UTC, diverging from the established
   Packaging-tests pattern. This is a consistency amendment, not a correctness blocker.
2. **FR-4 acceptance criteria — clarify the error shape:** Add that the error object omits
   the `data` property entirely (it is not present as null). The assertion must be
   "`data` property is absent", not "`data` is null". This distinguishes `PackingStatsTile`
   from `FailedJobsTile` and prevents a copy-paste failure.
3. **File location — note new folder:** `Features/Packaging/DashboardTiles/` does not yet
   exist in the test project and must be created (source-mirroring, matching the
   `BackgroundJobs/DashboardTiles/` precedent).

## Prerequisites
None. All packages (`xunit`, `Moq`, `FluentAssertions`,
`Microsoft.Extensions.TimeProvider.Testing`) are already referenced in
`Anela.Heblo.Tests.csproj`. No migrations, config, DI registration, or infrastructure
changes. Implementation can start immediately; validate with `dotnet build` +
`dotnet format` + `dotnet test` filtered to the new class.
