# PR Context

- **PR**: #3196 — test(ManufactureOrderExtensions): add unit tests for lot number and expiration date
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3196
- **Branch**: `feature/feat-3076` → `main`
- **State**: OPEN
- **Author**: onpaj
- **Changes**: +136 / -0 across 1 file
- **Absorbed**: already up to date with `main`; CI workflow fix committed and pushed

## Description

`ManufactureOrderExtensions.cs` had 49% line coverage (threshold: 60%). Added
`ManufactureOrderExtensionsTests.cs` with 16 pure unit tests covering:
- `GetDefaultLot` (5 Theory cases: week-padding, ISO-year-boundary, Sunday edge cases)
- `GetDefaultExpiration` (5 Theory cases: leap year, 31→30-day transition, year-boundary)
- `SetDefaultLot` / `SetDefaultExpiration` (6 Fact tests, one per entity type)

All 16 tests pass locally.

## CI fix applied

The `claude-review.yml` workflow was failing with HTTP 404 because the pinned
action defaulted to the deprecated model `claude-sonnet-4-20250514`. Fixed by
adding `anthropic_model: claude-sonnet-4-6` and removing the invalid `pr_number`
input. Committed (`2d05f39b`) and pushed to `feature/feat-3076` to re-trigger CI.

Closes #3076
