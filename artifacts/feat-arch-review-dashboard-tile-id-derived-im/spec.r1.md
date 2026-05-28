# Specification: Explicit Stable Tile IDs for Dashboard Tiles

## Summary
Replace the runtime, class-name-derived `TileExtensions.GetTileId()` convention with an explicit, declared identifier on every `ITile` implementation. Because tile IDs are persisted in the `UserDashboardTiles.TileId` column and reference user settings, making them explicit eliminates silent user-data corruption on class renames, removes the buggy `Replace("tile", "")` substring collision risk, and adds a compile-time + test-time safety net for new tiles.

## Background
The dashboard module (`backend/src/Anela.Heblo.Xcc/Services/Dashboard/`) registers `ITile` implementations and identifies each one by a string returned from `TileExtensions.GetTileId()`:

```csharp
// backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileExtensions.cs:5
public static string GetTileId(this Type tileType) =>
    tileType.Name.ToLowerInvariant().Replace("tile", "");
```

This ID is the **persisted contract** between code and database rows in `UserDashboardTiles.TileId` (`UserDashboardTile` entity in `Anela.Heblo.Xcc/Domain/UserDashboardTile.cs`). User settings creation, auto-show population (`DashboardService.GetUserSettingsAsync`, lines 55, 70, 79), tile lookup during render (`DashboardService.GetTileDataAsync`, lines 140–151), and tile registration (`TileRegistry.RegisterTile<TTile>()`, lines 15–21) all rely on it.

Three concrete problems:

1. **Silent breakage on rename.** Renaming `PurchaseOrdersInTransitTile` → `PurchaseOrdersTile` changes the derived ID from `"purchaseordersintransit"` to `"purchaseorders"`. Existing DB rows are silently orphaned; users see "Tile not found" at runtime (`DashboardService.cs:147`), and there is no compiler/test signal.
2. **`Replace("tile", "")` is unbounded.** It removes every occurrence of the substring, not just a suffix. A class named `TileImportTile` collapses to `"import"`; `MyTileLatestTile` collapses to `"mylatest"`. Collisions and surprise IDs are silently possible.
3. **No registration validation.** `TileRegistry` happily registers two tiles with identical IDs — the second silently overwrites the first in the dictionary (`TileRegistry.cs:20`).

The application currently registers ~24 `ITile` implementations spread across feature modules (Catalog, Logistics, Manufacture, Purchase, Analytics, DataQuality, BackgroundJobs, WeatherForecast, Dashboard). All their currently-derived IDs are in active use in production and staging user settings, and must be preserved verbatim.

## Functional Requirements

### FR-1: Explicit `TileId` on every `ITile` implementation
Add a required, read-only `string TileId` property to the `ITile` interface. Each concrete tile must return a hardcoded literal (not a computed value). The legacy class-name-derivation extension is removed.

**Acceptance criteria:**
- `ITile` exposes `string TileId { get; }` (no default implementation).
- Every concrete tile class declares `public string TileId => "..."` with a string literal.
- Any new `ITile` implementation that omits `TileId` fails to compile.
- The expression `tileType.Name.ToLowerInvariant().Replace("tile", "")` does not appear anywhere in `backend/src` after this change.

### FR-2: Preserve existing persisted IDs verbatim
The explicit IDs introduced in FR-1 must exactly match the values the runtime derivation produces today, so existing rows in `UserDashboardTiles` remain valid with no database migration.

**Acceptance criteria:**
- For every existing tile class `T`, the new literal returned by `T.TileId` equals `typeof(T).Name.ToLowerInvariant().Replace("tile", "")` computed on `main` immediately before this change.
- A unit test asserts the above equality for every registered tile type (using the old derivation as the source of truth, with a comment that the derivation will be deleted once the test ratchet is in place — see FR-5).
- No EF Core migration is generated for the dashboard schema.
- Manual verification on staging: a user with rows in `UserDashboardTiles` still sees their tiles after deployment.

The complete list of preserved IDs (for FR-2 verification and FR-5 fixtures):

| Class | Preserved `TileId` |
|---|---|
| `InvoiceImportStatisticsTile` | `invoiceimportstatistics` |
| `FailedJobsTile` | `failedjobs` |
| `MaterialWithExpirationInventorySummaryTile` | `materialwithexpirationinventorysummary` |
| `MaterialWithoutExpirationInventorySummaryTile` | `materialwithoutexpirationinventorysummary` |
| `MaterialInventoryCountTile` | `materialinventorycount` |
| `ProductInventoryCountTile` | `productinventorycount` |
| `LowStockAlertTile` | `lowstockalert` |
| `ProductInventorySummaryTile` | `productinventorysummary` |
| `PurchaseOrdersInTransitTile` | `purchaseordersintransit` |
| `DataQualityStatusTile` | `dataqualitystatus` |
| `DqtYesterdayStatusTile` | `dqtyesterdaystatus` |
| `CriticalGiftPackagesTile` | `criticalgiftpackages` |
| `ReceivedBoxesTile` | `receivedboxes` |
| `ErrorBoxesTile` | `errorboxes` |
| `InTransitBoxesTile` | `intransitboxes` |
| `ManualActionRequiredTile` | `manualactionrequired` |
| `NextDayProductionTile` | `nextdayproduction` |
| `ManufactureConditionsTile` | `manufactureconditions` |
| `UpcomingProductionTile` | `upcomingproduction` |
| `TodayProductionTile` | `todayproduction` |
| `LowStockEfficiencyTile` | `lowstockefficiency` |
| `WeatherForecastTile` | `weatherforecast` |
| `BackgroundTaskStatusTile` | `backgroundtaskstatus` |

If `TransportBoxBaseTile` is an abstract/base class that is not registered (verify during implementation), it is excluded from FR-1 and FR-2.

### FR-3: ID format validation
Define and enforce a single ID format: lowercase ASCII letters, digits, and hyphens; length 1–100 (matches the existing column constraint); not empty or whitespace.

**Acceptance criteria:**
- A regex `^[a-z0-9-]{1,100}$` (or equivalent validator) is defined in a single place inside `Anela.Heblo.Xcc.Services.Dashboard`.
- `TileRegistry.RegisterTile<TTile>()` throws `InvalidOperationException` with a descriptive message (naming the offending tile type and the invalid ID) if `TileId` violates the format.
- Unit tests cover: empty string, whitespace-only, uppercase characters, special characters (`_`, `.`, space), length 0, length 101.
- All 23+ existing tile IDs (FR-2 table) pass the validator.

### FR-4: Duplicate-ID detection at registration
`TileRegistry.RegisterTile<TTile>()` must fail loudly if two tiles declare the same ID, rather than silently overwriting the dictionary entry.

**Acceptance criteria:**
- Attempting to register a second tile with the same ID throws `InvalidOperationException` whose message includes both type full names and the conflicting ID.
- A unit test asserts this behavior using two stub tile types that share an ID.

### FR-5: Discovery test for all registered tiles
Add a single test in `backend/test/Anela.Heblo.Tests/Features/Dashboard/` (e.g. `TileRegistrationContractTests.cs`) that uses reflection to enumerate every concrete `ITile` implementation in the application assemblies and asserts the registration contract.

**Acceptance criteria:**
- The test discovers all non-abstract `ITile` types in `Anela.Heblo.Application` and `Anela.Heblo.Xcc` (matching today's registration sources).
- For each discovered type:
  - It can be instantiated through the DI container (using the same setup as production registration).
  - The instance returns a non-empty `TileId` that passes the FR-3 format validator.
- No two discovered implementations return the same `TileId`.
- The test fails with a clear message if a developer adds a new tile that violates any rule above or accidentally duplicates an existing ID.

### FR-6: Update all call sites
All references to the old `GetTileId()` extension methods (in `DashboardService.cs`, `TileRegistry.cs`, any registration helpers, and tests) switch to the new property.

**Acceptance criteria:**
- After the change, no `.GetTileId(` call exists in `backend/src` or `backend/test`.
- `TileExtensions.cs` is either deleted (preferred) or reduced to nothing but documented helpers that delegate to the property without string manipulation.
- `dotnet build` succeeds; `dotnet format` produces no diff.
- All existing dashboard tests pass without modifying assertions about ID values.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. Reading `TileId` is a field/literal access; it replaces an equally cheap reflection-based string derivation that already runs once per registration / lookup. `TileRegistry.RegisterTile<TTile>()` now resolves a transient instance from the DI container at startup — acceptable since registration happens once per process and tiles are already DI-registered.

### NFR-2: Security
No change. IDs are not user-supplied; they are compile-time literals. No new attack surface.

### NFR-3: Backward compatibility
No database migration. No API contract change — the existing `UserDashboardTileDto`, `GetAvailableTilesResponse`, etc. continue to expose the same string IDs. Any frontend code that hardcodes tile IDs (none required, but possible in feature flags or analytics) continues to work because the literal values are preserved.

### NFR-4: Maintainability
A developer adding a new tile must:
1. Implement `ITile.TileId` returning a unique, format-valid string literal.
2. Register the tile via the existing registration extension.

Failure of step 1 is a compile error (interface contract). Collision with an existing ID is a test failure (FR-5) and, defensively, a registration-time exception (FR-4).

### NFR-5: Testability
All new validation logic lives in code paths reachable by pure unit tests — no test that requires a running database or web host.

## Data Model
No schema changes.

Existing entities (unchanged):
- `UserDashboardSettings` — keyed by `UserId`.
- `UserDashboardTile` — `TileId` (string, max 100, NOT NULL), `IsVisible`, `DisplayOrder`, FK to `UserDashboardSettings`. Unique per `(UserDashboardSettings, TileId)`.

The contract between code and DB is now expressed by the `ITile.TileId` literal on each implementation; tile classes themselves become the source of truth.

## API / Interface Design

### `ITile` interface (modified)
```csharp
public interface ITile
{
    string TileId { get; }            // NEW — required, must be a literal
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
```

### `TileExtensions` (deleted or reduced)
The `Type`-based and generic overloads (`GetTileId(this Type)`, `GetTileId<TTile>()`) are deleted. The `this ITile` overload, if retained for compatibility, returns `tile.TileId` with no string manipulation. Preferred outcome: delete the file entirely; call sites read `tile.TileId` directly.

### `TileRegistry.RegisterTile<TTile>()` (modified)
Resolves the tile via `_serviceProvider` (creating a temporary scope), reads `TileId`, validates format (FR-3), checks for duplicates (FR-4), then stores in `_registeredTiles`. Today's implementation derives the ID from `typeof(TTile)` without instantiation; the new implementation requires a one-time DI resolution at startup, which is acceptable.

```csharp
public void RegisterTile<TTile>() where TTile : class, ITile
{
    using var scope = _serviceProvider.CreateScope();
    var instance = scope.ServiceProvider.GetRequiredService<TTile>();
    var tileId = instance.TileId;

    if (!TileIdValidator.IsValid(tileId))
        throw new InvalidOperationException(
            $"Tile {typeof(TTile).FullName} declared invalid TileId '{tileId}'. Expected format: ^[a-z0-9-]{{1,100}}$");

    if (_registeredTiles.TryGetValue(tileId, out var existing))
        throw new InvalidOperationException(
            $"Duplicate TileId '{tileId}': {existing.FullName} and {typeof(TTile).FullName}");

    _registeredTiles[tileId] = typeof(TTile);
}
```

### Tile implementations (modified)
Each concrete tile gains a single line, e.g.:
```csharp
public string TileId => "purchaseordersintransit";
public string TileId => "backgroundtaskstatus";
public string TileId => "lowstockalert";
// ...one per tile, per the FR-2 table
```

### No HTTP / REST changes
No controller, DTO, or response shape changes. The string values transmitted to the frontend are identical to today's values.

## Dependencies
- No new NuGet packages.
- No new external services.
- Affected projects:
  - `Anela.Heblo.Xcc` — `ITile` interface, `TileRegistry`, `TileExtensions`, new `TileIdValidator`.
  - `Anela.Heblo.Application.Features.*` — every `*Tile.cs` file across Analytics, BackgroundJobs, Catalog, DataQuality, Logistics, Manufacture, Purchase, WeatherForecast, Dashboard (22 files).
  - `Anela.Heblo.Xcc/Services/Dashboard/Tiles/BackgroundTaskStatusTile.cs` — 1 file.
  - `Anela.Heblo.Tests` — new `TileRegistrationContractTests`, plus updates to any tests touching `GetTileId()`.

## Out of Scope
- Renaming any tile class (would change the canonical ID and require a data migration plan, intentionally avoided in this change).
- Changing the `UserDashboardTiles.TileId` column type, length, or index.
- Frontend changes — tile IDs are opaque strings from the React side.
- Generalizing the same pattern to other "registry of plugins" code outside dashboards (e.g. background refresh tasks).
- Adding versioning, aliasing, or migration helpers for tile IDs.
- Adopting an attribute-based alternative (`[TileId("...")]`) — rejected in favor of a required interface property because the interface gives a stronger compile-time guarantee than reflection over attributes.
- Localization of tile IDs (IDs remain technical, English, machine identifiers; user-visible `Title`/`Description` already handle localization).

## Open Questions
None.

## Status: COMPLETE