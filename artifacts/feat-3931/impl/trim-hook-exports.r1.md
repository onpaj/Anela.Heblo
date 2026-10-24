# Implementation: trim-hook-exports

## What was implemented
Removed all knip-reported unused exports/types under `frontend/src/api/hooks/*`:
- dropped the `export` keyword where the symbol is used locally;
- removed names from unused re-export lists (`export type { ... }` / `export { ... } from`);
- deleted symbols unused entirely (e.g. `useAllHistory`, `useTaskStatus`, `fetchAllHistory`, `fetchTaskStatus`, `useCatalogList`, `useMaterialsForPurchase`, `useInvoiceDetails`, `useManufactureLog`, `giftPackageQueryKeys`, `useTransportBoxesList`, `formatPercentage`, dead type aliases) plus imports left unused.
- `marketingAds/mappers.ts#wireKeyChecks` is a compile-time drift guard that is intentionally never imported; kept and marked `@public` so knip ignores it (the Wire types it uses are no longer exported).

## Files created/modified
67 files under `frontend/src/api/hooks/` (155 insertions, 539 deletions).

## Tests
No new tests; behaviour unchanged.

## How to verify
- `cd frontend && npx knip` reports no `src/api/hooks/*` entries.
- `npx tsc --noEmit`: no errors under src (node_modules d.ts noise is pre-existing in this env).
- `npx react-scripts test --watchAll=false src/api`: 69 suites, 535 tests pass.
- `npx eslint src/api/hooks`: 0 warnings; 3 pre-existing testing-library errors in __tests__.

## Notes
Dependencies installed with `npm ci --legacy-peer-deps` (peer conflict in this environment). Subagent tooling was unavailable, so work was done directly with scripted edits.

## PR Summary
Trimmed unused exports and types from the API hook modules so knip no longer reports any hooks entries. Symbols used only inside their file lose `export`; fully dead helpers and re-exports are deleted.

### Changes
- `frontend/src/api/hooks/*` -- removed unused exports/types/dead helpers; `marketingAds/mappers.ts` guard annotated `@public`

## Status
DONE
