# Implementation: harden-transport-box-list-error-state

## What was implemented

Restructured `TransportBoxList.tsx` so the page header (`<h1>Transportní boxy</h1>`) and the primary action buttons ("Otevřít nový box", "Obnovit") render in a shell shared by `isLoading`/`error`/success states, per the architecture review's Decision 3. Previously, an early `if (error) return (...)` replaced the entire page with a standalone red alert box, dropping the header and action buttons — reproducing the same "h1/button never found" E2E signature for any future transient API failure, independent of the FR-1 permission-gap root cause.

Three edits, in order:
1. Removed the early `if (error) return (...)`, moved the header into a flex row containing both the `<h1>` and the two action buttons (always visible now, full text — no longer collapsed to icon-only), and wrapped the rest of the page body in `error ? (<red alert box>) : (<>...success/loading content...</>)`.
2. Removed the now-duplicated "Otevřít nový box"/"Obnovit" button pair from inside the collapsible controls block (they'd otherwise render twice in the success path).
3. Closed the new fragment/ternary right before the `TransportBoxDetail` modal (which continues to render in all three states, unchanged).

Added one new test to `TransportBoxList.test.tsx`'s existing "Error state" describe block asserting the `h1`, "Otevřít nový box", and "Zkusit znovu" are all present when the query errors.

## Files created/modified

- `frontend/src/components/pages/TransportBoxList.tsx` — header/error-state restructure (3 edits, no changes to `isLoading` or empty-results branches, no changes to the collapsible filters/summary-cards logic itself).
- `frontend/src/components/pages/__tests__/TransportBoxList.test.tsx` — one new test added to the existing "Error state" describe block.

## Tests

- New test `"should still render the page header and primary action button when the query errors"` — passes.
- Full `TransportBoxList*` suite (all 3 files: `TransportBoxList.test.tsx`, `TransportBoxList.stockUpGate.test.tsx`, `TransportBoxList.touch.test.tsx`): `Test Suites: 3 passed, 3 total / Tests: 31 passed, 31 total` — zero regressions, no changes needed to the other two files.
- `npm run build`: `Compiled successfully.`
- `npm run lint`: zero new lint errors/warnings from either changed file (confirmed via `npm run lint | grep -i TransportBoxList` — no matches). The 148 pre-existing lint errors reported project-wide are all in unrelated files (financial-overview, terminal, leaflet-generator tests, etc.) and predate this change.

## How to verify

```bash
cd frontend
CI=true npx react-scripts test src/components/pages/__tests__/TransportBoxList --watchAll=false
npm run build
```

## Notes

- Needed to run `npm install --legacy-peer-deps` in this worktree first — it's a fresh git worktree checkout with no `node_modules`, unrelated to this task's code change.
- The `isControlsCollapsed ? "" : "..."` icon-only collapsing behavior was intentionally dropped for these two buttons per the task spec's explicit instruction — they now live in the always-visible header row, not squeezed next to filter chips, so there's no reason to shorten them.

## PR Summary

Hardened `TransportBoxList`'s error-state rendering so the page header and primary "Otevřít nový box"/"Obnovit" action buttons always render, regardless of query loading/error/success state. Previously a query error blanked the whole page, which would reproduce the exact "h1/button never found" E2E failure signature for any future transient API error — this closes that latent gap as a defense-in-depth measure alongside the FR-1 permission fix.

### Changes
- `frontend/src/components/pages/TransportBoxList.tsx` — header/action-bar hoisted above the error/loading/success branch; error branch now only replaces the content region
- `frontend/src/components/pages/__tests__/TransportBoxList.test.tsx` — new test asserting header + buttons survive a query error

## Status
DONE
