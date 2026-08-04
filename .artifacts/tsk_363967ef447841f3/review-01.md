# Review — Photobank hooks migration off `(apiClient as any)`

## Verdict: done

## What was checked

Read plan-01.md, design-01.md, architecture-01.md, development-01.md, then read the actual diff
(commit `cda337a6`) file-by-file rather than trusting the development summary, and independently
re-ran the verification commands.

## Conformance to spec / architecture

- **FR-1/FR-3** — all 15 "plain" hooks in `usePhotobank.ts` and `usePhotobankSettings.ts` now call
  the matching generated `photobank_*` method via `getAuthenticatedApiClient()`. Verified each hook
  against the plan's table; all match (`photobank_GetPhotos`, `photobank_GetTags`,
  `photobank_AddPhotoTag`, `photobank_RemovePhotoTag`, `photobank_CreateTag`, `photobank_DeleteTag`,
  `photobank_BulkAddPhotoTagByIds`, `photobank_RetagPhotos`, `photobank_GetRoots`,
  `photobank_AddRoot`, `photobank_DeleteRoot`, `photobank_GetRules`, `photobank_AddRule`,
  `photobank_DeleteRule`, `photobank_ReapplyRules`). `getClientAndBaseUrl`/`apiFetch`/`apiPost`/
  `apiDelete`/`buildPhotosUrl` helpers and all `(apiClient as any)` access are deleted.
- **FR-2** — `useBulkAddPhotoTag` implements exactly the try/catch translation specified in the
  plan/architecture: happy path returns the typed response fields; catch discriminates on
  `err.success === false && typeof err.errorCode === "number"` (the guard architecture-01.md asked
  to preserve verbatim is present); anything else rethrows. `BulkTagDialog.tsx` required no changes,
  confirming the contract was preserved.
- **FR-4** — hand-declared `TagDto`/`PhotoDto`/`TagWithCountDto`/`GetPhotosResponse`/
  `IndexRootDto`/`TagRuleDto`/`ReapplyRulesResult` are gone; generated types are re-exported under
  the same names so existing import sites compile unchanged.
- **FR-5/ripple** — the three named `modifiedAt` call sites use `.toISOString() ?? ""` as specified.
  The wider ripple (all generated DTO fields being optional, not just the two `Date` fields) is
  real — confirmed by reading `api-client.ts`'s `PhotoDto`/`TagDto`/`IndexRootDto`/`TagRuleDto`
  declarations, all fields are `?`. The fixes applied (non-null assertions matching the codebase's
  existing 17-site convention, `?? []`/`?? ""` defaults, dropping redundant `new Date(...)` wrappers)
  are minimal, behavior-preserving, and consistently applied across all 9 touched component files.
  This is a reasonable, disclosed deviation from the plan, not scope creep.
- **FR-6** — both hook test files rewritten to mock `photobank_*` methods via
  `mockAuthenticatedApiClient()`/`createQueryClientWrapper()`, matching the `useBankStatements.test.ts`
  precedent the plan cited. `useBulkAddPhotoTag` has coverage for happy path, 2606 limit-exceeded
  translation, and unrecognizable-failure rethrow, as required by the plan's acceptance criteria.

## Independent verification (re-run, not just read from development-01.md)

- `grep -rn "apiClient as any" frontend/src/api/hooks/usePhotobank*.ts` → empty. ✅
- `CI=true npx react-scripts test --watchAll=false --testPathPattern="[Pp]hotobank"` → **16 suites /
  155 tests passed.** ✅
- `CI=true npm run build` → **Compiled successfully.** ✅
- `npm run lint` → Photobank-related findings are exactly `PhotoGrid.test.tsx:182` (`.parentElement`
  node access) and `PhotobankPage.selection.test.tsx:112/124` (`act()` wrapping). Checked these
  lines against `main` directly (`git show main:...`) — this exact code is unmodified pre-existing
  test code, not touched by this commit's diff. Confirmed not a regression. ✅
- Read the full diff for both hook files, all 9 modified component files, and the 2 fixture files
  spot-checked (`PhotoGrid.test.tsx`, `PhotobankPage.selection.test.tsx`) — changes are surgical,
  scoped to the ripple, and match what development-01.md describes.

## Assessment

No functional requirement is unmet, no architectural conflict, no missing required test coverage,
no correctness bug found. The one deviation from plan/design (the wider optional-field ripple) is
disclosed, justified by evidence (generated DTO source), and handled with the same minimal pattern
throughout rather than ad hoc fixes. This satisfies the arch-review issue's request to route both
Photobank hook files through the generated client and eliminate the private-field bypass.

```json
{"outcome": "done", "summary": "Both Photobank hook files now route through getAuthenticatedApiClient() and the generated photobank_* methods; (apiClient as any) is gone. Verified independently: grep clean, 155/155 Photobank tests pass, npm run build compiles successfully, and the two Photobank-adjacent lint findings are confirmed pre-existing (unchanged code on main). FR-1 through FR-6 all met per plan/design; the useBulkAddPhotoTag try/catch translation preserves BulkTagDialog's contract with the required guard intact. The wider Date/optional-field ripple beyond the plan's three named call sites is a disclosed, evidence-backed deviation handled consistently, not scope creep."}
```
