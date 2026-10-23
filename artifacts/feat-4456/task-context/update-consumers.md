### task: update-consumers
- `frontend/src/pages/ExpeditionListArchivePage.tsx`: change imports of `ExpeditionJobControlsBar` and `formatDateTime` to `../components/pages/Expedition/...`.
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx`: update `jest.mock` path to `../../components/pages/Expedition/ExpeditionJobControlsBar`.
- Remove the empty `ExpeditionListArchive` component folder.
Check: no file under `components/pages/ExpeditionListArchive/` remains; `grep -rn "components/pages/ExpeditionListArchive" frontend/src` is empty.

