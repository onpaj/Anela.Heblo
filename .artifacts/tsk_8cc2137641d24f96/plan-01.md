# Plan: Remove dead legacy API-client scaffolding in `frontend/src/services`

## Summary
`frontend/src/services/api.ts` and `frontend/src/services/generated/api-client.ts` are leftover scaffolding from the original frontend bootstrap, fully superseded by `frontend/src/api/` (client, generated OpenAPI client, TanStack Query hooks). Both are unreferenced anywhere in the codebase. This is a pure dead-code removal — no behavior change, no migration, nothing to redesign.

## Context
An architecture-review pass flagged these files as a duplicate API-client seam that risks a developer accidentally importing `apiClient` from `services/api.ts` (no auth header, no 401 handling, hardcoded `localhost:8080`, reads an env var — `REACT_APP_API_URL` — the app doesn't actually use) instead of the real client. Verification this step (grep across `frontend/src` and `frontend/test`, `git ls-files`, checking `package.json`/`scripts/regenerate-api-client.sh`) confirms all claims in the issue:

- `frontend/src/services/api.ts` — `ApiClient`/`apiClient`, zero importers anywhere.
- `frontend/src/services/generated/api-client.ts` — placeholder `AnelaHebloApiClient` stub with an unused `WeatherForecast` interface; zero importers. `scripts/regenerate-api-client.sh` targets `frontend/src/api/generated/api-client.ts` only — it has never written to `services/generated/`.
- `frontend/src/components/test/ApiTestComponent.tsx` — also zero importers (`TestApp.tsx`, the only file under `components/test/`, does not reference it).
- All three files are git-tracked (not build artifacts/gitignored), so straightforward `git rm` is correct.
- No dynamic or string-based imports of any of these paths exist (checked via grep for `services/api`, `services/generated`, `ApiTestComponent`, `require(`).

`frontend/src/services/versionService.ts` and its test are unrelated — `versionService.ts` already imports from the canonical `../api/client` — and must be left untouched.

## Functional requirements

**FR-1: Delete the dead legacy API client.**
- Remove `frontend/src/services/api.ts`.
- Acceptance: file no longer exists; `git grep -n "services/api['\"]"` and `git grep -n "ApiClient" frontend/src frontend/test` (excluding the canonical `api/client.ts` / generated client / hooks) return nothing referencing the removed file.

**FR-2: Delete the orphaned generated-client placeholder.**
- Remove `frontend/src/services/generated/api-client.ts` and `frontend/src/services/generated/.gitkeep` (remove the now-empty `services/generated/` directory).
- Acceptance: directory no longer exists; `git grep -n "AnelaHebloApiClient"` and `git grep -n "services/generated"` return nothing.

**FR-3: Delete the unused `ApiTestComponent`.**
- Remove `frontend/src/components/test/ApiTestComponent.tsx`.
- Acceptance: `git grep -n "ApiTestComponent"` returns nothing; `frontend/src/components/test/TestApp.tsx` (the only other file in that dir) is unaffected and continues to compile.

**FR-4: Leave `frontend/src/services/versionService.ts` and `frontend/src/services/__tests__/versionService.test.ts` untouched.**
- Acceptance: `git diff` shows no changes to these two files.

## Non-functional requirements
- No behavior change for any user-facing flow — these files are unreachable dead code.
- No new dependencies, no new abstractions.

## Data model
N/A — no entities involved.

## Interfaces
N/A — no endpoints/events/UI flows; this is file deletion only.

## Dependencies and scope
- Depends on nothing else landing first.
- In scope: the 3 files/dirs above (`services/api.ts`, `services/generated/` incl. `.gitkeep`, `components/test/ApiTestComponent.tsx`).
- Out of scope: `frontend/src/api/` (canonical client — untouched), `frontend/src/services/versionService.ts` (untouched, already correct), any backend changes, any change to `scripts/regenerate-api-client.sh`.

## Rough plan
1. `git rm frontend/src/services/api.ts`
2. `git rm frontend/src/services/generated/api-client.ts frontend/src/services/generated/.gitkeep` (directory becomes empty and drops from git automatically)
3. `git rm frontend/src/components/test/ApiTestComponent.tsx`
4. Re-run the repo-wide reference checks (`git grep` for `services/api`, `ApiClient` outside the canonical client, `AnelaHebloApiClient`, `services/generated`, `ApiTestComponent`) to confirm nothing else broke.
5. `npm run build` and `npm run lint` in `frontend/` to confirm the app still compiles cleanly with these files gone (this also catches any TypeScript project-reference or barrel-export surprises the grep might miss).
6. Run the frontend test suite (`versionService.test.ts` and the broader suite) to confirm nothing implicitly depended on these files (e.g. via Jest module mocks).

No design or architecture decisions are needed beyond what's already established — this is a straightforward, low-risk deletion. Downstream steps can proceed directly to implementation.

## Open questions
None — verification this step resolved the one open item the issue itself raised ("verify no dynamic/string-based import first": confirmed none exist).
