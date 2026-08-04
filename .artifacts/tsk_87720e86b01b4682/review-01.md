# Review — Expedition: replace `(apiClient as any).http.fetch` with typed generated client calls

## Verdict

**done.**

## What was checked

- Diff for `frontend/src/api/hooks/useExpeditionList.ts` and `useCarrierCooling.ts` matches `design-01.md` exactly: both files now call the typed generated client methods (`expeditionList_RunFix`, `expeditionList_PrintOrder`, `carrierCooling_GetMatrix`, `carrierCooling_SetCooling`) instead of `(apiClient as any).http.fetch` / `.baseUrl`.
- `grep -n "apiClient as any" frontend/src/api/hooks/useExpeditionList.ts frontend/src/api/hooks/useCarrierCooling.ts` → no matches. The rule violation cited in the task evidence is fully resolved.
- Response mapping matches the design's data schemas exactly: `{ totalCount: response.totalCount ?? 0 }`, the `BaseResponse` mapping for `usePrintExpeditionOrder`, and the `getMatrix`/`setCooling` enum-narrowing casts (`as unknown as <LocalUnion>`) with the aliased `GeneratedSetCarrierCoolingRequest` import to avoid a name collision.
- Confirmed `Carriers`/`DeliveryHandling`/`Cooling` are indeed generated as nominal TS `enum`s (`api-client.ts:17162/17213/17218`), which is the architecture step's stated justification for keeping the local string-literal unions and DTOs instead of deleting them per the plan's original FR-5 — the architecture step explicitly approved this supersession, and the reasoning holds up against the actual generated code.
- `grep -rn "CarrierCoolingRowDto\|GetCarrierCoolingMatrixResponse" frontend/src` shows the only non-generated definitions are the intentionally-kept local ones in `useCarrierCooling.ts`, consumed by `CarrierCoolingMatrix.tsx` — consistent with the design.
- Test diffs: `useExpeditionList.test.ts` rewritten to mock `expeditionList_RunFix`/`expeditionList_PrintOrder` on the client object (matching the `useExpeditionListArchive.test.ts` precedent) instead of `http.fetch`; new `useCarrierCooling.test.ts` added covering `getMatrix` success/defaulting and `setCooling` success/rejection. Both cover the previously-untested `usePrintExpeditionOrder` including its `success: false` business-failure path.
- Independently re-ran verification rather than trusting the development report:
  - `CI=false npm run build` → **Compiled successfully.**
  - `npx react-scripts test --watchAll=false` scoped to `useExpeditionList.test.ts|useCarrierCooling.test.ts|useExpeditionListArchive.test.ts|CarrierCoolingMatrix.test.tsx|ExpeditionListArchivePage.test.tsx` → **5 suites passed, 31 tests passed, 0 failed** (one pre-existing, unrelated `act()` console warning in `ExpeditionListArchivePage.test.tsx`, not a failure, not touched by this change).
  - `npx eslint` scoped to the two changed hook files and their two test files → zero output (no errors/warnings).
- No consumer files (`PrintOrderModal.tsx`, `CoolingTab.tsx`, `CarrierCoolingMatrix.tsx`, `ExpeditionListArchivePage.tsx`) were touched; spot-checked their read shapes are still satisfied by the new hook return types.
- Change is surgical: only the two target hook files plus their two test files were modified/added (`git diff main --stat` on source: 4 files). `useExpeditionListArchive.ts` (the reference implementation) was correctly left untouched.

## Non-blocking observations (from architecture-01.md, carried forward, not required to fix here)

1. Pre-existing backend inconsistency: `ExpeditionListController.RunFix`/`.PrintOrder` always return `Ok(...)` while `CarrierCoolingController.SetCooling` uses `HandleResponse` — correctly scoped out of this frontend-only task.
2. The `as unknown as <LocalUnion>` casts in `getMatrix`'s mapping bypass exhaustiveness checking if the backend adds a new enum member — low severity, fails soft (labels already have `?? fallback`), correctly flagged as a possible future follow-up rather than a blocker.

No functional requirement from the plan is unmet, the implementation follows the approved architecture without unapproved deviation, required tests exist and pass, and no correctness bugs were found.
