# Development: Remove dead legacy API-client scaffolding in `frontend/src/services`

## Summary

Implemented the plan/design/architecture-review exactly as scoped: a pure dead-code removal of the duplicate API-client seam, plus the one optional item the architecture review recommended folding in (the orphaned scripts that generate the placeholder).

## Files removed

| File | Reason |
|---|---|
| `frontend/src/services/api.ts` | Dead legacy `ApiClient`/`apiClient` — zero importers, hardcoded `localhost:8080`, no auth/401 handling. (FR-1) |
| `frontend/src/services/generated/api-client.ts` | Orphaned placeholder `AnelaHebloApiClient` stub — zero importers, not the canonical generated client. (FR-2) |
| `frontend/src/services/generated/.gitkeep` | Only other file in the now-removed `services/generated/` directory; directory drops from git automatically. (FR-2) |
| `frontend/src/components/test/ApiTestComponent.tsx` | Unused test component — zero importers, `TestApp.tsx` doesn't reference it. (FR-3) |
| `frontend/scripts/check-api-and-generate.js` | Orphaned script — the sole source of the `AnelaHebloApiClient`/`WeatherForecast` placeholder content just deleted; not wired into `package.json`, `.github/`, or `Dockerfile*`. Included per architecture-01.md's recommended (not required) FR-5, since leaving it behind would recreate the exact dead seam if ever re-wired into a build step. |
| `frontend/scripts/generate-api-client.js` | Companion script to the above, same rationale, same zero-reference status. |

`frontend/src/services/versionService.ts` and `frontend/src/services/__tests__/versionService.test.ts` were left untouched, as required by FR-4 — confirmed via `git diff` (no changes) and the test suite (still 9/9 passing).

## Verification performed

1. **Pre-deletion grep sweep** — confirmed zero importers of all six removed paths across `frontend/src`, `frontend/test`, `.github/`, `Dockerfile*`, and `frontend/package.json` (no dynamic/string-based imports either).
2. **Post-deletion grep sweep** (per design-01.md §Verification design):
   - `git grep -n "services/api['\"]" -- frontend/src frontend/test` → no output
   - `git grep -n "services/generated" -- frontend/src frontend/test` → no output
   - `git grep -n "AnelaHebloApiClient"` → only historical doc/artifact mentions of the *unrelated* backend-generated `AnelaHebloApiClient.cs` (canonical NSwag C# client) and other tasks' artifacts — nothing referencing the deleted files
   - `git grep -n "ApiClient" -- frontend/src frontend/test | grep -v frontend/src/api/` → only the canonical `getAuthenticatedApiClient` call sites and `versionService.ts`/its test, all importing from `../api/client`
   - `git grep -n "ApiTestComponent"` → only artifact/plan docs describing this task, no code
   - `git grep -n "check-api-and-generate\|generate-api-client"` → only doc references to the unrelated `npm run generate-api-client` (not a defined script) and the canonical root-level `scripts/regenerate-api-client.sh` (untouched, different file)
3. **`npm run build`** (`frontend/`) — compiled successfully, no TypeScript or webpack errors.
4. **`npm run lint`** (`frontend/`) — 188 problems (175 errors, 13 warnings), all in files unrelated to this change (testing-library rule violations in various `__tests__` files). Verified via `git stash`/`git stash pop` that this exact count exists identically on the pre-change tree — 100% pre-existing debt, not introduced or worsened by this change. Lint is not gated in CI (`ci-feature-branch.yml`/`ci-main-branch.yml` have no lint step).
5. **`npm test -- --watchAll=false`** (full suite) — 294/300 suites passed, 2497/2514 tests passed. The 6 failing suites (12 failing tests) are all timezone/date-arithmetic tests (`resolve.test.ts`, `chartDataMapping.test.ts`, `fullcalendarAdapters.test.ts`) unrelated to the API-client seam. Verified via `git stash`/`git stash pop` that the identical failures occur on the pre-change tree — pre-existing flakiness (likely host-timezone-dependent), not introduced by this change.
   - `versionService.test.ts` (the one file that stays in `services/`): 9/9 passed, unaffected.
6. **Backend check** — `git grep` across `backend/` for all removed path fragments returns nothing; this is a frontend-only change.

## Outcome

All FR-1 through FR-4 acceptance criteria from `plan-01.md` are met, plus the architecture review's recommended orphaned-script cleanup. No behavior change for any user-facing flow. `frontend/src/api/` remains the single API-client seam.

## How to verify

```bash
cd frontend
npm run build              # compiles clean
npm test -- --watchAll=false --testPathPattern=services   # versionService.test.ts: 9/9 pass
git grep -n "services/api\['\"]\|services/generated\|AnelaHebloApiClient\|ApiTestComponent\|check-api-and-generate\|generate-api-client.js"   # only stale historical artifact/doc mentions, no live code references
```
