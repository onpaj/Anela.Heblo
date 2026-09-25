# Implementation: remove-async-state-machine

## What was implemented

Removed the unnecessary `async` compiler-generated state machine from
`GetConfigurationHandler.Handle`. The method never used `await` — it built the
configuration synchronously and returned a value — so the `async` keyword was
generating an unneeded state machine on every call. The method signature now
returns `Task<GetConfigurationResponse>` (no `async`), and the final
`return response;` was replaced with `return Task.FromResult(response);`.

No behavior changed: exactly the same two `_logger.LogDebug` calls,
`BuildApplicationConfiguration()` call, and `GetConfigurationResponse` object
initializer (including `Timestamp = DateTime.UtcNow`) execute in the same
order as before. `BuildApplicationConfiguration()`, `GetVersionFromSources()`,
the constructor, and field declarations were left untouched.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
  — `Handle` method: dropped `async` from the signature, changed
  `return response;` to `return Task.FromResult(response);`. Only these two
  lines changed; everything else in the method body is byte-for-byte
  identical to before.

## Tests

No new tests were written — this task changes no observable behavior, only
removes an unnecessary compiler-generated state machine, so the existing
suite is the regression check:

- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`
  (unmodified, 5 tests: `Handle_ReturnsVersionFromConfiguration_WhenAppVersionIsSet`,
  `Handle_FallsBackToAssemblyVersion_WhenAppVersionIsEmpty`,
  `Handle_FallsBackToAssemblyVersion_WhenAppVersionIsAbsent`,
  `Handle_ReturnsCorrectUseMockAuth_WhenAppVersionIsSet`,
  `Handle_SetsTimestampAtResponseConstructionTime`)

## How to verify

1. Baseline (before edit): `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationHandlerTests"` — 5/5 passed (ran before the edit, confirming the pre-existing baseline).
2. Build: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — 0 errors, 116 warnings, and grepping the output for `CS1998` shows exactly one occurrence, in `Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs` (an unrelated file) — not in `GetConfigurationHandler.cs`. The warning that previously fired for `GetConfigurationHandler.cs` is gone.
3. Regression: same test filter re-run after the edit — 5/5 passed, `Duration: 6 ms`, identical to the baseline run.
4. Format: `cd backend && dotnet format Anela.Heblo.sln --verify-no-changes --include src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — exited 0, no formatting diff.
5. Committed the source change: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`.

## Notes

- The task context's `dotnet format` invocation needed the solution file
  (`Anela.Heblo.sln`, at the repo root) as the workspace argument — running
  it from `backend/` with no explicit workspace fails with
  `MSBuildWorkspaceFinder` unable to find a project/solution file. Used
  `dotnet format Anela.Heblo.sln --verify-no-changes --include <path-relative-to-repo-root>`
  instead; behavior and result (0 changes needed) are what the task expected.
- No deviations from the task's `Files to modify` / acceptance criteria.

## PR Summary
Removed the unnecessary `async` state machine from `GetConfigurationHandler.Handle` (part of the #4307 arch-review finding). The handler never awaited anything, so `async` was generating a compiler state machine for no reason on every configuration request — a minor per-request allocation/overhead with a purely mechanical fix.

Changed the method to return `Task<GetConfigurationResponse>` directly via `Task.FromResult(response)` instead of `async`/`return response`. No other line in the method changed, and no test changes were needed since behavior is identical — verified by running the existing 5-test suite for this handler both before and after the edit.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — `Handle` no longer `async`; returns `Task.FromResult(response)`.

## Status
DONE
