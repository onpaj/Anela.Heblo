# Code Review: relocate-print-fix-result-type-and-type-mutation

## Summary
This is a clean, minimal, type-only refactor exactly matching the spec: the orphaned `RunExpeditionListPrintFixResult` interface was removed from `useExpeditionListArchive.ts` and relocated directly above `useRunExpeditionListPrintFix` in `useExpeditionList.ts`, with explicit `useMutation<TData, TError, TVariables>` generics and a typed `mutationFn` return matching the sibling `usePrintExpeditionOrder` pattern. Independent verification (diff inspection, full-file reads, grep, `tsc --noEmit`, `npm run build`, `npm run lint`, and the relevant Jest suites) confirms no behavioral change and no regressions.

## Review Result: PASS

### task: relocate-print-fix-result-type-and-type-mutation
**Status:** PASS

**Verification performed:**
- `git show f72e97b` confirms the diff is exactly the two described hunks — no unrelated changes.
- Full contents of `useExpeditionList.ts` and `useExpeditionListArchive.ts` read: the interface is now declared directly above `useRunExpeditionListPrintFix` (lines 5-7), `useMutation<RunExpeditionListPrintFixResult, Error, void>` and `mutationFn: async (): Promise<RunExpeditionListPrintFixResult>` are in place, and the HTTP call/error-handling body is byte-for-byte unchanged. In the archive file, the dead interface and its blank line are gone, leaving a single blank line between `ReprintExpeditionListResponse` and `// --- Query Keys ---` (no double-blank-line artifact).
- `grep -rn "RunExpeditionListPrintFixResult" frontend/src` returns only 3 matches, all in `useExpeditionList.ts` (interface + generic + return type) — zero remaining references in the archive file or elsewhere.
- `ExpeditionListArchivePage.tsx` imports `useRunExpeditionListPrintFix` from `../api/hooks/useExpeditionList` (unchanged) and reads `result.totalCount` at the `handleRunFix` call site — this now type-checks against the concrete interface instead of `any`. The file was not touched, as required.
- `npx tsc --noEmit -p .` produces no errors attributable to either changed file or the consuming page (all errors present are pre-existing `react-i18next`/TypeScript version-mismatch noise in `node_modules`, unrelated to this change).
- `npm run build` completes with "Compiled successfully."
- `npm run lint` produces no output referencing either changed file or the consuming page.
- Ran the two directly relevant Jest suites (`useExpeditionList.test.ts`, `ExpeditionListArchivePage.test.tsx`): 2 suites / 16 tests, all passing (one pre-existing, unrelated `act()` warning from `ToastProvider` in an unrelated code path).

No functional requirement, architecture guidance item, or acceptance criterion from spec.r1.md / arch-review.r1.md is violated. No new tests were required (type-only change) and none were added, consistent with the spec.

## Docs to Update
None. This is an internal, non-behavioral TypeScript type refactor with no new public behavior, concepts, or operational changes.

## Overall Notes
Implementation is faithful to both the spec and the arch review, with no scope creep (the pre-existing `apiClient as any` casts and the `npm ci` peer-dependency conflict noted in the impl summary were correctly left untouched/unfixed, as instructed). Nothing further needed.
