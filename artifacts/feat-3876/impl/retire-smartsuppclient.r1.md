# Implementation: retire-smartsuppclient

## What was implemented
Deleted `frontend/src/api/smartsuppClient.ts` now that every Smartsupp hook (FR-1 through FR-6) routes through the generated typed API client. Confirmed no remaining references to `smartsuppClient` (as an import/usage) or `asInternal` anywhere in `frontend/src`. Ran the full frontend verification pass (build, lint, targeted tests), performed the NFR-3 compile-time spot-check (temporarily renamed `BaseResponse.Success` → `SuccessX`, regenerated the TS client, confirmed the frontend build breaks at compile time rather than silently returning `undefined` at runtime, then fully reverted), and ran the final full-repo verification (frontend build/lint/full test suite, backend build/format).

## Files created/modified
- `frontend/src/api/smartsuppClient.ts` — deleted (52 lines; unused internal `asInternal`/`apiGet`/`apiPost`/`apiDelete`/`getClientAndBaseUrl` helpers that reached into private fields of the generated API client).
- `frontend/src/components/customer-support/smartsupp/VisitorInfoCard.tsx` — fixed a pre-existing (already present before this task's changes, confirmed via `git stash` diff against `ada877f`) TS18048 compile error: `VisitorInfoDto.pages` is `pages?: VisitorPageDto[]` in the generated client, but the component read `pages.slice(...)` / `pages.length` without a null guard. This blocked `npm run build` outright, independent of the smartsuppClient.ts deletion, so it had to be fixed to get a green build as required by the task's own verification steps. Fix: default to `pages ?? []`.

## Tests
- `CI=true npx react-scripts test src/api/hooks/__tests__ src/components/customer-support/smartsupp --watchAll=false` — 70 suites / 510 tests, all passed (Step 3).
- `CI=true npx react-scripts test --watchAll=false` (full suite) — 313 suites / 2607 passed, 5 skipped (Step 6).
- No test files were added or modified; existing `VisitorInfoCard.test.tsx` continues to pass with the null-guard fix.

## How to verify
```bash
cd frontend
grep -rn "smartsuppClient" src --include="*.ts" --include="*.tsx"   # no import/usage hits (one harmless string-literal check remains, see Notes)
grep -rn "asInternal" src --include="*.ts" --include="*.tsx"        # no hits
npm run build                                                        # compiles clean
CI=true npx react-scripts test --watchAll=false                     # 313 suites pass

cd ../backend
dotnet build                                                         # 0 errors (run from repo root — no backend/*.sln)
dotnet format --verify-no-changes                                    # pre-existing unrelated failure, see Notes
```

## Notes
- **Dependency install required `--legacy-peer-deps`.** `node_modules` was not present in the worktree; plain `npm install`/`npm ci` fail with an ERESOLVE conflict between `typescript@4.9.5` (root) and `react-i18next@15.7.4`'s peer `typescript@^5`. CI workflows (`ci-feature-branch.yml`, `ci-main-branch.yml`) use `npm install --legacy-peer-deps`, so I used the same flag. Not a code change, just an environment note.
- **Pre-existing `npm run lint` failures (out of scope).** `npm run lint` reports 193 problems (180 errors, 13 warnings) spread across ~20 unrelated files (mostly `testing-library/no-node-access` and similar rules in test files). Verified identical before and after my change via `git stash`/`git stash pop` against `ada877f`. No GitHub Actions workflow in this repo actually invokes `npm run lint`, so this is dormant repo-wide lint debt, not something introduced or reasonably fixable within this surgical task's scope.
- **Pre-existing `dotnet format --verify-no-changes` failure (out of scope).** Reports 4 WHITESPACE errors in `backend/test/Anela.Heblo.Tests/Application/Overtime/GetMonthlyStatementsHandlerTests.cs:117-118`, a file untouched by this or any prior task in this feature. Confirmed identical content in commit `ada877f` (before this task started). Unrelated to Smartsupp/BaseResponse work.
- **Backend build/format target.** The task's Step 6 says `cd backend && dotnet build`, but there is no `.sln`/project file directly in `backend/` — the solution `Anela.Heblo.sln` lives at the repo root (matching `docs/development/setup.md`, which just says `dotnet build` from the root). I ran `dotnet build` and `dotnet format --verify-no-changes` from the repo root instead; both operate on the same solution.
- **Step 4 spot-check touched far more files than the task template anticipated.** The task assumed only `BaseApiController.HandleResponse` and `BaseResponse`'s own constructors would need `Success` → `SuccessX` touch-ups to get a clean backend build. In practice ~104 files across `Anela.Heblo.API` and `Anela.Heblo.Application` read/write `BaseResponse.Success` (every controller and most MediatR handlers), so the temporary rename required fixing all of them to get `dotnet build` green. Two enum members (`StockUpResultStatus.Success`, `ConsumeInventoryOutcome.Success`) were incidentally caught by a broad regex pass and had to be corrected back — these are unrelated enums, not `BaseResponse` properties. All of this was mechanical, temporary, and fully reverted via `git diff --name-only -- src | xargs git checkout --` (backend) and `git checkout -- src/api/generated/api-client.ts` (frontend); confirmed clean via `git diff --stat` (empty) and a final `npm run build` pass before proceeding to Step 5. The frontend build did fail at compile time as expected (in `useCompletePackingOrder.ts`, the first file the compiler hit — not specifically in a Smartsupp hook, since literally every `BaseResponse`-derived DTO shares the renamed field, not just Smartsupp's), which is the desired NFR-3 confirmation.
- Commit `fd85429` contains only the intended two file changes (the deletion and the null-guard fix); nothing from the Step 4 spot-check leaked into the commit. `artifacts/feat-3876/state.json` shows as modified in the working tree but was not touched by me and was left unstaged/uncommitted (pipeline-managed state file, out of this task's file scope).

## PR Summary
Deletes `frontend/src/api/smartsuppClient.ts`, the last remaining escape-hatch client that reached into the generated API client's private fields (`asInternal`/`.http.fetch`/`.baseUrl`). All Smartsupp hooks now go through `getAuthenticatedApiClient()`'s typed methods, so this file had no remaining callers. Also fixes an unrelated, pre-existing null-safety compile error in `VisitorInfoCard.tsx` that was blocking `npm run build`. Verified the migration's core motivation (NFR-3) holds: temporarily renaming `BaseResponse.Success` on the backend and regenerating the TS client makes the frontend fail to compile immediately, rather than silently returning `undefined` at runtime — confirming DTO field renames are now caught at build time.

### Changes
- `frontend/src/api/smartsuppClient.ts` — deleted (dead code, no remaining callers).
- `frontend/src/components/customer-support/smartsupp/VisitorInfoCard.tsx` — null-guard `pages` (optional in generated DTO) to unblock `npm run build`.

## Status
DONE_WITH_CONCERNS
