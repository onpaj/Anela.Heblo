# Implementation: extract-format-datetime

## What was implemented
Extracted the duplicated `formatDateTime` helper into a single co-located module and made both consumers import it.

## Files created/modified
- `frontend/src/components/pages/ExpeditionListArchive/formatDateTime.ts` - new named export (en dash U+2013 placeholder kept)
- `frontend/src/pages/ExpeditionListArchivePage.tsx` - removed local copy, added import
- `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` - removed local copy, added import

## Tests
- `__tests__/formatDateTime.test.ts` - null, empty string, fixed ISO string (date and time pattern, timezone-agnostic)

## How to verify
From `frontend/`: `CI=true npm test -- --watchAll=false ExpeditionList` (5 suites, 26 tests pass); eslint on touched files clean; `tsc --noEmit` shows no errors in `src/` (only pre-existing errors inside node_modules/react-i18next typings).

## Notes
`npm ci` needed `--legacy-peer-deps` in this environment. Full `npm run build` not run separately; tsc on src is clean.

## PR Summary
Deduplicated the `formatDateTime` helper used by the expedition list archive page and job controls bar into one shared module, with unit tests.

### Changes
- `frontend/src/components/pages/ExpeditionListArchive/formatDateTime.ts` - shared helper
- `frontend/src/pages/ExpeditionListArchivePage.tsx` - uses shared helper
- `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` - uses shared helper
- `frontend/src/components/pages/ExpeditionListArchive/__tests__/formatDateTime.test.ts` - tests

## Status
DONE
