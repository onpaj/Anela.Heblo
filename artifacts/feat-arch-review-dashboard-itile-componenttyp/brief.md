## Module
Dashboard

## Finding

`ITile` declares a `Type ComponentType { get; }` property (`Xcc/Services/Dashboard/ITile.cs`, line 10). Every implementation in the codebase sets it to `typeof(object)` with a comment saying it is not needed:

- `PurchaseOrdersInTransitTile.cs:17` — `public Type ComponentType => typeof(object); // Frontend component type not needed for backend`
- `TransportBoxBaseTile.cs:17` — same
- All other tiles examined follow the same pattern

The value is carried through the entire stack:
- `TileMetadata` record (`Xcc/Services/Dashboard/TileMetadata.cs`, line 11): `Type ComponentType`
- `TileData` class (`Xcc/Services/Dashboard/TileData.cs`, line 12): `public Type ComponentType { get; set; } = typeof(object);`
- `GetTileDataHandler` maps it into `TileData` (`Application/.../GetTileData/GetTileDataHandler.cs`, line ~84): `ComponentType = tile.ComponentType`

But `DashboardTileDto` (`Application/Features/Dashboard/Contracts/DashboardTileDto.cs`) has no `ComponentType` field. The value is populated at every intermediate step and then silently dropped when mapping to the DTO — it never reaches the API response or the frontend.

## Why it matters

The property pollutes the `ITile` contract (every tile author must implement a property that does nothing), inflates `TileMetadata` and `TileData`, and creates a false impression that the backend manages frontend component wiring. It is pure noise.

## Suggested fix

Remove `ComponentType` from `ITile`, `TileMetadata`, `TileData`, and every tile implementation. The frontend already resolves the correct component from `tileId` via the `switch` in `TileContent.tsx` — no backend involvement needed.

---
_Filed by daily arch-review routine on 2026-06-01._