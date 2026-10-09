# Implementation: add-margin-mapping-coverage-tests

## What was implemented
Replaced `MarginsChart.utils.test.ts` with 7 characterization tests for `mapMarginDataToMonthlyArrays`: empty history, January year-boundary, January current-month exclusion, mid-year split across years, same-month/different-year exclusion, missing-record fallback, undefined-metric fallback. No implementation change.

## Files modified
- `frontend/src/components/catalog/detail/tabs/MarginsTab/__tests__/MarginsChart.utils.test.ts`

## Tests
- Targeted: 7/7 pass.
- Full frontend suite: 385 suites, 3350 passed, 5 skipped.
- `npm run build` succeeds. `eslint` on the MarginsTab folder is clean (repo-wide `npm run lint` has pre-existing errors in unrelated files).

## Status
DONE
