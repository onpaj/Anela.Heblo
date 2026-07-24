# Implementation: regenerate-client-and-update-frontend-hook

## What was implemented
Regenerated the OpenAPI TypeScript client (`bankStatements_GetBankStatements` now types its four date params as `Date | null | undefined` instead of `string | null | undefined`, and its URL-building now calls `.toISOString()` on them) and updated `useBankStatementsList` in `useBankStatements.ts` to convert the hook's string date inputs to `Date` objects before calling the generated client, mirroring the existing `useBankStatementImport` pattern. Added test coverage for the new conversion behavior.

## Files created/modified
- `frontend/src/api/generated/api-client.ts` — regenerated via `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual`. **Scoping note:** the full regeneration also picked up unrelated, already-merged-to-main backend drift (a new `meetingTasks_UpdateStatus` endpoint and a `participants` field on `MeetingTranscriptDto`) that the previously-committed generated file hadn't caught up to yet — this is pre-existing drift from another feature, not something this task's backend changes caused. Since that's out of scope for this issue (a Bank date-field retype) and per the architecture review's explicit "do not hand-edit the generated file" guidance, I did not hand-write any client code; instead I generated fresh, then used `git apply` with a hand-extracted patch containing only the `bankStatements_GetBankStatements` hunks (produced by `git diff`, not typed by hand) to scope the commit to exactly this task's surface, leaving the pre-existing meeting-tasks drift untouched for whoever picks that up separately. Net diff: 10 lines (5 insertions, 5 deletions), confined to the `bankStatements_GetBankStatements` method.
- `frontend/src/api/hooks/useBankStatements.ts` — `useBankStatementsList`'s four date arguments now wrap `request?.statementDate`/`importDate`/`dateFrom`/`dateTo` in `new Date(...)` (guarded by a truthy check, `undefined` otherwise), matching the task context and the `useBankStatementImport` precedent. The `GetBankStatementListRequest` TS interface (string-typed) and the hook's public string-based contract to `ImportTab.tsx` are unchanged.
- `frontend/src/api/hooks/__tests__/useBankStatements.test.ts` — added `useBankStatementsList` to the import, and a new `describe('useBankStatements - List Query', ...)` block with two tests: one verifying the string→Date conversion for `dateFrom`/`dateTo`, one verifying `undefined` passthrough when all four date fields are absent.

## Tests
- `useBankStatements.test.ts` → `useBankStatements - List Query`: 2 new tests, both passing (7/7 total in the file, including the 5 pre-existing account-listing tests).

## How to verify
```bash
cd frontend
CI=true npx react-scripts test src/api/hooks/__tests__/useBankStatements.test.ts --watchAll=false
npm run build
npm run lint
```

## Notes
- `npm ci` initially failed on a pre-existing peer-dependency conflict (`react-i18next` requires `typescript@^5`, root `package.json` pins `typescript@^4.9.5`) — unrelated to this change. Installed with `npm ci --legacy-peer-deps` to proceed; did not modify `package.json`/`package-lock.json`.
- `dotnet msbuild ... -t:GenerateFrontendClientManual` initially failed with "Run `dotnet tool restore`..." — ran `dotnet tool restore` (restores the pinned `nswag.consolecore` tool) first, then regeneration succeeded.
- `npm run build`: compiled successfully (only pre-existing bundle-size and browserslist-staleness warnings, unrelated to this change).
- `npm run lint`: 161 pre-existing problems (148 errors, 13 warnings) across ~15 unrelated test files (testing-library rule violations, an unused import, an import-order issue) — none in any file this task touched (`useBankStatements.ts`, `useBankStatements.test.ts`, `api-client.ts` are absent from the lint output entirely). Pre-existing lint debt, out of scope for this issue.
- Confirmed `bankStatements_GetBankStatements(` has exactly one caller in `frontend/src` besides its own definition (`useBankStatements.ts`), so no other consumer is affected by the parameter-type change.
- `ImportTab.tsx` required no changes, as expected — it still passes strings into `useBankStatementsList`, matching the unchanged `GetBankStatementListRequest` TS interface.

## Status
DONE
