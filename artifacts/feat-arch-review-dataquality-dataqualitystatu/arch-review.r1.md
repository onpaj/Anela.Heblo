# Architecture Review: Add Structured Error Logging to DataQualityStatusTile

## Skip Design: true

Backend-only behavioral fix. No UI, no new visual components, no design decisions required.

## Architectural Fit Assessment

The change is a perfect fit. It does not introduce a new pattern — it adopts the one already in use by the sibling class `DqtYesterdayStatusTile` in the same folder. The DataQuality module follows Vertical Slice organization (`Application/Features/DataQuality/DashboardTiles/`), and both tiles implement the `ITile` interface from `Anela.Heblo.Xcc.Services.Dashboard`. Registration goes through `DataQualityModule.AddDataQualityModule()` via `services.RegisterTile<DataQualityStatusTile>()`, which is the standard tile registration helper.

Integration points:
- **DI container** — `ILogger<T>` is wired by default through `Microsoft.Extensions.Logging`; no module change needed. Tile registration via `RegisterTile<T>()` already resolves all constructor dependencies through the container.
- **Existing tests** — `DataQualityStatusTileTests` (3 tests) constructs the tile directly and must be updated to supply a logger. The sibling test uses `NullLogger<T>.Instance`; the same approach applies here.
- **No transitive ripple** — the class is not constructed manually anywhere else (search confirms only the module registration and test construct it).

## Proposed Architecture

### Component Overview

```
DI container
   │
   ├── ILogger<DataQualityStatusTile>  (Microsoft.Extensions.Logging — default)
   ├── IDqtRunRepository               (registered in DataQualityModule)
   │
   ▼
DataQualityStatusTile (ITile)
   │
   └── LoadDataAsync()
         try → repository call → shape payload
         catch (Exception ex)
              ↓
         _logger.LogError(ex, "...")  ← NEW
              ↓
         return degraded payload (unchanged shape)
```

### Key Design Decisions

#### Decision 1: Log message phrasing — fixed string vs. structured properties
**Options considered:**
- (A) Spec proposal: `_logger.LogError(ex, "Failed to load DataQuality status tile")` — fixed message, exception only.
- (B) Mirror sibling exactly: include the test type as a structured property, e.g. `_logger.LogError(ex, "Failed to load DataQuality status tile for {TestType}", DqtTestType.IssuedInvoiceComparison)`.

**Chosen approach:** Option B — include `{TestType}` as a structured property.

**Rationale:** NFR-3 ("match conventions used by `DqtYesterdayStatusTile`") and the sibling's actual implementation use structured properties (`{TestType}`, `{TargetDate}`). The fixed-string variant in the spec drifts from that pattern. Adding `{TestType}` is zero-PII, costs nothing on the happy path, and makes log aggregation/filtering meaningful when more test types come online. (There is no `TargetDate` analogue here since this tile reads the latest run regardless of date — so the message ends at `{TestType}`.)

#### Decision 2: Constructor parameter order
**Options considered:**
- (A) Append `logger` last: `(IDqtRunRepository repository, ILogger<DataQualityStatusTile> logger)`.
- (B) Prepend or insert mid-list.

**Chosen approach:** Option A — append last.

**Rationale:** Sibling tile orders dependencies as `(repository, timeProvider, logger)` — logger last. Matches the sibling convention and minimizes call-site churn (the only direct call site is the test class).

#### Decision 3: Test logger — mock vs. NullLogger
**Options considered:**
- (A) `NullLogger<DataQualityStatusTile>.Instance` for existing tests; add one new test using `Mock<ILogger<T>>` to verify `LogError` invocation (NFR-4).
- (B) Switch all tests to `Mock<ILogger<T>>`.

**Chosen approach:** Option A.

**Rationale:** Sibling test class uses `NullLogger<T>.Instance` everywhere — no test there verifies `LogError` was called. To satisfy NFR-4 without diverging stylistically, keep the existing three tests on `NullLogger` and add one new test (the rethrow-path test, or a fourth dedicated one) that uses `Mock<ILogger<DataQualityStatusTile>>` with `Verify(...)` on `LogError`. This is a small, targeted addition rather than a wholesale rewrite. As a follow-up consideration, the sibling tile *should* probably get the same verification test — but that is out of scope per the spec.

## Implementation Guidance

### Directory / Module Structure

No new files. Edits in:
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs` — add `using Microsoft.Extensions.Logging;`, add `_logger` field, update constructor, update catch block.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs` — update constructor call to pass `NullLogger<DataQualityStatusTile>.Instance`; add one test verifying `LogError` is invoked on the exception path.

No changes to `DataQualityModule.cs` — the `RegisterTile<T>()` helper resolves all constructor params through the container, and `ILogger<T>` is provided by the host's default logging setup.

### Interfaces and Contracts

- `ITile` interface — unchanged.
- Public method signature `LoadDataAsync(Dictionary<string, string>?, CancellationToken)` — unchanged.
- Returned anonymous-object shape (`status`, `data`, `drillDown`) — unchanged on all branches.
- Constructor: `DataQualityStatusTile(IDqtRunRepository repository, ILogger<DataQualityStatusTile> logger)`.

### Data Flow

Happy path: unchanged. Exception path: `repository throws → catch (Exception ex) → _logger.LogError(ex, "Failed to load DataQuality status tile for {TestType}", DqtTestType.IssuedInvoiceComparison) → return degraded payload`. No state mutation, no propagation, no shape change.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing `DataQualityStatusTileTests` (3 tests) fail to compile after constructor signature change | Low | Update test fixture constructor to pass `NullLogger<DataQualityStatusTile>.Instance` (same pattern as sibling test). Validated by `dotnet build` + `dotnet test`. |
| DI fails to resolve `ILogger<DataQualityStatusTile>` at startup | Negligible | Default ASP.NET Core host registers `ILogger<T>` automatically; sibling tile already relies on this with no special wiring. Confirm by running the app or an integration test that resolves the tile from the container. |
| Log message inconsistency with sibling (different phrasing/properties) confuses log search | Low–Medium | Decision 1 above — match sibling's structured-property style. Use `{TestType}` placeholder. |
| Future maintainers grep for "Failed to load yesterday DQT status" looking for both tiles | Low | Use distinctive phrasing per tile ("Failed to load DataQuality status tile" here vs. "Failed to load yesterday DQT status" in sibling) — already what the spec proposes. Each line is independently searchable. |

## Specification Amendments

1. **FR-2 log message** — change from the fixed-string version to one with a structured `{TestType}` property, to align with the sibling tile's actual pattern (NFR-3):
   ```csharp
   _logger.LogError(ex, "Failed to load DataQuality status tile for {TestType}", DqtTestType.IssuedInvoiceComparison);
   ```
   Sibling tile uses two properties (`{TestType}`, `{TargetDate}`); this tile has no date dimension, so only `{TestType}` applies.

2. **NFR-4 testability — concrete instruction** — keep the three existing tests on `NullLogger<DataQualityStatusTile>.Instance` (mirroring sibling), and **add one new test** that constructs the tile with a `Mock<ILogger<DataQualityStatusTile>>`, triggers the catch path, and asserts `LogError` was invoked exactly once with the expected exception. The existing `LoadDataAsync_RepositoryThrows_ReturnsErrorWithRouteKey` test stays focused on return-shape; the new test focuses on log invocation. Two narrow tests beat one wide test.

3. **API / Interface Design — DI registration note** — clarify that `RegisterTile<DataQualityStatusTile>()` in `DataQualityModule.cs` requires no edit. The current spec says "if registered explicitly … confirm it still resolves" — it *is* registered explicitly, but `RegisterTile<T>()` instantiates via DI, so the new logger parameter is satisfied automatically.

## Prerequisites

None. No migrations, no config changes, no infrastructure. The `Microsoft.Extensions.Logging` package is already a transitive dependency (used by the sibling tile in the same assembly), and `ILogger<T>` is provided by the host's default DI registration. Implementation can start immediately.