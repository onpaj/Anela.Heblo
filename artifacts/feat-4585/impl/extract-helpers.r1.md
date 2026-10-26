# Implementation: extract-helpers

## What was implemented
Moved `isOperationStuck` and `canRetry` from inline closures in `StockOperationsPage.tsx` into a new exported module; page now imports them. `isOperationStuck` takes an injectable `now` (default `new Date()`). Logic is unchanged.

## Files created/modified
- `frontend/src/pages/stockOperationsUtils.ts` - new, exports both helpers
- `frontend/src/pages/StockOperationsPage.tsx` - removed the two closures, added import

## Tests
None in this task (covered by test-helpers task).

## How to verify
`cd frontend && npx tsc --noEmit` (no src errors; pre-existing node_modules d.ts errors under installed TS are unrelated), `npx eslint` on both files is clean.

## Notes
Full `npm run build` not run: tsc reports pre-existing errors in node_modules/react-i18next typings in this environment, independent of this change. `npm ci` needed `--legacy-peer-deps`.

## PR Summary
Extracted stock operation stuck/retry helpers into a testable module.

### Changes
- `frontend/src/pages/stockOperationsUtils.ts` - new helpers
- `frontend/src/pages/StockOperationsPage.tsx` - uses imported helpers

## Status
DONE
