# Implementation: fix-datetime-utcnow

## What was implemented
Replaced two occurrences of `DateTime.Now` with `DateTime.UtcNow` in the `RegisterBackgroundRefreshTasks` method of `CatalogModule.cs`. This ensures the date boundary calculations for margin windows are timezone-independent, preventing off-by-one-day errors on hosts where the local timezone differs from UTC.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — line 310: `DateTime.Now.AddYears(-2)` → `DateTime.UtcNow.AddYears(-2)`; line 313: `DateTime.Now` → `DateTime.UtcNow` (inline comment preserved as-is)

## Tests
None — out of scope per spec

## How to verify
1. Run `git show HEAD` on the feature branch to confirm exactly two token changes in `CatalogModule.cs`.
2. Run `dotnet build Anela.Heblo.sln` — must produce 0 errors (pre-existing warnings are unrelated).
3. Inspect lines 310 and 313 in `CatalogModule.cs` for `DateTime.UtcNow`.

## Notes
No deviations. The solution file is at the worktree root (`Anela.Heblo.sln`), not under `backend/` as the task spec suggested — the build was run from the correct path.

## PR Summary
Fixes timezone-sensitive date boundary calculations in the catalog background refresh task. `DateTime.Now` returns local server time, which can differ from UTC by hours, causing the `twoYearsAgo` and `dateTo` date boundaries to land on the wrong calendar day depending on where the app is hosted. Switching to `DateTime.UtcNow` makes the calculation deterministic and consistent across environments (local dev, staging, production on Azure).

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — replaced `DateTime.Now` with `DateTime.UtcNow` on lines 310 and 313

## Status
DONE
