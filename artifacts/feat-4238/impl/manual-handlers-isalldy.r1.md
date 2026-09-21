# Implementation: manual-handlers-isalldy

## What was implemented

Updated the two manual (non-Graph) production call sites of the now three-parameter
`MarketingAction` constructor and `UpdateDetails` method — `CreateMarketingActionHandler`
and `UpdateMarketingActionHandler` — to compute and pass `isAllDay` using the shared
`MarketingAction.ComputeIsAllDay(startDate, endDate)` helper added by
`task: domain-isalldy-property`. Also fixed the two remaining test-only constructor call
sites in `MarketingActionRepositoryGetPagedTests` and
`MarketingActionRepositoryGetSyncedInWindowTests`, which seed actions with a literal
`isAllDay: false` (no request context available in those fixtures).

A solution-wide `grep -rn "new MarketingAction(\|\.UpdateDetails("` confirmed no other
call site was missed — the other matches (`MarketingActionConstructorTests`,
`MarketingActionTestBuilder`, `MarketingActionUpdateDetailsTests`,
`OutlookEventImportMapperTests`, `OutlookEventImportMapper.cs`) were already updated by
earlier tasks in this plan (`domain-isalldy-property`, `import-mapper-isalldy`).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs` — constructor call now passes `isAllDay: MarketingAction.ComputeIsAllDay(request.StartDate, request.EndDate)`.
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs` — `UpdateDetails` call now passes `isAllDay: MarketingAction.ComputeIsAllDay(request.StartDate, request.EndDate)`.
- `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetPagedTests.cs` — seeding constructor call now passes `isAllDay: false`.
- `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetSyncedInWindowTests.cs` — seeding constructor call now passes `isAllDay: false`.

## Tests

No new tests were added by this task (it closes the last production/test call sites of
an already-tested constructor/method; `ComputeIsAllDay` itself is covered by
`domain-isalldy-property`'s tests). The two modified repository test files' existing
test cases (`GetPagedAsync_*`, `GetSyncedInWindowAsync_*`) continue to pass unchanged —
they only needed the new required constructor argument to compile.

## How to verify

1. `cd backend && dotnet build Anela.Heblo.sln` (run from repo root as
   `dotnet build Anela.Heblo.sln`, since the `.sln` lives at the repo root, not under
   `backend/`) — succeeds, 0 errors.
2. `cd backend && dotnet test Anela.Heblo.sln` (also run as
   `dotnet test Anela.Heblo.sln` from the repo root) — the full run reports
   `Failed: 110, Passed: 7339` for `Anela.Heblo.Tests.dll`, plus pre-existing failures in
   `Anela.Heblo.Adapters.Shoptet.Tests.dll` (13) and `Anela.Heblo.Adapters.Flexi.Tests.dll`
   (72). Every one of these 195 failures is an environment/config issue unrelated to this
   change: the `Anela.Heblo.Tests.dll` failures are all
   `System.ArgumentException: Docker is either not running or misconfigured` from
   Testcontainers-backed integration tests (Leaflet, Article persistence, Authorization),
   and the Shoptet/Flexi failures are `InvalidOperationException: Integration test must
   not run against live environment` guards firing because no test environment is
   configured in this sandbox. No test with "Marketing" in its name failed
   (`grep "\[FAIL\]" ... | grep -i marketing` — no matches), and no failure references
   any file this task or the plan's other tasks touched.
3. `cd backend && dotnet format Anela.Heblo.sln --verify-no-changes` (from repo root as
   `dotnet format Anela.Heblo.sln --verify-no-changes`) — exits 0, no formatting changes
   needed.

## Notes

- The task context's Step 4/5/6 commands are written as `cd backend && dotnet build/test/format Anela.Heblo.sln`, but in this repository the solution file `Anela.Heblo.sln` lives at the repo root, not under `backend/`. Ran the equivalent commands from the repo root instead; behavior and results are otherwise exactly as the plan describes.
- Pre-existing, environment-caused test failures (Docker unavailable for Testcontainers; Shoptet/Flexi live-environment guards) are unrelated to this change and were present before this task's edits — confirmed by their error messages, which name Docker/testcontainers configuration and live-environment guards, not anything in the Marketing feature.

## PR Summary
Closed the last two production call sites (`CreateMarketingActionHandler`,
`UpdateMarketingActionHandler`) and two test-fixture call sites of the
`MarketingAction` constructor/`UpdateDetails` method that still needed the new
`isAllDay` parameter, so the whole solution builds green again. Manually
created/edited actions now derive `IsAllDay` via the same midnight-to-midnight
`ComputeIsAllDay` rule the export path previously used to guess it, preserving
today's observable export behavior for non-Outlook-originated actions.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs` — pass `isAllDay: MarketingAction.ComputeIsAllDay(...)` to the constructor
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs` — pass `isAllDay: MarketingAction.ComputeIsAllDay(...)` to `UpdateDetails`
- `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetPagedTests.cs` — seed with `isAllDay: false`
- `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetSyncedInWindowTests.cs` — seed with `isAllDay: false`

## Status
DONE
