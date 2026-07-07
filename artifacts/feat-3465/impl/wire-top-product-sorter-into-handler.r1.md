# Implementation: wire-top-product-sorter-into-handler

## What was implemented
Added `ITopProductSorter` as a new constructor dependency on `GetProductMarginSummaryHandler`, removed the now-redundant `ApplySorting` private method, and updated the call site in `GenerateTopProducts` to `_topProductSorter.Sort(...)`. Updated both handler-construction call sites in `GetProductMarginSummaryHandlerTests.cs` to pass a real `TopProductSorter` instance. This is the final task of the refactor — the handler now contains only orchestration (`Handle`, `GenerateTopProducts`, `CalculateTotalMarginForLevel`).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — added `ITopProductSorter` dependency, removed `ApplySorting`.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — added `_topProductSorter` field and wired it into both handler construction call sites.

## Tests
Existing `GetProductMarginSummaryHandlerTests.cs` suite (8 tests) — no new tests per the task-context (this task only rewires an existing call site and constructor).

## How to verify
- `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore` → 0 errors.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~Analytics"` → 141/141 passed.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build` (full suite) → 5448 passed, 64 failed (all pre-existing Docker/Testcontainers integration tests unrelated to Analytics — confirmed via `grep` that every failure is a `*IntegrationTests`/`*SqlTests` class hitting "Docker is either not running or misconfigured"; zero Analytics failures).
- `dotnet format Anela.Heblo.sln --include <touched files>` → no changes (already clean).
- `wc -l GetProductMarginSummaryHandler.cs` → 131 lines (down from the original 242).

## Notes
Followed the task-context file's exact code verbatim. No deviations. Additionally fixed one pre-existing, unrelated compile error (`ConfigurationConstants.APP_VERSION` → `InfrastructureConfigurationKeys.APP_VERSION` in `GetConfigurationHandlerTests.cs`, from #3432/#3437) that was blocking the entire test project from building — committed separately in `9f47768` since it's out of scope for this issue.

## PR Summary
Completed the SRP refactor: `GetProductMarginSummaryHandler` now takes `ITopProductSorter` via DI and calls `.Sort(...)` instead of its own private `ApplySorting` switch, which is deleted. Combined with the earlier tasks, the handler shrank from 242 to 131 lines and now contains only orchestration logic — both extracted concerns (margin aggregation and sorting) are independently unit-tested DI services. This is step 4 of 4 in the SRP refactor from issue #3465.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`

## Status
DONE
