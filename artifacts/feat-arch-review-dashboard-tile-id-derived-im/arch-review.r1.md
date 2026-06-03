Now I have everything I need to write the architecture review.

# Architecture Review: Explicit, Stable Dashboard Tile Identifiers

## Skip Design: true

Backend-only refactor. No UI components, screens, or visual design decisions are introduced or changed. The OpenAPI wire format is explicitly preserved (NFR-3), and there are no DB schema changes (NFR-2).

## Architectural Fit Assessment

The change aligns with the codebase's existing patterns and module boundaries:

- **Layering is respected.** The attribute and registry validation live in `Anela.Heblo.Xcc.Services.Dashboard`, the same project that owns `ITile`, `TileExtensions`, `TileRegistry`, and `TileRegistryExtensions`. Tile classes in `Anela.Heblo.Application` and `Anela.Heblo.Xcc` only consume the new attribute. No new project references are created.
- **Fail-fast on startup matches existing convention.** `IHost.InitializeTileRegistry()` (`backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileRegistryExtensions.cs:29`) already runs a reflection-driven init pass — augmenting it with validation is the obvious extension point.
- **Surgical change, no architectural shifts.** No new modules, no MediatR pipeline changes, no persistence work, no frontend work. Vertical Slice boundaries are unaffected.

The only point of friction is that the spec **undercounts the affected tile classes and confuses abstract base classes with registered concrete tiles** — see Specification Amendments.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Xcc.Services.Dashboard
├── ITile                          (unchanged)
├── TileIdAttribute               [NEW] — class-level metadata, sealed, AllowMultiple=false, Inherited=false
├── TileExtensions                [REWRITTEN] — reads attribute, throws on missing
├── TileRegistry                   (unchanged — keeps consuming GetTileId())
├── TileRegistryExtensions        [AUGMENTED] — InitializeTileRegistry adds duplicate-detection pass
└── Tiles/BackgroundTaskStatusTile [+ [TileId("backgroundtaskstatus")]]

Anela.Heblo.Application
├── Features/**/DashboardTiles/*Tile.cs   [+ [TileId("<pinned-value>")] on every concrete ITile]
└── Module registrations                   (unchanged)

Anela.Heblo.Tests
├── Features/Dashboard/Fixtures/TestTiles.cs   [+ [TileId(...)] on every fixture tile]
├── Features/Dashboard/TileRegistryTests.cs    [+ [TileId("tracked")] on TrackedTile]
├── Features/Dashboard/TileIdContractTests.cs  [NEW — FR-5 reflection-driven regression suite]
└── Features/Dashboard/TileRegistryValidationTests.cs [NEW — FR-4 startup-validation cases]
```

### Key Design Decisions

#### Decision 1: Attribute on the type, not a member on the instance

**Options considered:**
1. `[TileId("...")]` attribute on the class.
2. `const string TileId = "..."` per tile + interface default method.
3. Instance member `string ITile.TileId { get; }` resolved via DI.

**Chosen approach:** Option 1 — attribute on the class.

**Rationale:** `TileRegistry.RegisterTile<TTile>` and `TileRegistryExtensions.InitializeTileRegistry` resolve the ID from the `Type` without instantiating the tile (`tileType.GetTileId()`). Tiles are `Scoped` and have non-trivial constructor dependencies (e.g., `MaterialInventoryCountTile` requires `ICatalogRepository`, `TimeProvider`). Forcing instantiation just to read an ID is wasteful and would entangle the registry with scope management. Option 2 cannot be enforced via the interface in C# without instance members. Option 1 is the only choice that keeps the existing call sites in `TileRegistry.RegisterTile<TTile>` and `TileRegistry.ToMetadata` working unchanged.

#### Decision 2: `Inherited = false` on the attribute

**Options considered:**
1. `Inherited = false` — each concrete subclass must declare its own `[TileId]`.
2. `Inherited = true` — abstract base can declare an attribute, all subclasses inherit it.

**Chosen approach:** `Inherited = false` (as specified).

**Rationale:** Three abstract base classes exist (`TransportBoxBaseTile`, `UpcomingProductionTile`, `InventoryCountTileBase`, `InventorySummaryTileBase`). Each has 2–3 concrete subclasses that get registered, and **each subclass has a distinct persisted ID** (`intransitboxes`, `receivedboxes`, `errorboxes`, `todayproduction`, `nextdayproduction`, etc.). Inheritance would silently make every subclass share one ID — exactly the kind of collision FR-4 is meant to catch. `Inherited = false` forces the declaration at the registration boundary, which is the right one.

#### Decision 3: Validation happens in `InitializeTileRegistry`, not in `RegisterTile<TTile>`

**Options considered:**
1. Validate per-type during `RegisterTile<TTile>` (incremental).
2. Validate the full set in a single pass during `InitializeTileRegistry` (batch).

**Chosen approach:** Hybrid — `GetTileId(Type)` throws on missing/empty (so `RegisterTile<TTile>` throws too), and `InitializeTileRegistry` adds a single duplicate-detection pass after all `RegisterTile<TTile>` calls.

**Rationale:** Duplicate detection requires the full set to be known. The static `ConcurrentBag<Type> RegisteredTileTypes` in `TileRegistryExtensions.cs:9` is already collected before `InitializeTileRegistry` runs, so the validation pass can group-by `GetTileId()` and throw with **all** offenders listed at once — a single, comprehensive boot-time error beats a sequence of one-at-a-time errors.

#### Decision 4: FR-5 reflection scan filters to **concrete, non-abstract** `ITile` implementors

**Options considered:**
1. Scan every `ITile` implementor including abstract base classes.
2. Scan only concrete (non-abstract) `ITile` implementors.

**Chosen approach:** Option 2.

**Rationale:** `TransportBoxBaseTile`, `UpcomingProductionTile`, `InventoryCountTileBase`, `InventorySummaryTileBase` are abstract and never registered. Requiring `[TileId]` on them would force a meaningless attribute that `Inherited = false` immediately discards. The test must filter `t.IsClass && !t.IsAbstract && typeof(ITile).IsAssignableFrom(t)`.

## Implementation Guidance

### Directory / Module Structure

**New files:**
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileIdAttribute.cs`
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs` (FR-5)
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileRegistryValidationTests.cs` (FR-4 cases)

**Modified files:**
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileExtensions.cs` (rewrite per spec §API)
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileRegistryExtensions.cs` (augment `InitializeTileRegistry`)
- All 22 concrete tile classes (see amendments below) — add `[TileId("...")]`
- All test fixture tiles in `backend/test/Anela.Heblo.Tests/Features/Dashboard/Fixtures/TestTiles.cs` and `TrackedTile` in `TileRegistryTests.cs` — add `[TileId("...")]`
- `docs/features/dashboard_tiles_implementation_guide.md` (FR-6 rename procedure)

### Interfaces and Contracts

```csharp
// Anela.Heblo.Xcc.Services.Dashboard
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TileIdAttribute : Attribute
{
    public string Value { get; }
    public TileIdAttribute(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Tile ID must be a non-empty, non-whitespace string.", nameof(value));
        Value = value;
    }
}
```

`TileExtensions.GetTileId(Type)` throws `InvalidOperationException` with the offending CLR full name when the attribute is missing. `ITileRegistry` and `TileRegistry` keep their existing surface — no signature changes.

### Data Flow

Unchanged at runtime:

```
Startup
  Module.ConfigureServices → services.RegisterTile<TTile>()
    → AddScoped<TTile>(); RegisteredTileTypes.Add(typeof(TTile))
  app.InitializeTileRegistry()
    → foreach tile type: registry.RegisterTile<TTile>() ── reads [TileId]
    → [NEW] validate duplicates across all registered IDs → throw if any
    → [Existing exception path] missing [TileId] throws inside RegisterTile<TTile>

Request: GET /api/dashboard/tiles/{tileId}/data
  GetTileDataHandler → _tileRegistry.GetTileMetadata(tileId)
    → lookup _registeredTiles[tileId]  (no attribute reflection on hot path — already cached at startup)
```

The attribute is read **once per type at startup**, persisted in `_registeredTiles[tileId] = tileType`, and never re-read on the request path. NFR-1 is satisfied trivially.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Pinned attribute value drifts from the current derived value, silently orphaning user rows | **High** | FR-5 (4) backward-compat assertion: for every class ending in `"Tile"`, attribute value must equal `name.ToLowerInvariant().Replace("tile", "")`. CI fails if drift occurs without a co-PR migration. |
| Spec's enumerated tile list is incomplete; an unlisted tile (e.g., `InTransitBoxesTile`) is missed | **High** | Pre-merge audit step: run `grep` for `class \w+Tile\s*:\s*\w+` filtered to concrete classes and cross-check against `RegisterTile<...>` call sites. List below in **Specification Amendments** is the corrected ground truth. |
| Existing `TileRegistryTests` (`TrackedTile`) and `Fixtures/TestTiles.cs` break — they rely on derived IDs | **Medium** | These must also receive `[TileId("...")]` matching their currently-derived values (`"tracked"`, `"newautoshow"`, `"manual"`, `"auto1"`, `"auto2"`, `"testwithdata"`). FR-5 reflection scan also covers them if the test assembly is in scope; restrict assembly list per FR-5. |
| Two co-developers add tiles with same ID in concurrent PRs; only one tested locally | **Low** | FR-4 startup validation fails the application boot of the merged main branch in CI's `dotnet build` + smoke pass. |
| Concrete subclasses of abstract base tiles (e.g., `InTransitBoxesTile : TransportBoxBaseTile`) silently inherit nothing — confusion about whether attribute is needed | **Medium** | `Inherited = false` is by design (Decision 2). FR-6 doc must explicitly mention "abstract base tile classes don't need `[TileId]`; every concrete subclass does". |
| `TestTileWithData` has a constructor-injected `TileId` field unrelated to registration; readers may confuse it with `[TileId]` | **Low** | Leave the existing instance field alone (it's used by test logic). Add the class-level `[TileId("testwithdata")]` separately. Note this in the test file with a one-line comment. |

## Specification Amendments

### Amendment 1 — Concrete tile inventory is wrong/incomplete

The spec states "12 tile classes implementing `ITile`". The actual registered set is **22 concrete tiles** plus **4 abstract base classes**. The pinned attribute values must be:

**Xcc assembly (1):**
- `BackgroundTaskStatusTile` → `"backgroundtaskstatus"`

**Application assembly (21):**
- `InvoiceImportStatisticsTile` → `"invoiceimportstatistics"`
- `FailedJobsTile` → `"failedjobs"`
- `ProductInventoryCountTile` → `"productinventorycount"`
- `MaterialInventoryCountTile` → `"materialinventorycount"`
- `ProductInventorySummaryTile` → `"productinventorysummary"`
- `MaterialWithExpirationInventorySummaryTile` → `"materialwithexpirationinventorysummary"`
- `MaterialWithoutExpirationInventorySummaryTile` → `"materialwithoutexpirationinventorysummary"`
- `LowStockAlertTile` → `"lowstockalert"`
- `PurchaseOrdersInTransitTile` → `"purchaseordersintransit"`
- `DataQualityStatusTile` → `"dataqualitystatus"`
- `DqtYesterdayStatusTile` → `"dqtyesterdaystatus"`
- `InTransitBoxesTile` → `"intransitboxes"`
- `ReceivedBoxesTile` → `"receivedboxes"`
- `ErrorBoxesTile` → `"errorboxes"`
- `CriticalGiftPackagesTile` → `"criticalgiftpackages"`
- `TodayProductionTile` → `"todayproduction"`
- `NextDayProductionTile` → `"nextdayproduction"`
- `ManualActionRequiredTile` → `"manualactionrequired"`
- `ManufactureConditionsTile` → `"manufactureconditions"`
- `LowStockEfficiencyTile` → `"lowstockefficiency"`
- `WeatherForecastTile` → `"weatherforecast"`

**Abstract base classes (do NOT add attribute):**
- `TransportBoxBaseTile`, `UpcomingProductionTile`, `InventoryCountTileBase`, `InventorySummaryTileBase`

The spec entries `TransportBoxBaseTile → "transportboxbase"` and `UpcomingProductionTile → "upcomingproduction"` are **incorrect**: those types are abstract and never reach `RegisterTile<TTile>`. Adding `[TileId]` to them would either be inert (with `Inherited = false`) or actively harmful (with `Inherited = true`, all subclasses collide).

### Amendment 2 — FR-5 must filter abstract types

The reflection scan in `TileIdContractTests` must restrict to `t.IsClass && !t.IsAbstract && typeof(ITile).IsAssignableFrom(t)`. Without this, the test fails for the four abstract base classes that intentionally have no attribute.

### Amendment 3 — Test fixtures need attributes too

The spec's "all 12 existing tile classes" omits test fixtures. The following must also receive `[TileId]` to keep tests green:

- `TrackedTile` → `"tracked"` (referenced literally in `TileRegistryTests.cs:22`)
- `NewAutoShowTile`, `ManualTile`, `AutoTile1`, `AutoTile2`, `TestTileWithData` in `Fixtures/TestTiles.cs` — derive their current IDs from the existing rule (`Replace("tile", "")` over lowercased name) and pin those.

Decide explicitly whether FR-5's reflection scan includes the test assembly. **Recommended: no** — scan only `Anela.Heblo.Xcc` and `Anela.Heblo.Application` assemblies (as FR-5 already states). The fixture tiles still need attributes for compilation/runtime, but they aren't governed by the contract test.

### Amendment 4 — Clarify duplicate detection's interaction with the static `ConcurrentBag`

`TileRegistryExtensions.RegisteredTileTypes` is `static`. In a multi-host test process (xUnit runs tests in one process), residual entries from prior tests can pollute later runs. The FR-4 validation tests need a way to seed and reset this state. **Recommendation:** expose an internal `Clear()` method on `TileRegistryExtensions` (or use a separate, instance-scoped `TileRegistry` directly without the static bag, since `TileRegistry.RegisterTile<TTile>` works standalone). Without addressing this, FR-4 tests will be flaky.

## Prerequisites

- **None** in infrastructure, config, secrets, DB, or external services.
- **Pre-merge audit script** (developer task, ~10 min): grep for `class \w+\s*:\s*ITile`, filter out abstract classes, cross-check against `RegisterTile<` registrations, and confirm each concrete class's name-derived ID matches the proposed pinned attribute value. Output should be checked into the PR description so reviewers can verify exhaustiveness.
- No EF Core migration. No frontend regeneration. No Key Vault secret. No CI changes (FR-5 test runs in the existing `dotnet test` lane).