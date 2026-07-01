# Implementation: frontend-surface-import-outcome

## What was implemented
Extended the hand-written bank statement import response types to carry per-run
outcome counts, and updated `ImportTab.handleImportSubmit` to branch its completion
alert on those counts instead of always showing a fixed message.

## Files created/modified
- `frontend/src/api/hooks/useBankStatements.ts` — added `successCount`, `errorCount`,
  `totalCount`, `hasErrors` fields to both `BankStatementImportResult` and
  `BankImportResponse` interfaces (lines ~168-180).
- `frontend/src/components/customer/tabs/ImportTab.tsx` — `handleImportSubmit` now
  captures the `mutateAsync` result and shows one of three alert messages:
  - `totalCount === 0` → "žádné výpisy k importu"
  - `hasErrors` → success/error counts + "Zkontrolujte seznam výpisů"
  - otherwise → success count only

## Tests
No new automated tests added (task scope was type/UI-message only, per spec).
Manually verified the three alert branches read correctly by inspecting the
edited source (logic mirrors the exact spec text). Existing test suite for
these files was not run as part of validation (not requested); build/lint were
used as the verification gate per task instructions.

## How to verify
```
cd frontend
npm install --legacy-peer-deps   # required in this environment; CI already uses this flag
npm run build
npm run lint
```
Both commands were run and passed. Lint output shows only pre-existing errors/warnings
in unrelated test files (e.g. `MarketingActionModal.test.tsx`, `PhotoGrid.test.tsx`,
`useBankStatements.test.ts`'s unused import) — zero issues in the two files touched by
this task.

## Notes
- `npm ci` failed in this environment due to a pre-existing `react-i18next` /
  `typescript` peer-dependency conflict unrelated to this change. Used
  `npm install --legacy-peer-deps`, matching the flag already used in
  `.github/workflows/*.yml` CI jobs.
- No changes made to the generated API client, consistent with the task note that
  this task does not depend on it.
- Implementation follows the spec's exact code blocks verbatim; no deviations.

## Status
DONE
