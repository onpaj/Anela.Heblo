# Task Plan: Relocate ExpeditionJobControlsBar (#4456)

### task: move-components
Move files with `git mv` from `frontend/src/components/pages/ExpeditionListArchive/` to `frontend/src/components/pages/Expedition/`:
- `ExpeditionJobControlsBar.tsx`
- `formatDateTime.ts`
- `__tests__/ExpeditionJobControlsBar.test.tsx` and `__tests__/formatDateTime.test.ts` (into `Expedition/__tests__/`)

Relative import depth is unchanged (`../../../`), so internal imports of the component need no edits; the `./formatDateTime` import stays valid. Verify the test files' relative imports (`../ExpeditionJobControlsBar`, `../formatDateTime`) still resolve and any `jest.mock` paths inside them.
Check: `grep -rn "ExpeditionListArchive/" frontend/src` shows only the page-level references still to fix.

### task: update-consumers
- `frontend/src/pages/ExpeditionListArchivePage.tsx`: change imports of `ExpeditionJobControlsBar` and `formatDateTime` to `../components/pages/Expedition/...`.
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx`: update `jest.mock` path to `../../components/pages/Expedition/ExpeditionJobControlsBar`.
- Remove the empty `ExpeditionListArchive` component folder.
Check: no file under `components/pages/ExpeditionListArchive/` remains; `grep -rn "components/pages/ExpeditionListArchive" frontend/src` is empty.

### task: validate
In `frontend`: `npm run build`, `npm run lint`, and `npx jest ExpeditionJobControlsBar formatDateTime ExpeditionListArchivePage --watchAll=false`. All must pass. No docs/process change needed (no behaviour change); check `docs/architecture/filesystem.md` for references to the old path and update if present.
