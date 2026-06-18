## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateManufactureDifficulty/UpdateManufactureDifficultyHandler.cs`

## Coverage
Line coverage: 22.4% (filter threshold: 60%)

## What's not tested
Three business-rule validation branches in the handler are uncovered:
1. **Not-found guard**: when `_repository.GetByIdAsync` returns null the handler returns `ManufactureDifficultyNotFound` — the error is never exercised, so a regression that skips this guard would silently update a non-existent record
2. **Date range validation**: `ValidFrom >= ValidTo` returns `InvalidValue` — boundary cases (equal dates, reversed dates) are not asserted; if the condition direction flips, valid requests would be rejected and invalid ones accepted
3. **Overlap detection**: `HasOverlapAsync` returning true returns `ManufactureDifficultyConflict` — the fact that `excludeId` is correctly passed (to exclude the record being updated from its own overlap check) is never verified

The successful path (update + catalog-cache refresh) is also uncovered.

## Why it matters
Manufacture difficulty settings control production cost calculations over date ranges. Accepting an overlapping range corrupts the difficulty history; allowing a ValidFrom ≥ ValidTo creates an always-empty or inverted range. Silently skipping the not-found check could produce a null-reference exception downstream.

## Suggested approach
Unit tests with mocked `IManufactureDifficultyRepository` and `ICatalogRepository`:
1. Happy path: existing record found, no overlap, update succeeds → assert DTO returned and cache refresh called
2. Not found: `GetByIdAsync` returns null → assert `ManufactureDifficultyNotFound` error code
3. Date range invalid: `ValidFrom = ValidTo` → assert `InvalidValue` with correct Params
4. Overlap exists: `HasOverlapAsync` returns true → assert `ManufactureDifficultyConflict` with product code
5. Verify `excludeId` is passed correctly to `HasOverlapAsync`
Effort: ~1.5 hours

---
_Filed by weekly coverage-gap routine on 2026-06-14. Based on CI run #27416879267 (3a6b7f99ee715caf82fc0efa17de5a5ede7b46fb)._