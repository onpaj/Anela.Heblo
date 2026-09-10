# Code Review: wire-controls-bar-into-page

## Summary
The implementation cleanly separates archive-browsing responsibility from job-control logic by extracting all recurring-job triggers, print-fix mutations, permission checks, and the PrintOrderModal into the self-contained `ExpeditionJobControlsBar` component. The page file correctly imports and renders the component with no props in the header, and all acceptance criteria are satisfied.

## Review Result: PASS

### task: wire-controls-bar-into-page
**Status:** PASS

### Acceptance Criteria Verification

**Import confinement (FR-2 & FR-4):**
- ✓ `grep -n "useExpeditionList\"" frontend/src/pages/ExpeditionListArchivePage.tsx` returns NO matches — the problematic cross-module import of `useRunExpeditionListPrintFix` is gone
- ✓ `grep -n "useRecurringJobs\|PermissionsContext\|PrintOrderModal" frontend/src/pages/ExpeditionListArchivePage.tsx` returns NO matches — all three are completely removed from the page
- ✓ TypeScript type-check (`npx tsc --noEmit -p tsconfig.json`) produces only pre-existing deprecation warnings, no NEW errors in the page file
- ✓ `formatDateTime` helper is deliberately duplicated in the page file (line 25) and still used in the "Nahráno" column (line 208), per explicit spec requirement

**Component integration:**
- ✓ `ExpeditionJobControlsBar` imported from correct path: `"../components/pages/ExpeditionListArchive/ExpeditionJobControlsBar"`
- ✓ Rendered with no props: `<ExpeditionJobControlsBar />`
- ✓ Placed in header before "Obnovit" button (gap-4 flex container, correct order)
- ✓ Component is a default export and self-contained (owns all job-trigger imports, state, mutations, permission checks, and PrintOrderModal)

**Behavior preservation:**
- ✓ Archive browsing unchanged: date sidebar pagination (lines 119–168), items table with formatDateTime in "Nahráno" column (lines 170–237), open/reprint actions (lines 215–228)
- ✓ Reprint confirmation dialog intact (lines 240–271)
- ✓ Refresh button functional (lines 106–113)
- ✓ No standalone `<PrintOrderModal>` render remains in the page

**Scope compliance:**
- ✓ Test-file fixes explicitly deferred to `update-archive-page-tests` task — not penalized

## Overall Notes
The refactor achieves complete import confinement as specified. The controls bar component correctly owns all job-control state and API calls (recurring jobs, print-fix trigger, permissions, modal lifecycle), while the page retains only archive-browsing queries and UI. The shared QueryClient invalidation on print success (component line 86) ensures seamless synchronization without prop threading or callbacks. Implementation is minimal, surgical, and adheres to the architecture guidelines.
