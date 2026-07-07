# Implementation: regenerate-openapi-clients-and-verify

## What was implemented
Synced the frontend TypeScript OpenAPI client with the trimmed `GetMarginReportRequest` contract from task 1. Ran the project's `GenerateFrontendClientManual` MSBuild target to regenerate `frontend/src/api/generated/api-client.ts` against the running API. The regeneration also surfaced unrelated drift between the checked-in generated file and the current API surface (an already-merged `packaging_GetStatistics` endpoint, a reordered enum, and a new error code that predate this change) — those unrelated diffs were reverted so only the `analytics_GetMarginReport` signature change lands in this PR, per the project's surgical-changes rule. The unrelated drift is pre-existing and out of scope here.

There is no backend C# OpenAPI client in this repo: `backend/src/Anela.Heblo.API.Client/Generated/` contains only a `.gitkeep` placeholder, and `Anela.Heblo.API.csproj` has no PostBuild client-generation target (only `GenerateFrontendClientManual` and a disabled `GenerateFrontendClient` target exist) — the API-client-generation doc's description of a backend C# client PostBuild step does not match the current project setup. Nothing to regenerate there.

## Files created/modified
- `frontend/src/api/generated/api-client.ts` — `analytics_GetMarginReport` no longer accepts/serializes `includeDetailedBreakdown`; the query-string branch for it is removed. No other lines in this generated file changed (unrelated drift from the raw regeneration was reverted).

## Tests
No new tests (generated-client-only change). Verified via grep/build/test commands below.

## How to verify
```bash
grep -n "includeDetailedBreakdown\|IncludeDetailedBreakdown" frontend/src/api/generated/api-client.ts   # no matches
grep -n "analytics_GetMarginReport" frontend/src/api/generated/api-client.ts                              # 5 params: startDate, endDate, productFilter, categoryFilter, maxProducts
grep -rn "analytics_GetMarginReport(" frontend/src --include="*.ts" --include="*.tsx" | grep -v "api-client.ts"  # no hand-written callers
cd frontend && npm run build   # Compiled successfully
cd frontend && npm run lint    # 148 pre-existing errors, unrelated to this change (none in api-client.ts or Analytics code)
dotnet build Anela.Heblo.sln   # Build succeeded, 0 errors
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj  # 5414 passed, 64 pre-existing Docker/Testcontainers failures (unrelated), 4 skipped
```

## Notes
- `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual` requires `dotnet tool restore` first (nswag tool) and a clean MSBuild node state (`dotnet build-server shutdown` was needed once to clear a stale file lock from a prior build in this session).
- The raw regeneration pulled in unrelated changes because the checked-in `api-client.ts` had already drifted from `origin/main`'s current API surface before this task started. Reverted the file and hand-applied only the `analytics_GetMarginReport` signature/body diff to keep this PR surgical, consistent with the project's "touch only what the task requires" rule. The broader drift is a pre-existing, separate issue.
- The 64 full-suite test failures are identical in count and cause to task 1's run: `Article.Persistence`/`KnowledgeBase.Integration`-style Testcontainers PostgreSQL tests that require a Docker daemon, unavailable in this sandbox (`docker info` confirms no daemon socket). Not caused by this change.

## Status
DONE
