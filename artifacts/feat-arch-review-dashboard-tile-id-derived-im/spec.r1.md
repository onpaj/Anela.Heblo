# Specification: Explicit Stable Tile IDs for Dashboard Tiles

## Summary
Replace the runtime, class-name-derived `TileExtensions.GetTileId()` convention with an explicit, declared identifier on every `ITile` implementation. Tile IDs are persisted in the database (`UserDashboardTile.TileId`), so making them explicit eliminates silent user-data corruption on class renames, removes the buggy `Replace("tile", "")` substring collision risk, and adds a compile-time + test-time safety net for new tiles.

## Background
The dashboard module (`backend/src/Anela.Heblo.Xcc/Services/Dashboard/`) registers `ITile` implementations and identifies each one by a string returned from `TileExtensions.GetTileId()`:

```csharp
public static string GetTileId(this Type tileType) =>
    tileType.Name.ToLowerInvariant().Replace("tile", "");
```

This ID is the **persisted contract** between code and database rows in `UserDashboardTiles.TileId` (configured in `UserDashboardTileConfiguration.cs`, max length 100, unique per user). User settings, auto-show population (`DashboardService.GetUserSettingsAsync`), tile lookup (`DashboardService.GetTileDataAsync` lines 140–151), and tile registration (`TileRegistry.RegisterTile<TTile>`) all rely on it.

Three concrete problems:

1. **Silent breakage on rename.** Renaming `PurchaseOrdersInTransitTile` → `PurchaseOrdersTile` changes the derived ID from `purchaseordersintransit` to `purchaseorders`. Existing DB rows are silently orphaned; users see "Tile not found" errors at runtime (`DashboardService.cs:147`), and there is no compiler/test signal.
2. **`Replace("tile", "")` is unbounded.** It removes every occurrence of the substring, not just a suffix. A class named `TileImportTile` collapses to `"import"`; `MyTileLatestTile` collapses to `"mylatest"`. Collisions and surprise IDs are silently possible.
3. **No registration validation.** `TileRegistry` happily registers two tiles with identical IDs — the second silently overwrites the first in the dictionary.

Two tile implementations exist today: `PurchaseOrdersInTransitTile` (current ID `purchaseordersintransit`) and `BackgroundTaskStatusTile` (current ID `backgroundtaskstatus`). Both IDs must be preserved verbatim to avoid breaking persisted user settings.

## Functional Requirements

### FR-1: Explicit `TileId` on every `ITile` implementation
Add a required, read-only `string TileId` property to the `ITile` interface. Each concrete tile must return a hardcoded literal (not a computed value). The legacy class-name derivation must be removed.

**Acceptance criteria:**
- `ITile` exposes `string TileId { get; }`.
- `PurchaseOrdersInTransitTile.TileId` returns the literal `"purchaseordersintransit"`.
- `BackgroundTaskStatusTile.TileId` returns the literal `"backgroundtaskstatus"`.
- A new tile class that does not implement `TileId` fails to compile.
- `TileExtensions.GetTileId(this Type)` and `GetTileId<TTile>()` are removed; `GetTileId(this ITile)` either delegates to `tile.TileId` or is removed in favor of direct property access at call sites.

### FR-2: Preserve existing persisted IDs
The explicit IDs introduced in FR-1 must exactly match the values previously produced by the runtime derivation, so existing rows in `UserDashboardTiles` remain valid with no database migration required.

**Acceptance criteria:**
- For every existing tile class, the new literal equals `typeof(T).Name.ToLowerInvariant().Replace("tile", "")` computed on `main` immediately before this change.
- No EF Core migration is generated for the dashboard schema as part of this change.
- Manual verification: a user with rows in `UserDashboardTiles` (e.g. on staging) still sees their tiles after deployment.

### FR-3: ID format validation
Define and enforce an ID format: lowercase ASCII letters, digits, and hyphens; length 1–100 (matches the existing column constraint); must not be empty or whitespace.

**Acceptance criteria:**
- A regex or equivalent validator (`^[a-z0-9-]{1,100}$`) is defined in a single place inside `Anela.Heblo.Xcc.Services.Dashboard`.
- `TileRegistry.RegisterTile<TTile>()` throws `InvalidOperationException` with a descriptive message if the resolved `TileId` violates the format.
- A unit test covers: empty string, whitespace, uppercase, special characters, length > 100.

### FR-4: Duplicate-ID detection at registration
`TileRegistry.RegisterTile<TTile>()` must fail loudly if two tiles declare the same ID, rather than silently overwriting the dictionary entry.

**Acceptance criteria:**
- Attempting to register a second tile with the same ID throws `InvalidOperationException` naming both types and the conflicting ID.
- A unit test asserts this behavior.

### FR-5: Discovery test for all registered tiles
Add a single test in `backend/test/Anela.Heblo.Tests/Features/Dashboard/` that uses reflection to enumerate every `ITile` implementation in the application assembly and asserts:

**Acceptance criteria:**
- Each discovered `ITile` type can be instantiated through the DI container.
- Each instance returns a non-empty `TileId` that passes the FR-3 format validator.
- No two implementations return the same `TileId`.
- The test fails if a developer adds a new tile that violates any rule above.

### FR-6: Update all call sites
All references to the old `GetTileId()` extension methods (in `DashboardService.cs`, `TileRegistry.cs`, and any tests) must switch to the new property.

**Acceptance criteria:**
- `grep -r "GetTileId" backend/src` returns no results after the change (or only the interface property declaration if the name is reused).
- `dotnet build` succeeds.
- All existing dashboard tests pass without modification of assertions related to ID values.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The new `TileId` is a property returning a literal; it replaces an equally cheap reflection-based string derivation that already runs once per registration / lookup.

### NFR-2: Backward compatibility
No database migration. No API contract change (the existing `UserDashboardTileDto`, `GetAvailableTilesResponse`, etc. continue to expose the same string IDs). Frontend code that hardcodes any tile ID (if any) continues to work because the literal values do not change.

### NFR-3: Maintainability
A developer adding a new tile must:
1. Implement `ITile.TileId` returning a unique, lowercase, hyphen-allowed string.
2. Register the tile via `TileRegistryExtensions`.

Failure to step 1 is a compile error; collision with an existing ID is a test failure (FR-5) and a registration-time exception (FR-4).

### NFR-4: Testability
All new validation logic lives in code paths reachable by unit tests — no test that requires a running database.

## Data Model
No schema changes.

Existing entities (unchanged):
- `UserDashboardSettings` — keyed by `UserId`.
- `UserDashboardTile` — `TileId` (string, max 100, NOT NULL), `IsVisible`, `DisplayOrder`, FK to `UserDashboardSettings`. Unique index on `(UserId, TileId)`.

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

### `TileExtensions` (removed or reduced)
The `Type`-based and generic overloads are deleted. If a `this ITile` overload remains, it forwards to `tile.TileId` — no string manipulation.

### `TileRegistry.RegisterTile<TTile>()` (modified)
Resolves the tile via `_serviceProvider`, reads `TileId`, validates format (FR-3), checks for duplicates (FR-4), then stores in `_registeredTiles`.

Note: today `RegisterTile` works without resolving the instance (it derives from `typeof(TTile)`). To read the property the registry must resolve a temporary instance during registration (acceptable — registration happens once at startup and `IServiceProvider` is already injected).

### Tile implementations (modified)
Each concrete tile gains one line:
```csharp
public string TileId => "purchaseordersintransit";   // PurchaseOrdersInTransitTile
public string TileId => "backgroundtaskstatus";      // BackgroundTaskStatusTile
```

### No HTTP / REST changes
No controller, DTO, or response shape changes. The string values transmitted to the frontend are identical to today's values.

## Dependencies
- No new NuGet packages.
- No new external services.
- Affects: `Anela.Heblo.Xcc` (interface + registry + extensions), `Anela.Heblo.Application.Features.Dashboard.Tiles` (one tile), `Anela.Heblo.Xcc.Services.Dashboard.Tiles` (one tile), `Anela.Heblo.Tests.Features.Dashboard` (new test).

## Out of Scope
- Renaming any tile class (would change persisted IDs and require a data migration).
- Changing the database column type, length, or index.
- Frontend changes — tile IDs are an opaque string from the React side.
- Generalizing the same pattern to other "registry of plugins" code outside dashboards (e.g. background refresh tasks).
- Adding versioning, aliasing, or migration helpers for tile IDs.
- Localization of tile IDs (IDs remain technical, English, machine identifiers).

## Open Questions
None.

## Status: COMPLETE