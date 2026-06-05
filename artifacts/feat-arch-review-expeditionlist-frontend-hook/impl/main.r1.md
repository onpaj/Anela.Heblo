All 9 tasks complete. Here is the output summary:

---

# Implementation: Eliminate `as any` Casts in `useExpeditionListArchive.ts`

## What was implemented

Pure call-site migration replacing all 6 `(apiClient as any)` casts in the expedition list archive feature area with typed generated-client calls and public helpers. No backend changes, no OpenAPI regeneration, no new files (except a new test file).

## Files created/modified

- `frontend/src/api/hooks/useExpeditionListArchive.ts` — All 4 hooks migrated to typed methods (`expeditionListArchive_GetDates`, `expeditionListArchive_GetByDate`, `expeditionListArchive_Reprint`, `expeditionList_RunFix`). `getExpeditionListDownloadUrl` switched to `getApiBaseUrl()`. Local `ReprintExpeditionListRequest` interface dropped (replaced by generated class). New `RunExpeditionListPrintFixResult` interface added. `Date | undefined` → `string | null` DTO mapping in `useExpeditionListsByDate`.
- `frontend/src/pages/ExpeditionListArchivePage.tsx` — `handleOpen` switched from `(apiClient as any).http.fetch` to `getAuthenticatedFetch()`. Import swapped from `getAuthenticatedApiClient` to `getAuthenticatedFetch`.
- `frontend/src/api/__tests__/authenticated-api-usage.test.ts` — `"useExpeditionListArchive.ts"` added to `MIGRATED_HOOKS` Set (regression gate).
- `frontend/src/api/hooks/__tests__/useExpeditionListArchive.test.ts` — New test file. Happy-path + edge-case tests for all 4 hooks: 7 tests total.
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` — Mock updated to include `getAuthenticatedFetch: jest.fn(() => jest.fn())`.

## Tests

- `frontend/src/api/hooks/__tests__/useExpeditionListArchive.test.ts` — 7 tests covering `useExpeditionDates` (typed call + args), `useExpeditionListsByDate` (Date→string mapping, enabled guard), `useReprintExpeditionList` (instanceof check, error rethrow), `useRunExpeditionListPrintFix` (happy path, defaults).
- `frontend/src/api/__tests__/authenticated-api-usage.test.ts` — Existing guardrail now enforces zero `(apiClient as any)` in the migrated file.
- Full suite: **2,002 tests passing**, 248 suites.

## How to verify

```bash
cd frontend
npm run lint
npm run build
npm test -- --watchAll=false
grep -nE "as any|\.http\.|\.baseUrl" src/api/hooks/useExpeditionListArchive.ts  # expect: no output
grep -nE "\(apiClient as any\)" src/pages/ExpeditionListArchivePage.tsx           # expect: no output
```

## Notes

- The arch-review's Decision 2 (keep local DTO interfaces, map inside `queryFn`) was followed — re-exporting generated types was not safe because `createdOn` is `Date | undefined` in the generated class vs. `string | null` expected by page consumers.
- FR-6 (guardrail) was implemented via the existing `MIGRATED_HOOKS` Jest pattern (arch-review amendment A2) instead of a new ESLint rule.
- The sixth cast in `ExpeditionListArchivePage.tsx:handleOpen` (arch-review amendment A1) was fixed in Task 7.

## PR Summary

Replaced all six `(apiClient as any)` casts across the expedition list archive feature area with typed generated-client calls and public helper functions. The hook file now calls `expeditionListArchive_GetDates`, `expeditionListArchive_GetByDate`, `expeditionListArchive_Reprint`, and `expeditionList_RunFix` directly on the typed client; `getExpeditionListDownloadUrl` uses `getApiBaseUrl()`; and `handleOpen` in the page component uses `getAuthenticatedFetch()`. All five hook signatures are preserved so `ExpeditionListArchivePage` needs no consumer-side changes. A new test file covers the four refactored hooks with 7 tests, and the existing `MIGRATED_HOOKS` Jest guardrail is extended to prevent future regressions.

### Changes
- `frontend/src/api/hooks/useExpeditionListArchive.ts` — four hooks migrated to typed client, download URL helper fixed, local request interface dropped
- `frontend/src/pages/ExpeditionListArchivePage.tsx` — `handleOpen` cast removed, import swapped
- `frontend/src/api/__tests__/authenticated-api-usage.test.ts` — MIGRATED_HOOKS extended
- `frontend/src/api/hooks/__tests__/useExpeditionListArchive.test.ts` — new: 7 hook tests
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` — mock updated for new import

## Status
DONE