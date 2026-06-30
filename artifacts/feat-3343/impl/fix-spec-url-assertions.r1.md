# Implementation: fix-spec-url-assertions

## What was implemented
Changed `.toContain('/stock-operations')` to `.toContain('/stock-up-operations')` in the `test.beforeEach` block of all 7 stock-operations E2E spec files, correcting the URL assertion to match the actual route.

## Files created/modified
- `frontend/test/e2e/stock-operations/badges.spec.ts` — line 15: /stock-operations → /stock-up-operations
- `frontend/test/e2e/stock-operations/accept.spec.ts` — line 13: /stock-operations → /stock-up-operations
- `frontend/test/e2e/stock-operations/state-filter.spec.ts` — line 14: /stock-operations → /stock-up-operations
- `frontend/test/e2e/stock-operations/source-filter.spec.ts` — line 13: /stock-operations → /stock-up-operations
- `frontend/test/e2e/stock-operations/sorting.spec.ts` — line 14: /stock-operations → /stock-up-operations
- `frontend/test/e2e/stock-operations/retry.spec.ts` — line 15: /stock-operations → /stock-up-operations
- `frontend/test/e2e/stock-operations/panel.spec.ts` — line 18: /stock-operations → /stock-up-operations

## Tests
N/A — these are test file fixes.

## How to verify
Run grep to confirm no `/stock-operations` remains in the stock-operations spec dir:
```
grep -r "toContain('/stock-operations')" frontend/test/e2e/stock-operations/
```
Should return no matches.

## Notes
No deviations. All edits were identical single-line changes in `beforeEach` blocks. Committed as a single atomic commit: `7ebdcda fix(e2e): correct URL assertions in stock-operations spec files`.

## PR Summary

### Changes
- 7 spec files — corrected URL assertion from /stock-operations to /stock-up-operations

## Status
DONE
