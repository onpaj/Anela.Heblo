Plan saved to `artifacts/feat-arch-review-dashboard-tile-id-derived-im/task-plan.r1.md`.

**Plan summary:** 8 tasks, TDD-ordered to keep the build green at every checkpoint.

1. **Task 0 (pre-flight):** verify legacy derivation rule, enumerate 22 concrete tiles + 4 abstract bases + 6 fixture tiles, capture build/test baseline.
2. **Task 1:** create sealed `TileIdAttribute` (class-only, `Inherited=false`, non-empty guard).
3. **Task 2:** add `[TileId("pinned-value")]` to all 22 concrete production tiles — pinned values exactly match the current `Replace("tile","")` derivation, so persisted user rows continue resolving (NFR-2).
4. **Task 3:** add `[TileId]` to 6 test fixture tiles so step 4 doesn't break them.
5. **Task 4:** TDD rewrite of `TileExtensions.GetTileId` to read the attribute, throw on missing — deletes the legacy `Replace("tile","")` entirely (FR-3).
6. **Task 5:** TDD add `internal static ValidateTileTypes(IReadOnlyList<Type>)` to `TileRegistryExtensions`, call it at the top of `InitializeTileRegistry`. Reports missing + duplicates in one boot-time exception (FR-4). Helper is a pure function over `Type[]` — sidesteps the static `ConcurrentBag` pollution hazard flagged in arch-review Amendment 4.
7. **Task 6:** reflection-driven `TileIdContractTests` scanning only the two production assemblies with `!t.IsAbstract` filter (Amendments 2 + 3). Four assertions: presence, lowercase, uniqueness, backward-compat parity with legacy derivation.
8. **Task 7:** append rename-procedure section to `docs/features/dashboard_tiles_implementation_guide.md` (FR-6) citing the existing `20251024072354_UpdateMaterialInventoryTileId` migration as the template.
9. **Task 8:** final `dotnet build` + `dotnet format` + `dotnet test` + boot smoke + grep checks.

Spec/arch coverage matrix included in the self-review section. Skipping the execution choice prompt per pipeline instructions.