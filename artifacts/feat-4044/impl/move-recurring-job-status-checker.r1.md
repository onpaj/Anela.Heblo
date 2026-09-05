# Implementation: move-recurring-job-status-checker

## What was implemented
Moved `RecurringJobStatusChecker.cs` from the `BackgroundJobs` module root into `BackgroundJobs/Services/` (via `git mv` to preserve history), updated its file-scoped namespace declaration from `Anela.Heblo.Application.Features.BackgroundJobs` to `Anela.Heblo.Application.Features.BackgroundJobs.Services`, and fixed the stale `using` directive in the corresponding test file. No logic, method signatures, constructor, or DI registration behavior was changed.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobStatusChecker.cs` — moved from `BackgroundJobs/` root (git mv, history preserved); only the namespace line changed (`BackgroundJobs;` → `BackgroundJobs.Services;`). Class body byte-for-byte identical otherwise.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobStatusCheckerTests.cs` — updated `using Anela.Heblo.Application.Features.BackgroundJobs;` → `using Anela.Heblo.Application.Features.BackgroundJobs.Services;`. No other type from the old root namespace was referenced in this file, so no second using statement was needed. Test bodies/assertions unchanged.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs` — verified only, not edited. It already had `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` in place, and the build confirms the DI registration `services.AddScoped<IRecurringJobStatusChecker, RecurringJobStatusChecker>();` resolves correctly.

## Tests
- `grep -rn "RecurringJobStatusChecker" backend/ --include=*.cs` — confirmed every match is either the moved file, the test file, `BackgroundJobsModule.cs` (already correct), or a reference to the unaffected `IRecurringJobStatusChecker` Domain interface. No stale references remain.
- `dotnet build Anela.Heblo.sln` — succeeded, 0 errors. 261 warnings, all pre-existing and unrelated to this change (nullable-reference and async-without-await warnings scattered across the codebase).
- `dotnet format Anela.Heblo.sln --verify-no-changes` — no formatting issues found.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~RecurringJobStatusCheckerTests"` — Passed: 5, Failed: 0.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build` (full suite) — Passed: 6635, Failed: 105, Skipped: 4, Total: 6744. All 105 failures were verified (via full log inspection) to be `System.ArgumentException: Docker is either not running or misconfigured` from Testcontainers-based integration tests (Postgres-backed repository tests across KnowledgeBase, Leaflet, Bank, Smartsupp, GridLayouts, Photobank, etc.) — a pre-existing sandbox limitation (no Docker daemon available in this environment), entirely unrelated to this change. No test with "RecurringJob" in its name failed, and `git diff --name-only` confirms only the three files listed above (plus a pre-existing unrelated `artifacts/feat-4044/state.json` diff) were touched.

## How to verify
1. `cd /home/user/worktrees/feature-4044-Arch-Review-Backgroundjobs-Recurringjobstatuscheck`
2. `dotnet build Anela.Heblo.sln` — expect 0 errors.
3. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobStatusCheckerTests"` — expect 5/5 passing.
4. `grep -rn "RecurringJobStatusChecker" backend/ --include=*.cs` — expect no reference to the concrete class through the old `Anela.Heblo.Application.Features.BackgroundJobs` namespace.
5. In an environment with Docker available, `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` should show the Testcontainers-based integration tests passing as well (they fail here only for lack of a Docker daemon).

## Notes
- The `dotnet test` command (with implicit restore/build) hung for 12+ minutes with near-zero CPU usage on the first invocation in this sandbox — almost certainly stuck on an implicit-restore network call through the proxy. Worked around by running `dotnet build Anela.Heblo.sln` first (succeeds normally) and then invoking `dotnet test --no-build`, which completes in ~30 seconds. This is an environment/tooling quirk, not related to the code change; worth noting for future runs in this sandbox.
- `artifacts/feat-4044/state.json` showed as modified in `git status` before any of my edits (pre-existing uncommitted pipeline state change from a prior step). It was included in the `git add -A` per the commit instructions but I made no manual edits to it myself.
- No changes were made to `IRecurringJobStatusChecker` (Domain layer) or any other file in the `BackgroundJobs` module beyond what's listed above, per the out-of-scope constraints.

## PR Summary
This is a pure namespace/file-location refactor with zero behavior change: `RecurringJobStatusChecker.cs` moves from the `BackgroundJobs` module root into `BackgroundJobs/Services/` (using `git mv` to preserve history), aligning it with the rest of the module's service implementations (`RecurringJobSeeder.cs`, etc.) which already live in that subfolder. The class's namespace is updated to match its new location, and the one stale `using` directive in its unit test file is corrected accordingly. `BackgroundJobsModule.cs` needed no changes since it already imported the `Services` namespace.

### Changes
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobStatusChecker.cs` — moved (git mv) from module root; namespace updated to `...BackgroundJobs.Services`.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobStatusCheckerTests.cs` — using directive updated to match the new namespace.

## Status
DONE
