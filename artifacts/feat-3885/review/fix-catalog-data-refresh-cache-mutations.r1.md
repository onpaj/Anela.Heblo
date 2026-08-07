# Code Review: Fix cache isolation violations in CatalogDataRefreshService

## Summary
The implementation matches the spec, arch-review, and design exactly: both `RefreshManufactureDifficultySettingsData`'s single-product branch and `RefreshManufactureCostData` now build new collections and install them via `SetManufactureDifficultySettingsData`/`ReplaceCacheAtomicallyAsync` instead of mutating live cached objects in place. Tests directly verify the isolation contract (pre-call references untouched, post-call reads reflect the update) rather than only asserting on the end state, which is the right test shape for a bug about reference mutation.

## Review Result: PASS

### task: fix-catalog-data-refresh-cache-mutations
**Status:** PASS

Verification performed:
- Read the diff in `CatalogDataRefreshService.cs` line-by-line against FR-1 and FR-2 in `spec.r1.md` and the exact code shape proposed in `design.r1.md` — matches.
- Confirmed `new Dictionary<string, List<ManufactureDifficultySetting>>(existingDict) { [product] = ... }` produces a new dictionary instance (container identity changes) while untouched entries' inner `List<ManufactureDifficultySetting>` references are shared — correct per Decision 2 in `arch-review.r1.md` (only the touched product needs isolation, not a full deep clone of everything).
- Confirmed the snapshot-update step is correctly guarded by `current != null && productAggregate != null`, matching FR-1's "no current snapshot" and "product not present" acceptance criteria without throwing.
- Confirmed `RefreshManufactureCostData` clones only products present in `manufactureMap`, passing the rest through unchanged by reference (safe, since they're never mutated) — matches Decision 3.
- Confirmed the stale in-code comment ("Pre-existing behavior: mutates live aggregate in-place...") was removed.
- Confirmed no public signatures changed (`ICatalogRepository`, `CatalogCacheStore`).
- Ran the full build (`dotnet build` from repo root): 0 errors, no new warnings introduced by the changed files.
- Ran `CatalogDataRefreshServiceTests` (126/126 pass), including the two new isolation tests and the regenerated single-product test.
- Ran the broader `Catalog` test filter (813/817 pass); the 4 failures are `GetStockUpOperationsSummaryIntegrationTests`, pre-existing Testcontainers/Docker-unavailable failures in this sandbox, unrelated to `CatalogDataRefreshService`/`CatalogCacheStore`/`CatalogMergeService` — confirmed by inspecting the failing tests' file and stack trace (`PostgresSharedContainerFixture`, Docker endpoint error, no reference to the changed class).
- `CatalogCacheStoreTests` and `CatalogMergeServiceTests` required no changes and pass unmodified, confirming no API-surface regression on `CatalogCacheStore`.
- `dotnet format --verify-no-changes` on the two changed files reports no formatting issues.

No correctness bugs, no missed acceptance criteria, no architecture deviations found.

## Docs to Update
(None — this is an internal bug fix with no public interface, CLI, config, or operational behavior change. `CLAUDE.md`, `docs/architecture/*`, and `docs/features/*` do not reference this internal cache-mutation detail.)

## Overall Notes
None. The fix is minimal, matches the pre-established `Merge()` pattern exactly, and the new tests would have caught the original bug (they fail against the pre-fix code, as verified during TDD execution of the task plan).
