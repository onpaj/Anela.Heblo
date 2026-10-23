### task: move-components
Move files with `git mv` from `frontend/src/components/pages/ExpeditionListArchive/` to `frontend/src/components/pages/Expedition/`:
- `ExpeditionJobControlsBar.tsx`
- `formatDateTime.ts`
- `__tests__/ExpeditionJobControlsBar.test.tsx` and `__tests__/formatDateTime.test.ts` (into `Expedition/__tests__/`)

Relative import depth is unchanged (`../../../`), so internal imports of the component need no edits; the `./formatDateTime` import stays valid. Verify the test files' relative imports (`../ExpeditionJobControlsBar`, `../formatDateTime`) still resolve and any `jest.mock` paths inside them.
Check: `grep -rn "ExpeditionListArchive/" frontend/src` shows only the page-level references still to fix.

