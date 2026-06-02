## Module
Dashboard

## Finding
`TileExtensions.GetTileId()` (`backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileExtensions.cs`, line 5) derives the tile's stable identifier from the class name at runtime:

```csharp
public static string GetTileId(this Type tileType) =>
    tileType.Name.ToLowerInvariant().Replace("tile", "");
```

Examples:
- `PurchaseOrdersInTransitTile` → `"purchaseordersintransit"`
- `BackgroundTaskStatusTile` → `"backgroundtaskstatus"`

These string IDs are persisted in the database (`UserDashboardTile.TileId` column via `UserDashboardSettingsRepository`) and stored in user settings. The mapping is entirely implicit — there is no constant, attribute, or migration that documents the expected value.

Consequences:
1. **Renaming a tile class silently breaks all existing user dashboard settings** stored in the DB. The new class gets a new auto-derived ID; old rows become orphaned ("tile not found" at runtime, lines 141–151 in `DashboardService`).
2. The `Replace("tile", "")` call removes **all** occurrences of the substring, not just a suffix. A hypothetical `TileImportTile` → `"import"`. This is surprising and can produce collisions.
3. There is no compile-time check or test that catches an accidental rename.

## Why it matters
- User data is silently corrupted on refactoring without any compiler or test signal.
- The convention violates the principle of explicit, stable identifiers for persisted values.
- Makes the codebase fragile to routine rename refactors.

## Suggested fix
Give each tile an explicit, stable ID constant and make `GetTileId()` read it:

```csharp
// On each tile class:
public const string TileId = "purchaseordersintransit";
string ITile.GetTileId() => TileId;

// Or via attribute:
[TileId("purchaseordersintransit")]
public class PurchaseOrdersInTransitTile : ITile { ... }
```

The extension method or interface default can then enforce that the constant is present, giving a compile-time guarantee. A single test scanning all registered tile types for duplicate or empty IDs would catch accidental collisions.

---
_Filed by daily arch-review routine on 2026-05-28._