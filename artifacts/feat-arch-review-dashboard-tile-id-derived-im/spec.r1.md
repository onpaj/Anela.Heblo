# Specification: Explicit, Stable Dashboard Tile Identifiers

## Summary
Replace the implicit, class-name-derived tile ID convention in `TileExtensions.GetTileId()` with an explicit, stable identifier declared on each tile class. The new mechanism must be backward compatible with all currently persisted `UserDashboardTiles.TileId` values, must fail loudly at application startup if any registered tile is missing or duplicates an ID, and must remove the silent coupling between C# class names and persisted database values.

## Background
The dashboard tile registry (`backend/src/Anela.Heblo.Xcc/Services/Dashboard`) currently derives a tile's persistent identifier from its CLR class name:

```csharp
// TileExtensions.cs:5
public static string GetTileId(this Type tileType) =>
    tileType.Name.ToLowerInvariant().Replace("tile", "");
```

These IDs are persisted per-user in the `UserDashboardTiles.TileId` column (`UserDashboardSettings` aggregate) and looked up by string at runtime in `GetTileDataHandler` (`backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs:58`). A missing ID surfaces only at request time as a `"Tile '<id>' not found"` payload — there is no compile-time signal.

This convention has already caused a production incident, evidenced by the hand-written migration `20251024072354_UpdateMaterialInventoryTileId.cs`, which had to delete and rewrite rows after a tile class was renamed. The convention has three concrete failure modes:

1. **Silent data corruption on rename.** Renaming the C# class (`PurchaseOrdersInTransitTile` → `OrdersInTransitTile`) changes the derived ID. All existing user rows orphan; affected tiles disappear and the user sees `"Tile not found"`.
2. **Substring `Replace` is greedy.** `string.Replace("tile", "")` removes every occurrence, not just the suffix. A hypothetical `TileImportTile` becomes `"import"`, colliding with any future `ImportTile`. No collision detection exists today.
3. **No compile-time or test-time guard.** A bulk rename refactor in an IDE will silently produce a green build and a green test suite while breaking every user's stored dashboard.

There are currently 12 tile classes implementing `ITile` (1 in `Anela.Heblo.Xcc`, 11 in `Anela.Heblo.Application`). The fix must apply uniformly to all of them and to any tile added in the future.

## Functional Requirements

### FR-1: Explicit tile ID declaration on every tile class
Each class implementing `ITile` must declare its persistent identifier explicitly using a `[TileId("...")]` attribute on the class.

```csharp
[TileId("purchaseordersintransit")]
public class PurchaseOrdersInTransitTile : ITile { ... }
```

The attribute lives in `Anela.Heblo.Xcc.Services.Dashboard` next to `ITile`. It accepts a single non-empty `string` and exposes it as a `Value` property. Its `AttributeUsage` is `AttributeTargets.Class`, `Inherited = false`, `AllowMultiple = false`.

**Acceptance criteria:**
- A new `TileIdAttribute` class exists in the `Anela.Heblo.Xcc.Services.Dashboard` namespace.
- All 12 existing tile classes carry the attribute with a value identical to the value previously produced by `tileType.Name.ToLowerInvariant().Replace("tile", "")` (preserving backward compatibility for persisted rows). Required values:
  - `BackgroundTaskStatusTile` → `"backgroundtaskstatus"`
  - `FailedJobsTile` → `"failedjobs"`
  - `InvoiceImportStatisticsTile` → `"invoiceimportstatistics"`
  - `WeatherForecastTile` → `"weatherforecast"`
  - `LowStockEfficiencyTile` → `"lowstockefficiency"`
  - `ManufactureConditionsTile` → `"manufactureconditions"`
  - `UpcomingProductionTile` → `"upcomingproduction"`
  - `ManualActionRequiredTile` → `"manualactionrequired"`
  - `CriticalGiftPackagesTile` → `"criticalgiftpackages"`
  - `TransportBoxBaseTile` → `"transportboxbase"`
  - `DqtYesterdayStatusTile` → `"dqtyesterdaystatus"`
  - `DataQualityStatusTile` → `"dataqualitystatus"`
  - `PurchaseOrdersInTransitTile` → `"purchaseordersintransit"`
  - `LowStockAlertTile` → `"lowstockalert"`
  - Plus any tile not in this list — its derived value must be inspected and pinned. (The pre-merge audit must enumerate every `ITile` implementor and verify the pinned attribute matches the current derived value before the deletion in FR-3.)

### FR-2: Lookup reads the attribute, not the class name
`TileExtensions.GetTileId(this Type tileType)` is replaced (or rewritten) so it returns the value of the `[TileId]` attribute on the type.

**Acceptance criteria:**
- `GetTileId(Type)`, `GetTileId<TTile>()`, and `GetTileId(this ITile)` all return the attribute value verbatim.
- If the attribute is absent or its value is null/whitespace, the method throws `InvalidOperationException` with a message naming the offending CLR type and pointing to the missing attribute.
- All existing call sites (`TileRegistry.RegisterTile<TTile>`, `TileRegistry.ToMetadata`) compile and behave identically for the 12 existing tiles.

### FR-3: Removal of the substring-based fallback
The legacy `tileType.Name.ToLowerInvariant().Replace("tile", "")` logic is deleted entirely. There is no fallback to class-name derivation. The intent is that *forgetting the attribute is a loud bug, not a silent miss*.

**Acceptance criteria:**
- A grep for `Replace("tile"` in the repository returns zero hits in production code after the change.
- No call path produces a tile ID without consulting the attribute.

### FR-4: Startup validation across all registered tiles
At application startup, after `IHost.InitializeTileRegistry()` has run, the registry validates the full set of registered tiles and refuses to start the application if any of the following hold:

- A registered tile type lacks `[TileId]`.
- A registered tile's `[TileId].Value` is null, empty, or whitespace.
- Two or more registered tile types share the same `[TileId].Value` (case-sensitive comparison; values are already lowercase by convention).

Validation failure throws an exception during `app.InitializeTileRegistry()` so the misconfiguration is detected before the first request. The exception message lists every offending type and the conflict reason (missing / empty / duplicate-of-X).

**Acceptance criteria:**
- Validation runs as part of `InitializeTileRegistry` in `TileRegistryExtensions.cs` (or an equivalent post-registration hook).
- A unit test registers two tiles with the same `[TileId]` and asserts initialization throws.
- A unit test registers a tile without the attribute and asserts initialization throws.
- Boot of the real application succeeds (validation passes for the 12 existing tiles).

### FR-5: Codebase-wide regression test
A test in `backend/test/Anela.Heblo.Tests/Features/Dashboard/` reflects over every concrete `ITile` implementation in `Anela.Heblo.Xcc` and `Anela.Heblo.Application` assemblies and asserts:

1. Each has `[TileId]`.
2. Each `[TileId].Value` is non-empty and lowercase.
3. No two share the same value.
4. (Backward-compat guard) For every tile class whose name still ends in `"Tile"`, the attribute value equals `className.ToLowerInvariant().Replace("tile", "")`. This guard catches accidental drift from the persisted-value contract and can be relaxed only via an explicit data migration (see FR-6).

**Acceptance criteria:**
- The test is independent of `ITileRegistry` registration order (operates by reflection over the assemblies).
- The test fails when a new tile is added without the attribute, runs in CI, and is part of the standard `dotnet test` suite.

### FR-6: Documented rename procedure
Add a short section to `docs/features/dashboard_tiles_implementation_guide.md` describing:

- The attribute is the source of truth for the persisted ID.
- Renaming a tile class is safe and does not require a migration *as long as the `[TileId]` value stays the same*.
- Changing a `[TileId]` value is a breaking data change and requires an EF Core migration that updates `UserDashboardTiles.TileId` from the old value to the new one (model the existing `20251024072354_UpdateMaterialInventoryTileId` migration). The FR-5 backward-compat guard must be updated in the same PR.

**Acceptance criteria:**
- The dashboard tiles implementation guide contains the section.
- The guide cites the existing `UpdateMaterialInventoryTileId` migration as the template.

## Non-Functional Requirements

### NFR-1: Performance
No runtime regression. The attribute lookup happens once per `RegisterTile<TTile>` call at startup and is then cached in the registry's `Dictionary<string, Type>`, exactly as the derived ID is today. Per-request paths (`GetTileMetadata`, `GetTileDataAsync`) are unchanged.

### NFR-2: Backward compatibility (data)
Zero changes to the database schema. Zero migrations to existing `UserDashboardTiles` rows. The pinned attribute values in FR-1 are exactly the strings already in production, so every existing user's tile configuration continues to resolve. No user-visible behavior change at deployment.

### NFR-3: Backward compatibility (frontend)
Zero changes to the OpenAPI surface. `DashboardTileDto.TileId`, `UserDashboardTileDto.TileId`, `EnableTileRequest.TileId`, `DisableTileRequest.TileId` keep their wire shape and values. No frontend code change required.

### NFR-4: Fail-fast over fail-soft
Misconfiguration must surface at application start, not at the first request that touches the affected tile. Throwing during `InitializeTileRegistry` is the chosen mechanism (consistent with the existing reflection-driven init in `TileRegistryExtensions.cs:29`).

### NFR-5: Discoverability
The `[TileId]` attribute is the single canonical place a developer looks to know a tile's persisted ID. No source-generators, no convention-over-configuration, no implicit lowercasing in the lookup path.

## Data Model
No changes.

- `UserDashboardTiles.TileId` (PostgreSQL `text`, persisted via `UserDashboardSettingsRepository`) — unchanged column and values.
- `UserDashboardSettings` aggregate — unchanged.
- `TileMetadata.TileId` (in-memory DTO) — unchanged.

## API / Interface Design

### New type: `TileIdAttribute`
Location: `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileIdAttribute.cs`

```csharp
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

### Rewritten: `TileExtensions`
Location: `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileExtensions.cs`

```csharp
public static class TileExtensions
{
    public static string GetTileId(this Type tileType)
    {
        var attr = tileType.GetCustomAttribute<TileIdAttribute>(inherit: false);
        if (attr is null)
            throw new InvalidOperationException(
                $"Tile type '{tileType.FullName}' is missing [TileId(\"...\")]. " +
                $"Every ITile must declare an explicit, stable identifier.");
        return attr.Value;
    }

    public static string GetTileId<TTile>() => typeof(TTile).GetTileId();
    public static string GetTileId(this ITile tile) => tile.GetType().GetTileId();
}
```

### Augmented: `TileRegistryExtensions.InitializeTileRegistry`
Adds a validation pass after the existing reflection-driven registration loop. Validation collects all registered tile IDs, checks for duplicates, and throws on conflict with a message that names every offender. The existing per-type missing/empty check happens inside `GetTileId` and propagates up through `RegisterTile`.

### Modified files (tile classes)
Every class implementing `ITile` gains a single `[TileId("<pinned-value>")]` attribute. No other behavior changes on those classes.

## Dependencies
- `System.Reflection` — already used by `TileRegistryExtensions`.
- No new NuGet packages.
- No changes to `Anela.Heblo.Persistence`, `Anela.Heblo.API`, frontend, or OpenAPI client.

## Out of Scope
- Database schema changes (column name, type, constraints).
- Migrating existing `UserDashboardTiles` rows to a different ID format.
- Renaming any tile class. (The pinned IDs preserve current values; class renames stay a separate, opt-in change.)
- Replacing the `string` tile ID with a strongly typed wrapper (`TileId` value object).
- Introducing a source generator to enforce the attribute at compile time.
- Frontend changes — tile IDs continue to flow as opaque strings.
- Refactoring `DashboardOptions`, `TileRegistry`, or tile registration ergonomics beyond what FR-2/FR-4 require.
- Changing `ITile` to expose `TileId` as an instance member. (Attribute-on-type is preferred so the registry can resolve the ID without instantiating the tile through DI, matching the existing `GetTileId(Type)` call sites in `TileRegistry`.)

## Open Questions
None.

## Status: COMPLETE