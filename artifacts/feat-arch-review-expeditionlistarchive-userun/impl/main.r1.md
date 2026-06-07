Final reviewer says **Ready to merge**. All tasks complete, writing the output artifact.

---

# Implementation: Relocate `useRunExpeditionListPrintFix` to ExpeditionList Module

## What was implemented

A pure source-file relocation of the `useRunExpeditionListPrintFix` React Query mutation hook from `useExpeditionListArchive.ts` (wrong module — it calls `/api/expedition-list/run-fix` owned by `ExpeditionListController`) to a new correctly-named `useExpeditionList.ts` file. The page import and Jest mocks were updated accordingly. Zero behavioral change.

## Files created/modified

- `frontend/src/api/hooks/useExpeditionList.ts` — NEW (26 lines); exports `useRunExpeditionListPrintFix`
- `frontend/src/api/hooks/useExpeditionListArchive.ts` — MODIFIED; 24 lines deleted (the misplaced hook export)
- `frontend/src/pages/ExpeditionListArchivePage.tsx` — MODIFIED; import block split so `useRunExpeditionListPrintFix` comes from `useExpeditionList`
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` — MODIFIED; jest.mock factory and require() updated to point at `useExpeditionList`

## Tests

- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` — all 4 existing tests pass: `renders the refresh button`, `invalidates expedition archive queries when refresh is clicked`, `disables the refresh button while invalidation is in progress`, `re-enables the refresh button after invalidation completes`

## How to verify

```bash
# From repo root — verify no consumer imports from old location
grep -rn "from .*useExpeditionListArchive" frontend/src | grep "useRunExpeditionListPrintFix"
# Should produce no output

# Verify exactly 6 occurrences (1 definition, 2 page, 3 test)
grep -rn "useRunExpeditionListPrintFix" frontend/src

# From frontend/
npm run build    # should succeed
npm run lint     # should pass
npx jest --no-coverage  # all tests pass
```

## Notes

- 4 commits on the current branch: `19ecb525`, `5435dd47`, `082146a8`, `760db5f1`
- The `(apiClient as any)` casts in the hook are preserved verbatim — they match the existing pattern in sibling hooks and changing them is out of scope
- Pre-existing `react-i18next` type errors in node_modules did not affect the TypeScript check outcome; `npm run build` (which includes a full tsc compile) succeeded cleanly

## PR Summary

Relocates `useRunExpeditionListPrintFix` from the archive module hook file to a new `useExpeditionList.ts` that correctly owns endpoints under `/api/expedition-list/...`. The hook called `ExpeditionListController.RunFix` but lived in a file dedicated to `ExpeditionListArchiveController` hooks, creating a module boundary leak flagged in the 2026-06-04 architecture review.

The fix is a zero-behavior-change relocation: new file created, duplicate removed, single page import updated, Jest mock factory redirected to the new module. All 4 archive page tests continue to pass; build and lint are clean.

### Changes
- `frontend/src/api/hooks/useExpeditionList.ts` — new file; owns hooks for `/api/expedition-list/...` endpoints
- `frontend/src/api/hooks/useExpeditionListArchive.ts` — removed misplaced hook export (24 lines)
- `frontend/src/pages/ExpeditionListArchivePage.tsx` — import redirected to new module
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` — mock and require updated for new module path

## Status
DONE