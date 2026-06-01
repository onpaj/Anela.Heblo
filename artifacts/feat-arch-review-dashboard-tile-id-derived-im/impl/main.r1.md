The working tree is clean. The entire implementation is complete and committed. Here's the final output summary:

---

# Implementation: Explicit, Stable Dashboard Tile Identifiers

## What was implemented

All 8 tasks from the plan were completed in prior sessions. This session verified correctness — build passes and all tests pass on the current branch.

- **FR-1: `TileIdAttribute`** — sealed attribute class in `Anela.Heblo.Xcc.Services.Dashboard`, enforces non-empty value, `Inherited=false, AllowMultiple=false`.
- **FR-2: `TileExtensions.GetTileId` rewrite** — reads `[TileId]` attribute, throws `InvalidOperationException` naming the offending type if missing. Three overloads all route through attribute lookup.
- **FR-3: Legacy `Replace("tile","")` deleted** — zero hits in production code.
- **FR-4: Startup validation** — `TileRegistryExtensions.ValidateTileTypes()` runs during `InitializeTileRegistry`, collects all missing-attribute and duplicate-ID errors and throws once with the full list.
- **FR-5: Reflection-driven contract tests** — `TileIdContractTests` scans `Anela.Heblo.Xcc` and `Anela.Heblo.Application` assemblies (abstract types excluded), asserts presence, lowercase, uniqueness, and backward-compat parity with the legacy derived value.
- **FR-6: Rename procedure docs** — appended to `docs/features/dashboard_tiles_implementation_guide.md`, cites `20251024072354_UpdateMaterialInventoryTileId` as template.

## Files created/modified

- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileIdAttribute.cs` — new sealed attribute class
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileExtensions.cs` — rewritten to read attribute
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileRegistryExtensions.cs` — added `ValidateTileTypes` + call in `InitializeTileRegistry`
- 22 concrete production tile classes — each got `[TileId("pinned-value")]`
- 6 test fixture tiles (`TrackedTile`, `NewAutoShowTile`, `ManualTile`, `AutoTile1`, `AutoTile2`, `TestTileWithData`) — each got `[TileId("...")]`
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs` — new FR-5 reflection test
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileRegistryValidationTests.cs` — new FR-4 startup validation tests
- `docs/features/dashboard_tiles_implementation_guide.md` — FR-6 rename procedure section

## Tests

- `TileIdContractTests` (4 facts): attribute presence, lowercase, uniqueness, backward-compat parity
- `TileRegistryValidationTests` (3 facts): duplicate IDs throw, missing attribute throws, valid tiles pass
- All 116 dashboard tests: **Passed**

## How to verify

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Dashboard"
# grep for legacy logic — should return nothing:
grep -r 'Replace("tile"' backend/src --include="*.cs"
```

## Notes

The abstract base classes (`TransportBoxBaseTile`, `UpcomingProductionTile`, `InventoryCountTileBase`, `InventorySummaryTileBase`) intentionally do NOT have `[TileId]` — the arch review's Amendment 1 corrected the spec's erroneous inclusion of these. The contract test's `!t.IsAbstract` filter handles them correctly.

## PR Summary

Replace implicit, class-name-derived tile IDs with explicit `[TileId("...")]` attributes on every concrete `ITile` implementation. This eliminates silent data corruption on class renames (the production incident that required the `UpdateMaterialInventoryTileId` migration), removes the greedy `Replace("tile","")` logic, and adds startup validation that refuses to boot if any tile is missing an ID or two tiles share one.

All 22 concrete production tiles and 6 test fixture tiles now carry pinned attributes matching their previously-derived IDs, preserving all existing `UserDashboardTiles` rows without a migration. A reflection-driven CI test (`TileIdContractTests`) enforces presence, lowercase format, uniqueness, and backward-compat parity for any tile added in future PRs.

### Changes
- `TileIdAttribute.cs` — new sealed attribute class (source of truth for persisted tile IDs)
- `TileExtensions.cs` — rewritten to read attribute; throws at call site if attribute is absent
- `TileRegistryExtensions.cs` — added `ValidateTileTypes` with duplicate detection, called during `InitializeTileRegistry`
- 22 production tile classes — each annotated with `[TileId("pinned-value")]`
- 6 test fixture tiles — annotated to keep tests green
- `TileIdContractTests.cs` — new reflection-driven regression suite (FR-5)
- `TileRegistryValidationTests.cs` — new startup validation tests (FR-4)
- `dashboard_tiles_implementation_guide.md` — rename procedure section (FR-6)

## Status
DONE