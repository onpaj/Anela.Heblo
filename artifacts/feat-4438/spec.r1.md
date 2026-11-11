# Spec: Deduplicate formatDateTime in ExpeditionListArchive (issue #4438)

## Problem
`formatDateTime(iso: string | null)` is defined identically in `frontend/src/pages/ExpeditionListArchivePage.tsx` (L25-34) and `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` (L19-28).

## Requirements
- R1: One single definition, co-located at `frontend/src/components/pages/ExpeditionListArchive/formatDateTime.ts`, exported as named `formatDateTime`.
- R2: Both files import it; local copies removed.
- R3: Behavior unchanged: null/empty -> en dash "–" (U+2013); otherwise `toLocaleString("cs-CZ", {day,month,year:"2-digit/numeric", hour, minute 2-digit})`.
- R4: Add a unit test for the helper.

## Non-goals
Do not migrate to `utils/formatters.ts` (see arch review); no changes to other modules' copies.

## Acceptance
- grep finds exactly one definition in the ExpeditionListArchive area.
- Existing tests (ExpeditionJobControlsBar "Další běh: –", ExpeditionListArchivePage) pass; `npm run build` and `npm run lint` pass.
