# Implementation: relocate-print-fix-result-type-and-type-mutation

## What was implemented
Moved the `RunExpeditionListPrintFixResult` interface out of `useExpeditionListArchive.ts` (where it was dead/orphaned) into `useExpeditionList.ts`, directly above `useRunExpeditionListPrintFix`, which is the hook that actually produces/consumes it. Added explicit generics `<RunExpeditionListPrintFixResult, Error, void>` to the `useMutation` call and an explicit `Promise<RunExpeditionListPrintFixResult>` return type on `mutationFn`, matching the existing `usePrintExpeditionOrder` pattern in the same file. No runtime/behavioral changes: HTTP method, headers, endpoint path, and error handling are unchanged.

## Files created/modified
- `frontend/src/api/hooks/useExpeditionListArchive.ts` — deleted the orphaned `RunExpeditionListPrintFixResult` interface (and the blank line it left behind between `ReprintExpeditionListResponse` and the `// --- Query Keys ---` comment). No other changes.
- `frontend/src/api/hooks/useExpeditionList.ts` — added the `RunExpeditionListPrintFixResult` interface directly above `useRunExpeditionListPrintFix`; gave `useMutation` explicit generics `<RunExpeditionListPrintFixResult, Error, void>` and gave `mutationFn` an explicit `Promise<RunExpeditionListPrintFixResult>` return type. Body of the mutation (URL construction, `apiClient as any` casts, fetch call, error handling) is untouched.

## Tests
N/A — type-only change verified by build/lint. `ExpeditionListArchivePage.tsx` already imports `useRunExpeditionListPrintFix` from `useExpeditionList` (not from the archive hooks file), so no consumer changes were needed; it type-checks cleanly against the new explicit `totalCount: number` return type with no `any` involved.

## How to verify
1. `grep -rn "RunExpeditionListPrintFixResult" frontend/src` — only `frontend/src/api/hooks/useExpeditionList.ts` matches (3 lines: interface declaration, `useMutation` generic, `mutationFn` return type).
2. `cd frontend && npm run build` — compiles successfully, no new type errors (had to `npm ci --legacy-peer-deps` first since `node_modules` was absent in this worktree and `react-i18next` has a `typescript@^5` peer conflict with the pinned `typescript@4.9.5` — pre-existing, unrelated to this change).
3. `cd frontend && npm run lint` — 148 pre-existing errors / 14 pre-existing warnings, all in unrelated test files (testing-library rules, unused imports, import ordering). Neither `useExpeditionList.ts`, `useExpeditionListArchive.ts`, nor `ExpeditionListArchivePage.tsx` appear anywhere in the lint output — confirmed via grep against the full lint log — so this change introduces zero new lint issues.
4. `git diff` on both files shows only the minimal, spec-matching edits (no reordering, no unrelated formatting churn).

## Notes
- `node_modules` did not exist in this worktree; `npm ci` failed with an ERESOLVE conflict (`react-i18next@15.7.4` wants `typescript@^5`, project pins `typescript@4.9.5`). Used `npm ci --legacy-peer-deps` to install, which is an environment-setup workaround only — no `package.json`/`package-lock.json` changes were made. This conflict is pre-existing and out of scope for this task; noting it here for visibility.
- Per the task's explicit out-of-scope list, `ExpeditionListArchivePage.tsx` was not touched — it already imports from the correct module and required no changes.
- `artifacts/` directory was left untouched per instructions (not staged/committed), except that `artifacts/feat-3476/state.json` showed as pre-existing modified in git status before this task began; it was not staged in this task's commit.

## PR Summary
Relocates the `RunExpeditionListPrintFixResult` type from `useExpeditionListArchive.ts` (where it was unused/orphaned) to `useExpeditionList.ts`, right next to the `useRunExpeditionListPrintFix` hook that actually returns it. The hook's `useMutation` call now has explicit generics and an explicit `mutationFn` return type instead of relying on implicit `any`, matching the sibling `usePrintExpeditionOrder` hook's pattern. Purely a type-level refactor — no HTTP behavior, error handling, or endpoint changed.

### Changes
- `frontend/src/api/hooks/useExpeditionListArchive.ts` — removed the dead `RunExpeditionListPrintFixResult` interface.
- `frontend/src/api/hooks/useExpeditionList.ts` — added `RunExpeditionListPrintFixResult`, added explicit `useMutation<RunExpeditionListPrintFixResult, Error, void>` generics and `mutationFn` return type.

## Status
DONE
