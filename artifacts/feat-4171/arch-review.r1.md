# Architecture Review: Unit Test Coverage for TransportBoxBaseTile Drill-Down Filters

## Skip Design: true
This is a backend-only, test-only coverage-gap fix with no new or changed UI, endpoints, or visual components. No design-phase work is needed; the designer agent should record a minimal "no UI component" design document and pass through.

## Architectural Fit Assessment
`TransportBoxBaseTile` sits in `Anela.Heblo.Application/Features/Logistics/DashboardTiles/`, alongside three concrete subclasses already in the codebase: `ErrorBoxesTile`, `InTransitBoxesTile`, `ReceivedBoxesTile` (confirmed by grep — no other subclasses exist). Each concrete tile only overrides `Title`, `Description`, `FilterStates`, and forwards `ITransportBoxRepository` to the base constructor; all executable logic under test (`GenerateDrillDownFilters`, `LoadDataAsync`) lives in the abstract base class itself.

This matches an established, already-tested pattern elsewhere in the codebase: `InventorySummaryTileBaseTests` (`test/Anela.Heblo.Tests/Features/Catalog/DashboardTiles/InventorySummaryTileBaseTests.cs`) tests an abstract tile base class via one of its concrete subclasses (`ProductInventorySummaryTile`), mocking the repository dependency with **Moq**, and asserting on the `LoadDataAsync` anonymous-type result by round-tripping it through `System.Text.Json.JsonSerializer.Serialize` → `JsonDocument.Parse` and reading properties off the `JsonElement` tree. This is the correct precedent to follow — it solves exactly the "anonymous return type" assertion problem this spec's Data Model section flagged as open, and it needs no production code change.

No test file currently exists for `Anela.Heblo.Application.Features.Logistics.DashboardTiles` (grep against `test/` confirms no `Logistics/DashboardTiles` folder in the test project). This is a new coverage area, not an extension of an existing tile-specific test file.

## Proposed Architecture

### Component Overview
```
test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/
  └── TransportBoxBaseTileTests.cs   (new file — all FR-1..FR-6 tests)
```
No production code changes. No new interfaces. The test file uses one of the existing concrete subclasses (`ErrorBoxesTile`) directly, driven with different `TransportBoxState[]` compositions supplied indirectly, plus a small number of purpose-built test doubles only where a concrete subclass's fixed `FilterStates` can't express the required test data (see Decision 1).

### Key Design Decisions

#### Decision 1: How to control `FilterStates` per test case
**Options considered:**
- (a) Reuse only the existing concrete subclasses (`ErrorBoxesTile`, `InTransitBoxesTile`, `ReceivedBoxesTile`) as-is. Rejected: none of them has a multi-state `FilterStates`, so FR-2/FR-3 (which require 2+ states, one variant with `Closed` present and one without) cannot be expressed through any existing subclass — grep confirms each concrete tile hardcodes a single-element `FilterStates`.
- (b) Add a `FilterStates` setter or constructor parameter to `TransportBoxBaseTile` to make it directly configurable. Rejected: `NFR-1` (no production behavior change) — production code has no need for a mutable `FilterStates`, and it would be a real widening of the class's public/protected surface for test-only benefit.
- (c) Define one minimal test-only concrete subclass in the test project, e.g. `TestTransportBoxTile : TransportBoxBaseTile`, whose constructor takes `ITransportBoxRepository repository` and `TransportBoxState[] filterStates` and exposes `filterStates` through the overridden `FilterStates` property. **Chosen.**

**Rationale:** (c) is the only option that can express all four `GenerateDrillDownFilters` branches (0, 1, 2+ non-Closed, 2+ with Closed) without touching production code. It mirrors how `InventorySummaryTileBaseTests` reuses a concrete subclass, except a slightly more parameterizable one is needed here specifically because the branch logic under test is driven by the *shape* of `FilterStates`, not by data returned from the repository. `TileIdContractTests` (`test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs`) only reflects over the **production** assemblies (`Anela.Heblo.Xcc`, `Anela.Heblo.Application`) — confirmed by reading its `ProductionAssemblies` array — so a concrete `ITile` subclass defined in the test assembly is invisible to that contract test and needs no `[TileId(...)]` attribute, no registration, and no dashboard-registry changes.

`Title` and `Description` on the test double can return any fixed non-null string (e.g. `"Test Tile"`); `LoadDataAsync`'s tooltip interpolation (`Title.ToLower()`) means `Title` must be non-null but its exact value is irrelevant to every FR in the spec.

#### Decision 2: How to assert against the anonymous-typed return values
**Options considered:**
- (a) Reflection over the anonymous type's properties directly.
- (b) Serialize with `System.Text.Json.JsonSerializer.Serialize`, parse with `JsonDocument.Parse`, and read properties off `JsonElement`. **Chosen** — this is the exact pattern `InventorySummaryTileBaseTests` already uses for the same "abstract dashboard tile base class returning an anonymous type" situation, so following it keeps this new test file idiomatically consistent with the nearest existing precedent rather than introducing a second convention.
- (c) `dynamic` casting. Rejected — more fragile with anonymous types across assembly boundaries and not the pattern already in use.

**Rationale:** Consistency with the one directly analogous test file in the codebase; no new technique introduced.

#### Decision 3: Mocking library and test framework
**Chosen:** xUnit (`[Fact]`), **Moq** for `Mock<ITransportBoxRepository>`, **FluentAssertions** for assertions (`.Should().Be(...)`) — all already referenced by `Anela.Heblo.Tests.csproj` and used throughout the `DashboardTiles` test folders (see `InventorySummaryTileBaseTests`, `PackingStatsTileTests`, etc.). `NSubstitute` is also referenced in the project but is not the convention used in the `DashboardTiles` test folders specifically — Moq is used there, so Moq is the correct choice for consistency with the immediate neighborhood, per NFR-3.

For FR-4 (empty-object fallback assertion), assert on the anonymous type via `JsonDocument`: `doc.RootElement.EnumerateObject()` should be empty (no properties) rather than checking for `TryGetProperty("state", ...)` alone, since an empty anonymous object (`new { }`) serializes to `{}` — this is the most direct structural check for "no members."

For FR-1–FR-4, call `GenerateDrillDownFilters()` directly (it is `protected virtual`, so the test double subclass can expose it, e.g. via a public wrapper method `public object CallGenerateDrillDownFilters() => GenerateDrillDownFilters();`) rather than going through `LoadDataAsync` and a mocked repository for every branch — this isolates the branch logic under test from repository-mock noise per FR-1..FR-4's acceptance criteria, and is cheaper to write. FR-5/FR-6 (`LoadDataAsync` success/error shape) do need the repository mocked since they exercise the method that calls it.

## Implementation Guidance

### Directory / Module Structure
- New file: `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs`
- Namespace: `Anela.Heblo.Tests.Features.Logistics.DashboardTiles` (matches the folder-mirrors-namespace convention visible in every other `DashboardTiles` test file).
- No other files change.

### Interfaces and Contracts
Test-only helper type, defined at the bottom of the same test file (private nested class or a private top-level class in the same namespace — match whatever style `InventorySummaryTileBaseTests`-adjacent files use; a private nested class is preferred to avoid polluting the test project namespace):

```csharp
private sealed class TestTransportBoxTile : TransportBoxBaseTile
{
    public override string Title => "Test Tile";
    public override string Description => "Test Tile Description";

    private readonly TransportBoxState[] _filterStates;
    protected override TransportBoxState[] FilterStates => _filterStates;

    public TestTransportBoxTile(ITransportBoxRepository repository, TransportBoxState[] filterStates)
        : base(repository)
    {
        _filterStates = filterStates;
    }

    public object CallGenerateDrillDownFilters() => GenerateDrillDownFilters();
}
```
No production interfaces change. `ITransportBoxRepository.FindAsync(Expression<Func<TransportBox,bool>>, bool includeDetails = false, CancellationToken cancellationToken = default)` is the method to mock for FR-5/FR-6 — use `It.IsAny<Expression<Func<TransportBox, bool>>>()` for the predicate argument (the predicate closes over `FilterStates`, which is awkward to match structurally and isn't what these tests are verifying) and `It.IsAny<bool>()`/`It.IsAny<CancellationToken>()` for the rest.

### Data Flow
- FR-1–FR-4: construct `TestTransportBoxTile` with a mocked (never-invoked) `ITransportBoxRepository`, call `CallGenerateDrillDownFilters()` directly, assert via `JsonDocument`.
- FR-5: mock `FindAsync` to return a known `IEnumerable<TransportBox>`, call `LoadDataAsync()`, assert `status`/`data.count` via `JsonDocument`. Constructing a real `TransportBox` only needs whatever minimal fields the entity requires to exist in a list — the repository mock never actually filters by predicate, so box `State` values don't need to match `FilterStates` for this test.
- FR-6: mock `FindAsync` to `ThrowsAsync(new Exception("known message"))`, call `LoadDataAsync()`, assert `status == "error"`, `error == "Nepodařilo se načíst počet boxů"`, `details == "known message"` via `JsonDocument`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A private nested test-double class with a mismatched `[TileId]`-less signature could confuse future readers into thinking it's a real tile | Low | Name it clearly `TestTransportBoxTile`, keep it `private` and file-local, add a one-line comment stating it exists only to make `FilterStates` configurable for branch coverage. |
| `JsonDocument`-based assertions are more verbose than direct property access | Low | Accepted — matches existing convention (`InventorySummaryTileBaseTests`); do not introduce `dynamic` or reflection as a shortcut. |
| Coverage tool may still show `LoadDataAsync`'s try-block below 100% if FR-5 is skipped | Medium | Include FR-5 as specified — it is cheap (one more `[Fact]`) and directly needed to exercise the try branch that FR-6 alone does not cover (FR-6 only proves the catch path; the `return` statement inside `try` needs its own passing execution). |

## Specification Amendments
- **FR-1–FR-4 clarification:** call `GenerateDrillDownFilters()` directly through the `CallGenerateDrillDownFilters()` wrapper on the test double, not through `LoadDataAsync` with a mocked repository — cheaper and isolates branch logic per Decision 3.
- **NFR-3 resolved:** test framework is **xUnit** + **Moq** + **FluentAssertions**, matching `test/Anela.Heblo.Tests/Features/Catalog/DashboardTiles/InventorySummaryTileBaseTests.cs` exactly. No new library needed.
- **"API / Interface Design" section resolved:** option (2) from the spec (a minimal test-only concrete subclass) is the chosen approach — see Decision 1. It is safe: `TileIdContractTests` only scans production assemblies, so the test double needs no `[TileId]` attribute and requires no dashboard registry changes.
- **Data Model section resolved:** anonymous-type assertions go through `JsonSerializer.Serialize` → `JsonDocument.Parse`, per Decision 2 — matching existing precedent.

## Prerequisites
None. No migrations, config, or infrastructure changes are needed — this is a self-contained new test file in an existing, already-buildable test project.
