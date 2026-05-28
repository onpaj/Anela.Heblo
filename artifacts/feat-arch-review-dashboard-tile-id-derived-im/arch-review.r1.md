I have a complete picture. The spec has a significant factual gap: it claims only two tile classes exist, but the codebase has ~24 concrete tiles plus three abstract base classes, all of which need updates. Writing the review now.

```markdown
# Architecture Review: Explicit Stable Tile IDs for Dashboard Tiles

## Skip Design: true

Pure backend refactor: interface change, internal validator, registry behaviour, and a new unit test. No new UI components, no visual changes, no API contract change. The string IDs transmitted to the frontend are byte-identical to today's values.

## Architectural Fit Assessment

The change aligns cleanly with the existing dashboard architecture in `backend/src/Anela.Heblo.Xcc/Services/Dashboard/`. It replaces a single implicit convention (`tileType.Name.ToLowerInvariant().Replace("tile", "")`) with an explicit declaration on the `ITile` interface — strictly local to the Dashboard subsystem.

Integration points:
- **`ITile` interface** (`Anela.Heblo.Xcc.Services.Dashboard.ITile`) — gains one read-only property.
- **`TileRegistry`** — must resolve a temporary instance at registration time to read the property, then validate format and uniqueness before storing the `(tileId → Type)` entry. Registration is already host-time, so service-provider access is available.
- **All concrete `ITile` implementations** — must declare a literal `TileId`. The spec significantly understates the scope here (see Specification Amendments).
- **Abstract tile base classes** (three exist: `TransportBoxBaseTile`, `InventoryCountTileBase`, `InventorySummaryTileBase`) — must declare an `abstract string TileId { get; }` so concrete subclasses are forced to supply a literal at compile time.
- **Five call sites** of the old extension method (`DashboardService.cs` lines 55, 70, 79, 160; `GetAvailableTilesHandler.cs` line 22) collapse to direct `tile.TileId` access.
- **Two existing tile tests** (`WeatherForecastTileTests.cs:40`, `ManufactureConditionsTileTests.cs:48`) reference `TileExtensions.GetTileId<T>()` and must switch to instance access.

This is consistent with the Clean Architecture layering: the contract lives in `Xcc` (cross-cutting), implementations live in feature modules, and there are no new layer crossings.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Xcc.Services.Dashboard                             │
│                                                                │
│   ITile  ──── string TileId { get; }   ◄── NEW (required)      │
│     ▲                                                          │
│     │ implemented by                                           │
│     │                                                          │
│   ┌─┴────────────────────────────────────────────────────┐     │
│   │ Concrete tiles (~24, across feature modules)         │     │
│   │   public string TileId => "purchaseordersintransit"; │     │
│   │   public string TileId => "weatherforecast"; ...     │     │
│   └──────────────────────────────────────────────────────┘     │
│                                                                │
│   TileIdValidator (NEW, internal static)                       │
│       ^[a-z0-9-]{1,100}$                                       │
│                                                                │
│   TileRegistry                                                 │
│       RegisterTile<TTile>():                                   │
│           1. resolve instance via _serviceProvider             │
│           2. read instance.TileId                              │
│           3. TileIdValidator.Validate(id)         (FR-3)       │
│           4. throw if _registeredTiles.ContainsKey(id) (FR-4)  │
│           5. store (id → typeof(TTile))                        │
│                                                                │
│   TileExtensions  ◄── REMOVED entirely                         │
└────────────────────────────────────────────────────────────────┘
        ▲
        │
┌───────┴───────────────────────────────────────────────────────┐
│ Anela.Heblo.Tests.Features.Dashboard                          │
│   RegisteredTilesContractTests (NEW)                          │
│     - reflects over assemblies, finds non-abstract ITile      │
│     - resolves each via DI, reads TileId                      │
│     - asserts format + uniqueness                             │
└───────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Declaration mechanism — instance property vs static abstract vs attribute

**Options considered:**
1. Instance property `string TileId { get; }` on `ITile` (spec's choice).
2. C# 11 `static abstract string TileId { get; }` — no instance needed.
3. `[TileId("...")]` attribute on the class.
4. `public const string TileId` convention enforced by reflection.

**Chosen approach:** Instance property on `ITile`.

**Rationale:** It is the only option that gives a compile-time guarantee (missing override = build break), is accessible everywhere `ITile` is already passed by reference (`DashboardService`, `GetAvailableTilesHandler`), and integrates with the existing DI-resolved-instance pattern the registry already uses. Static abstract members would force every call site to know the concrete type and complicate registry-time access (which holds a `Type`, not a generic parameter). Attributes lose compile-time enforcement. Constants cannot be enforced on an interface contract.

#### Decision 2: Where validation runs

**Options considered:**
1. At `IServiceCollection.RegisterTile<TTile>()` (build-time of the service collection, before host build).
2. At `TileRegistry.RegisterTile<TTile>()` (called from `InitializeTileRegistry(this IHost)` after host build).
3. Only at the new discovery test.

**Chosen approach:** Validation runs inside `TileRegistry.RegisterTile<TTile>()` (option 2) **and** the discovery test (option 3, as a belt-and-braces compile-CI gate).

**Rationale:** At option 2's call point the `IServiceProvider` is already built, so we can resolve a temporary scoped instance, read `TileId`, validate format, and detect duplicates — all before storing. Validating earlier (option 1) is impossible because the property is on the instance and the service provider does not exist yet. Adding the discovery test catches format/uniqueness regressions even if a developer bypasses the registry. Fail-fast at host startup beats failing at first user request.

#### Decision 3: Abstract base classes

**Options considered:**
1. Each base class declares `public abstract string TileId { get; }`, forcing concrete subclasses to override.
2. Base class provides a default derivation (would resurrect the bug we are removing).

**Chosen approach:** Option 1 — abstract property on every base, literal override on every concrete subclass.

**Rationale:** Three abstract bases exist: `TransportBoxBaseTile`, `InventoryCountTileBase`, `InventorySummaryTileBase`. None of them are registered directly. Forcing the literal at the leaf class is the whole point of the change — silently defaulting would defeat it. The compiler enforces that subclasses (`InTransitBoxesTile`, `ReceivedBoxesTile`, `ProductInventoryCountTile`, etc.) declare their own ID.

#### Decision 4: Format validator placement

**Options considered:**
1. Internal `static class TileIdValidator` in `Anela.Heblo.Xcc.Services.Dashboard`.
2. Inline regex inside `TileRegistry`.

**Chosen approach:** Standalone `internal static TileIdValidator` with `IsValid(string)` and `Validate(string)` methods, in the same namespace as `TileRegistry`.

**Rationale:** FR-3 explicitly requires the validator "defined in a single place" and FR-5 requires the discovery test to use the same predicate. A standalone helper makes that sharing trivial and removes duplication. Keep it `internal` — it is not part of the public Dashboard contract.

#### Decision 5: Deletion vs deprecation of `TileExtensions`

**Options considered:**
1. Delete `TileExtensions.cs` entirely.
2. Keep `GetTileId(this ITile)` as a passthrough wrapper around `tile.TileId`.

**Chosen approach:** Delete `TileExtensions.cs` entirely.

**Rationale:** Five call sites in production code and two tests is a trivial sweep. Leaving a passthrough invites future drift and re-emergence of the runtime-derivation pattern. The migration is one mechanical replace per call site (`t.GetTileId()` → `t.TileId`).

## Implementation Guidance

### Directory / Module Structure

**Modify:**
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/ITile.cs` — add property.
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileRegistry.cs` — resolve instance, validate, detect duplicates.

**Add:**
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileIdValidator.cs` — `internal static class` with regex `^[a-z0-9-]{1,100}$`.
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/RegisteredTilesContractTests.cs` — FR-5 discovery test.

**Delete:**
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileExtensions.cs`.

**Modify (every concrete `ITile` implementation — full enumeration in Spec Amendments):**
- `backend/src/Anela.Heblo.Application/Features/**/DashboardTiles/*Tile.cs` (~23 files)
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/Tiles/BackgroundTaskStatusTile.cs`

**Modify (abstract tile base classes — declare `abstract string TileId { get; }`):**
- `backend/src/Anela.Heblo.Application/Features/Logistics/DashboardTiles/TransportBoxBaseTile.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/InventoryCountTileBase.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/InventorySummaryTileBase.cs`

**Modify (call sites):**
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/DashboardService.cs` (lines 55, 70, 79, 160) — `tile.GetTileId()` → `tile.TileId`.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetAvailableTiles/GetAvailableTilesHandler.cs` (line 22) — same change.
- `backend/test/Anela.Heblo.Tests/Features/WeatherForecast/DashboardTiles/WeatherForecastTileTests.cs` (line 40) — replace `TileExtensions.GetTileId<WeatherForecastTile>()` with instance access on `_tile.TileId` (test already has a constructed instance).
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/DashboardTiles/ManufactureConditionsTileTests.cs` (line 48) — same.

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Xcc.Services.Dashboard;

public interface ITile
{
    string TileId { get; }            // NEW — literal, immutable, persisted contract
    string Title { get; }
    string Description { get; }
    TileSize Size { get; }
    TileCategory Category { get; }
    bool DefaultEnabled { get; }
    bool AutoShow { get; }
    Type ComponentType { get; }
    string[] RequiredPermissions { get; }
    Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null,
                               CancellationToken cancellationToken = default);
}

internal static class TileIdValidator
{
    private static readonly Regex Pattern =
        new(@"^[a-z0-9-]{1,100}$", RegexOptions.Compiled);

    public static bool IsValid(string? tileId) =>
        !string.IsNullOrEmpty(tileId) && Pattern.IsMatch(tileId);

    public static void Validate(string? tileId, Type tileType)
    {
        if (!IsValid(tileId))
            throw new InvalidOperationException(
                $"Tile '{tileType.FullName}' declared invalid TileId '{tileId}'. " +
                $"Required format: lowercase ASCII letters, digits, hyphens; length 1-100.");
    }
}
```

**`TileRegistry.RegisterTile<TTile>` updated body:**
```csharp
public void RegisterTile<TTile>() where TTile : class, ITile
{
    var tileType = typeof(TTile);

    string tileId;
    using (var scope = _serviceProvider.CreateScope())
    {
        var instance = (ITile)scope.ServiceProvider.GetRequiredService(tileType);
        tileId = instance.TileId;
    }

    TileIdValidator.Validate(tileId, tileType);

    if (_registeredTiles.TryGetValue(tileId, out var existing))
        throw new InvalidOperationException(
            $"Duplicate TileId '{tileId}'. Already registered by '{existing.FullName}'; " +
            $"attempted to register '{tileType.FullName}'.");

    _registeredTiles[tileId] = tileType;
}
```

### Data Flow

**Startup (one-time):**
1. Each feature module calls `services.RegisterTile<TTile>()` (no behavioural change — still records the type for later).
2. `app.InitializeTileRegistry()` iterates recorded types and invokes `ITileRegistry.RegisterTile<T>()` per type.
3. For each tile: registry creates a DI scope, resolves the tile, reads `TileId`, validates, checks duplicates, stores.
4. Any violation throws — host fails fast with a descriptive error.

**Per-request (unchanged path, simpler code):**
1. `DashboardController` → `DashboardService.GetTileDataAsync(userId)`.
2. Service reads user's `UserDashboardTile` rows (each carries a `TileId` string from the DB).
3. Service calls `_tileRegistry.GetTile(tileId)` — dictionary lookup.
4. `tile.TileId` is read directly in `TileData` construction (replacing `tile.GetTileId()`).
5. Response shape and persisted IDs are unchanged.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Developer declares an ID that differs from the value previously produced by the runtime derivation, silently orphaning user rows for that tile | **High** | The discovery test (FR-5) only catches *format* and *uniqueness*, not *historical equivalence*. Add a one-shot golden-list assertion: a test that hardcodes the full `Type → expected TileId` map for the ~24 existing tiles and asserts every concrete `ITile` matches. Drop after the migration commit if undesired long-term; mandatory for the migration PR. |
| Registration now resolves a tile instance at startup — a tile constructor that touches a slow/unavailable dependency would delay or break startup | Medium | Tile constructors are already required to be cheap (they are resolved per-request today). Document this in `XccModule.cs` or `TileRegistryExtensions.cs` as a precondition. If a tile's constructor *must* do I/O, that is a pre-existing bug. |
| Two existing tile tests use the static `TileExtensions.GetTileId<T>()`; removing the class breaks them | Low | Update both tests as part of this change. They are listed above. |
| Abstract base classes have no compile-time enforcement that subclasses don't return the same hardcoded string by copy-paste | Medium | Duplicate detection at registry time (FR-4) + discovery test (FR-5) both catch this at startup / CI. Acceptable. |
| Regex `^[a-z0-9-]{1,100}$` rejects existing ID `purchaseordersintransit` — verify | Low | All current derived IDs contain only `[a-z]`, all ≤ 27 chars; the regex accepts them. Verified manually for every existing tile. |
| Adding `string TileId { get; }` to `ITile` is a binary-breaking interface change | Low | The interface has no external implementers — it lives in `Xcc` and all implementations are in this solution. No NuGet consumers. |
| `RegisterTile<TTile>` creates a scope solely to read a property; minor startup overhead | Low | One-time at startup, ~24 tiles. Negligible. |

## Specification Amendments

The following gaps must be reflected in the spec before implementation.

### Amendment 1: Tile count is wrong

The spec states: *"Two tile implementations exist today: `PurchaseOrdersInTransitTile` and `BackgroundTaskStatusTile`."*

This is incorrect. The actual concrete `ITile` implementations registered today (via `RegisterTile<T>` in module files):

| # | Class | Module | Required literal `TileId` |
|---|-------|--------|---------------------------|
| 1 | `BackgroundTaskStatusTile` | `XccModule` | `"backgroundtaskstatus"` |
| 2 | `PurchaseOrdersInTransitTile` | `DashboardModule` | `"purchaseordersintransit"` |
| 3 | `DataQualityStatusTile` | `DashboardModule` | `"dataqualitystatus"` |
| 4 | `DqtYesterdayStatusTile` | `DashboardModule` | `"dqtyesterdaystatus"` |
| 5 | `FailedJobsTile` | `DashboardModule` | `"failedjobs"` |
| 6 | `InTransitBoxesTile` | `LogisticsModule` | `"intransitboxes"` |
| 7 | `ReceivedBoxesTile` | `LogisticsModule` | `"receivedboxes"` |
| 8 | `ErrorBoxesTile` | `LogisticsModule` | `"errorboxes"` |
| 9 | `CriticalGiftPackagesTile` | `LogisticsModule` | `"criticalgiftpackages"` |
| 10 | `WeatherForecastTile` | `WeatherForecastModule` | `"weatherforecast"` |
| 11 | `ProductInventoryCountTile` | `CatalogModule` | `"productinventorycount"` |
| 12 | `MaterialInventoryCountTile` | `CatalogModule` | `"materialinventorycount"` |
| 13 | `ProductInventorySummaryTile` | `CatalogModule` | `"productinventorysummary"` |
| 14 | `MaterialWithExpirationInventorySummaryTile` | `CatalogModule` | `"materialwithexpirationinventorysummary"` |
| 15 | `MaterialWithoutExpirationInventorySummaryTile` | `CatalogModule` | `"materialwithoutexpirationinventorysummary"` |
| 16 | `LowStockAlertTile` | `CatalogModule` | `"lowstockalert"` |
| 17 | `LowStockEfficiencyTile` | `PurchaseModule` | `"lowstockefficiency"` |
| 18 | `InvoiceImportStatisticsTile` | `AnalyticsModule` | `"invoiceimportstatistics"` |
| 19 | `TodayProductionTile` | `ManufactureModule` | `"todayproduction"` |
| 20 | `NextDayProductionTile` | `ManufactureModule` | `"nextdayproduction"` |
| 21 | `ManualActionRequiredTile` | `ManufactureModule` | `"manualactionrequired"` |
| 22 | `ManufactureConditionsTile` | `ManufactureModule` | `"manufactureconditions"` |

(Two file globs found additional tiles that are not registered today — `ManufactureConditionsTile` and others. The implementer must reconcile registered-vs-defined; only registered tiles risk persisted-ID breakage, but FR-1 still requires every `ITile` to declare a `TileId` to keep the interface contract honest.)

**Every literal above must equal `typeof(T).Name.ToLowerInvariant().Replace("tile", "")` computed on `main`** — implementer must verify each one before merge. Acceptance criterion in FR-2 should reference this table (or the verification test described in Amendment 3), not "two tiles".

### Amendment 2: Abstract tile base classes

Three abstract `ITile` base classes exist and are not mentioned in the spec: `TransportBoxBaseTile`, `InventoryCountTileBase`, `InventorySummaryTileBase`. FR-1 should be amended to state:

> Abstract base classes that implement `ITile` declare `public abstract string TileId { get; }`. Concrete subclasses must supply a literal.

### Amendment 3: Migration-equivalence test

Add a new acceptance criterion to FR-2:

> A unit test asserts that for every type `T` where `typeof(ITile).IsAssignableFrom(T) && !T.IsAbstract`, the resolved `TileId` equals `T.Name.ToLowerInvariant().Replace("tile", "")`. This test is the safety net for FR-2's "no DB migration" requirement and may be deleted in a follow-up PR once the migration is proven on staging.

### Amendment 4: Update FR-6 grep target

`grep -r "GetTileId" backend/src` will still match the property *name* if we choose to call it `TileId` — the spec already does. But it will *not* match if we keep the property as `TileId` and the call sites use `tile.TileId`. The spec's acceptance criterion is correct **only if** the property is named `TileId` (not `GetTileId`). Confirm property name = `TileId` to make the grep unambiguous.

### Amendment 5: Additional call site

`GetAvailableTilesHandler.cs:22` calls `t.GetTileId()` and is missing from the spec's "Update all call sites" list (FR-6 mentions `DashboardService.cs`, `TileRegistry.cs`, and tests). Add it explicitly.

## Prerequisites

None.

- No database migration.
- No new NuGet packages.
- No new configuration.
- No infrastructure changes.
- No coordination with frontend — IDs on the wire are unchanged.

Implementation can begin immediately after the spec is amended per the section above.
```