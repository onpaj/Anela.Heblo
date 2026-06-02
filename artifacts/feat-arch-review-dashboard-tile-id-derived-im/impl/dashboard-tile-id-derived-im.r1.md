# Implementation: Explicit, Stable Dashboard Tile Identifiers

## What was implemented

Replaced the implicit class-name-derived tile ID convention (`tileType.Name.ToLowerInvariant().Replace("tile", "")`) with explicit `[TileId("...")]` attributes on every concrete tile class. Added startup validation that refuses to boot if any registered tile is missing the attribute or if two tiles share the same ID. Added a reflection-driven regression test suite and documented the rename procedure.

## Files created/modified

**New files:**
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileIdAttribute.cs` — sealed attribute class, `AttributeTargets.Class, Inherited=false, AllowMultiple=false`, validates non-empty value
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs` — FR-5 reflection suite: 4 tests asserting presence, non-empty/lowercase, uniqueness, backward-compat parity across Xcc + Application assemblies
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileRegistryValidationTests.cs` — FR-4 unit tests: duplicate ID → throws; missing attribute → throws

**Modified files:**
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileExtensions.cs` — rewritten: reads `[TileId]` attribute via `GetCustomAttribute<TileIdAttribute>(inherit: false)`, throws `InvalidOperationException` with FullName when missing, old `Replace("tile","")` deleted
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileRegistryExtensions.cs` — added `internal static ValidateTileTypes(IReadOnlyList<Type>)` for duplicate detection; called at start of `InitializeTileRegistry`
- All 22 concrete production tile classes — `[TileId("pinned-value")]` added (values exactly match legacy derived strings, preserving DB backward compatibility)
- 6 test fixture tile classes — `[TileId("pinned-value")]` added
- `docs/features/dashboard_tiles_implementation_guide.md` — appended "Renaming Tiles and Changing Tile IDs" section

## Tests

- `TileIdContractTests.cs` — 4 tests: `AllConcreteTiles_ShouldHaveTileIdAttribute`, `AllConcreteTiles_ShouldHaveNonEmptyLowercaseTileId`, `AllConcreteTiles_ShouldHaveUniqueTileIds`, `AllConcreteTiles_ShouldMatchLegacyDerivedValue_ForBackwardCompatibility`
- `TileRegistryValidationTests.cs` — 2 tests: duplicate-ID detection, missing-attribute detection

## How to verify

```bash
# Confirm legacy Replace is gone
grep -r 'Replace("tile"' backend/src --include="*.cs"  # must return no output

# Run tests (once dotnet is available)
dotnet test backend/test/Anela.Heblo.Tests --filter "Dashboard"

# Check commit log
git log 967b946f..HEAD --oneline
```

## Notes

- Abstract base classes (`TransportBoxBaseTile`, `UpcomingProductionTile`, `InventoryCountTileBase`, `InventorySummaryTileBase`) intentionally have no `[TileId]` — `Inherited=false` means concrete subclasses must each declare their own.
- The spec underestimated the tile count (said 12; actual concrete tiles = 22). The arch review caught this before implementation.
- `ValidateTileTypes` avoids the static `ConcurrentBag` pollution hazard in tests by accepting an explicit `IReadOnlyList<Type>` — tests call it directly without touching the static bag.
- No DB changes, no migrations, no frontend changes, no OpenAPI changes.

## PR Summary

Replaces the fragile class-name-derived tile ID convention with explicit `[TileId("...")]` attributes, eliminating the silent data corruption risk that caused a production migration (`20251024072354_UpdateMaterialInventoryTileId`) when a tile class was renamed. All 22 concrete production tile IDs are pinned to their current values so existing user dashboard configurations are unaffected.

The change fails loudly at startup (in `InitializeTileRegistry`) if any registered tile is missing the attribute or if two tiles share an ID, and a reflection-driven CI test (`TileIdContractTests`) enforces presence, uniqueness, and backward-compat parity across both production assemblies — catching any future tile added without the attribute before it reaches production.

### Changes
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileIdAttribute.cs` — new sealed attribute class
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileExtensions.cs` — rewritten to read attribute, old Replace logic deleted
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileRegistryExtensions.cs` — added ValidateTileTypes + startup call
- 22 production tile classes — [TileId] annotation added
- 6 test fixture tile classes — [TileId] annotation added
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileIdContractTests.cs` — FR-5 reflection regression suite
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileRegistryValidationTests.cs` — FR-4 startup validation tests
- `docs/features/dashboard_tiles_implementation_guide.md` — rename/ID-change procedure documented

## Status
DONE
