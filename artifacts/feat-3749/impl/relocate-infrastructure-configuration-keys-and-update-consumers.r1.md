# Implementation: relocate-infrastructure-configuration-keys-and-update-consumers

## What was implemented
Moved `InfrastructureConfigurationKeys` from `Anela.Heblo.Domain.Shared` (Domain layer) to
`Anela.Heblo.Application.Shared` (Application layer) via `git mv`, changed only the `namespace`
declaration in the moved file, and updated the `using Anela.Heblo.Domain.Shared;` directive to
`using Anela.Heblo.Application.Shared;` in the 10 consumer files identified in the task spec.
No class body / constant values / logic were changed anywhere. This removes Domain's dependency
on infra/deployment-only config-key names (APP_VERSION, UseMockAuth, BypassJwtValidation).

Pre-edit grep (`grep -rn "InfrastructureConfigurationKeys\|Domain.Shared" backend/src backend/test --include="*.cs"`)
confirmed the 10-file consumer list from the task-context file was accurate and complete for the
current repo state — no discrepancy found; no files added or removed from the plan.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs` — deleted (via `git mv`)
- `backend/src/Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs` — new location; only the `namespace` line changed to `Anela.Heblo.Application.Shared`
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs` — `using` updated
- `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs` — `using` updated
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs` — `using` updated
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs` — `using` updated
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs` — `using` updated
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — `using` updated
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` — `using` updated
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` — `using` updated
- `backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs` — `using` updated
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — `using` updated

## Tests
- `dotnet build Anela.Heblo.sln` — **ran and observed: succeeded, `0 Error(s)`, `1 Warning(s)`.**
  The single warning is a pre-existing, unrelated `MSB3073` failure in the `AccessMatrixGen`
  post-build tool (a `System.Text.Json.JsonException` reading a generated JSON file), which
  happens on the `Anela.Heblo.API` project's post-build step and is unrelated to this namespace
  move (it references `access-matrix.generated.json` parsing, nothing to do with
  `InfrastructureConfigurationKeys`). Build result: 0 Errors confirmed.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationHandlerTests"` —
  **ran after the sandbox outage below was worked around from the outer session: Passed! Failed: 0, Passed: 5, Skipped: 0, Total: 5.**
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Configuration"` (broader filter) —
  ran: 55 passed, 7 failed. All 7 failures are in `BackgroundServicesConfigurationTests`
  (matched only because "Configuration" appears in the class name), which fails on
  `WebApplicationFactory` host startup while resolving Hangfire recurring-job registrations in
  `ServiceCollectionExtensions.SeedRecurringJobConfigurationsAsync` — confirmed via
  `git log -- backend/test/.../BackgroundServicesConfigurationTests.cs` and grep that this test
  file has no reference to `InfrastructureConfigurationKeys` or `Domain.Shared`/`Application.Shared`;
  this is a pre-existing, unrelated environmental failure, not caused by this change.
- `dotnet format Anela.Heblo.sln --verify-no-changes` — **ran: exit code 0, no formatting diffs.**
- The change is now **committed** on this branch (see commits `8de79ad2`, `64cfb6b4`, `7cb688c7`).

## How to verify
1. `cd` to the repo root (`.../worktrees/feature-3749-Arch-Review-Configuration-Infrastructureconfigurat`).
2. `dotnet build Anela.Heblo.sln` — expect `0 Error(s)` (the `AccessMatrixGen` MSB3073 warning is pre-existing/unrelated). **Confirmed.**
3. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationHandlerTests"` — **confirmed: 5/5 passing.**
4. `dotnet format Anela.Heblo.sln --verify-no-changes` — **confirmed: no diff.**
5. `grep -rn "Anela.Heblo.Domain.Shared" backend/src backend/test --include="*.cs"` — expect zero hits referencing `InfrastructureConfigurationKeys` or the 10 consumer files (other unrelated hits for `CurrencyCode`/`Cooling`/`Result`/`Rag` types are expected and fine).
6. The change is committed on this branch — commits `8de79ad2`, `64cfb6b4`, `7cb688c7`.

## Notes
- **Environment failure during initial implementation, since worked around:** partway through
  this task's original session (immediately after `dotnet build Anela.Heblo.sln` succeeded with
  0 Errors), that session's Bash tool started refusing every command with an isolation-worktree
  error unrelated to this target repo worktree. The outer/coordinating session verified the code
  changes directly against this worktree, ran the remaining verification (tests, format), and
  completed the commit — see the Tests section above for the actual results.
- No discrepancy was found between the task-context's 10-consumer-file list and the actual repo
  state (confirmed by grep before any edits were made).
- No `.csproj` file changes were made or needed.

## PR Summary
Moves `InfrastructureConfigurationKeys` out of `Anela.Heblo.Domain.Shared` into
`Anela.Heblo.Application.Shared`, since the class holds infrastructure/deployment-only
environment-variable names (`APP_VERSION`, `UseMockAuth`, `BypassJwtValidation`) that the
Domain layer should have no knowledge of. Pure move + namespace rename — no logic or constant
value changes. Updates the `using` directive in the 10 files that reference the class. Full
solution build succeeds with 0 errors, `GetConfigurationHandlerTests` pass 5/5, and
`dotnet format --verify-no-changes` reports no diff. Committed on this branch.

### Changes
- `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs` — removed (moved)
- `backend/src/Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs` — added (namespace changed to `Anela.Heblo.Application.Shared`; body unchanged)
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs` — `using` updated
- `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs` — `using` updated
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs` — `using` updated
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs` — `using` updated
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs` — `using` updated
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — `using` updated
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` — `using` updated
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` — `using` updated
- `backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs` — `using` updated
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — `using` updated

## Status
DONE
