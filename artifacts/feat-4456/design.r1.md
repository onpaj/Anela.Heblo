# Design: Relocate ExpeditionJobControlsBar

## Component Design
- `components/pages/Expedition/ExpeditionJobControlsBar.tsx` (moved, unchanged logic; props `{ refreshButton?: ReactNode }`).
- `components/pages/Expedition/formatDateTime.ts` (moved).
- Tests moved to `components/pages/Expedition/__tests__/`.
- `ExpeditionListArchive/` retains no files other than possibly empty; remove the folder if empty.

## Data Schemas
None.
