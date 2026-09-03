# Implementation: wire-handler-to-targeted-lookup

## What was implemented
Replaced `GetConsumptionHistoryHandler.Handle`'s full-table `GetAllAsync` call with the
page-scoped `GetMaterialNamesByIdsAsync` lookup (already added to
`IPackingMaterialRepository`/`PackingMaterialRepository` by a prior task in this pipeline).
The handler now resolves material display names only for the ids present on the current
page of consumption-history records, instead of loading every packing material row on
every paginated request.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetConsumptionHistory/GetConsumptionHistoryHandler.cs` — `Handle` now computes `materialIds` from the fetched `records` (`records.Select(r => r.PackingMaterialId).Distinct()`) and calls `_repository.GetMaterialNamesByIdsAsync(materialIds, cancellationToken)` instead of `_repository.GetAllAsync(cancellationToken)` followed by an in-memory `ToDictionary`. No other line in the file changed.

## Tests
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConsumptionHistoryQueryCountTests"` →
  `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1` (the previously-red query-count test is now green).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConsumptionHistoryHandlerTests"` →
  `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4` (all four pre-existing handler tests pass unchanged).
- `dotnet build` (full solution) → `0 Error(s)` (94 pre-existing warnings, unrelated to this change).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterials"` →
  `Passed! - Failed: 0, Passed: 75, Skipped: 0, Total: 75`.

All commands were run with the documented `dotnet build/test` nodeReuse-deadlock workaround
from `memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md`
(`MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 ... -m:1 -nodeReuse:false
-p:UseSharedCompilation=false`), since a first plain `dotnet test` attempt stalled with no
CPU growth in the sandbox.

## How to verify
1. `cd backend`
2. `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConsumptionHistoryQueryCountTests" -m:1 -nodeReuse:false -p:UseSharedCompilation=false`
   — expect 1/1 passed.
3. `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConsumptionHistoryHandlerTests" -m:1 -nodeReuse:false -p:UseSharedCompilation=false`
   — expect 4/4 passed.
4. `dotnet build` — expect 0 errors.
5. `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterials" -m:1 -nodeReuse:false -p:UseSharedCompilation=false`
   — expect 75/75 passed.

## Notes
- No deviation from the task's specified diff — the edit matches the exact before/after
  snippets given in the task description.
- `IPackingMaterialRepository.GetMaterialNamesByIdsAsync` and its implementations
  (`PackingMaterialRepository`, `MockPackingMaterialRepository`, and the
  `CountingRepositoryWrapper` in `GetConsumptionHistoryQueryCountTests.cs`) already existed
  from a prior task in this pipeline and were not modified.
- The sandbox's `dotnet test`/`dotnet build` hit the previously-documented nodeReuse
  deadlock (stale MSBuild worker nodes) on the first attempt; killing the stuck process
  tree and retrying with the documented flag combination resolved it and every subsequent
  run completed normally. This is an environment quirk unrelated to the code change.
- `git status` showed `artifacts/feat-4027/state.json` already modified before this task
  started (untouched by this task) — only
  `GetConsumptionHistoryHandler.cs` was staged and committed, per the task instructions.

## PR Summary
`GetConsumptionHistoryHandler.Handle` previously resolved packing-material display names
by calling `IPackingMaterialRepository.GetAllAsync`, fetching every packing material row
from the database on every paginated consumption-history request just to build a lookup
dictionary for the current page's ids — an unnecessary full-table scan flagged by the
issue #4027 architecture review. This change swaps that call for the page-scoped
`GetMaterialNamesByIdsAsync(materialIds, cancellationToken)`, where `materialIds` is the
distinct set of `PackingMaterialId`s actually present in the fetched page of records. The
new query-count test (`GetConsumptionHistoryQueryCountTests`) now passes, asserting
`GetAllAsync` is never called and `GetMaterialNamesByIdsAsync` is called exactly once with
a page-scoped id set; all four pre-existing `GetConsumptionHistoryHandlerTests` continue to
pass unchanged, since their behavior (name resolution, unknown-material fallback,
pagination clamping, filter behavior) is unaffected by the lookup strategy. Full solution
build is clean and the full `PackingMaterials` test suite (75 tests) passes.

## Status
DONE
