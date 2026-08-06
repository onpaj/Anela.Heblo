# Development — BackgroundRefreshTaskRegistry honours explicit RefreshTaskConfiguration

## What changed

### Fix — `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/BackgroundRefreshTaskRegistry.cs`

`InitializeTasksFromSetup` unconditionally called `RegisterTask(taskInfo.TaskId, taskInfo.RefreshMethod)` (the app-settings-derived overload) after already registering with an explicit `RefreshTaskConfiguration`, when one was supplied. Because both overloads write into the same `ConcurrentDictionary` key via `AddOrUpdate` with an always-overwrite update delegate, the second call always won — discarding the explicit configuration and crashing app startup if no matching `BackgroundRefresh:{Owner}:{Method}` app-settings section existed.

Changed the unconditional second `RegisterTask` call into an `else` branch, exactly as specified in `design-01.md`:

```csharp
foreach (var taskInfo in setup.TaskRegistrations)
{
    if (taskInfo.Configuration != null)
    {
        RegisterTask(taskInfo.TaskId, taskInfo.RefreshMethod, taskInfo.Configuration);
    }
    else
    {
        RegisterTask(taskInfo.TaskId, taskInfo.RefreshMethod);
    }
}
```

No other lines in the file changed. No changes to `BackgroundRefreshExtensions.cs`, `RefreshTaskConfiguration.cs`, `IBackgroundRefreshTaskRegistry`, or module registration call sites — matches the plan's "in scope"/"out of scope" boundaries.

### New tests — `backend/test/Anela.Heblo.Tests/Xcc/BackgroundRefresh/BackgroundRefreshTaskRegistryTests.cs`

New file, one test class, three tests mapping 1:1 to the plan's FRs, following the existing construction pattern from `BackgroundRefreshSchedulerServiceTests.cs` (Moq `ILogger`/`IServiceProvider`, real setup passed via `Options.Create`):

- `ExplicitConfiguration_IsHonoured_NotOverwrittenByAppSettings` (FR-1) — registers a task with an explicit `RefreshTaskConfiguration` while an in-memory `IConfiguration` has a *different* value for the matching `BackgroundRefresh:TestOwner:TestMethod` section; asserts the registered config matches the explicit values, proving the app-settings path did not win.
- `NoConfiguration_FallsBackToAppSettings_Unchanged` (FR-2) — registers a task with `Configuration == null` and a matching in-memory app-settings section; asserts the registered config matches what `RefreshTaskConfiguration.FromAppSettings` independently produces for that section.
- `ExplicitConfiguration_NoAppSettingsSection_DoesNotThrow` (FR-3) — registers a task with an explicit `RefreshTaskConfiguration` against an empty `IConfiguration` (no `BackgroundRefresh` section at all); asserts construction succeeds and the explicit config is registered.

Per the design, FR-2/FR-3 configuration is built via `new ConfigurationBuilder().AddInMemoryCollection(...).Build()` rather than mocking `IConfiguration`, since `RefreshTaskConfiguration.FromAppSettings` calls `GetSection(...).Exists()`.

## Verification performed

- `dotnet build backend/src/Anela.Heblo.Xcc/Anela.Heblo.Xcc.csproj` — succeeded, 0 errors (pre-existing warnings only, unrelated to this change).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~BackgroundRefresh -p:UseAppHost=false` — **29/29 passed** (3 new + 26 existing `BackgroundRefreshSchedulerServiceTests`/`RefreshTaskConfigurationTests`), confirming no regression to existing no-config production-caller behavior.
  - `-p:UseAppHost=false` was needed only to work around an unrelated macOS sandbox codesigning failure (`NETSDK1177: Failed to sign apphost`) when building the `Anela.Heblo.API` project as a transitive part of the full solution build; it does not affect test semantics.
- `dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/BackgroundRefreshTaskRegistry.cs backend/test/Anela.Heblo.Tests/Xcc/BackgroundRefresh/BackgroundRefreshTaskRegistryTests.cs` — ran clean, no files needed reformatting.
- `git diff` confirms the production change is exactly the one-line-to-if/else restructure; the only other change is the new test file (untracked/added).

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~BackgroundRefresh
```

(add `-p:UseAppHost=false` if the local environment hits the same apphost-signing sandbox issue).

## Files changed

- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/BackgroundRefreshTaskRegistry.cs` (fix)
- `backend/test/Anela.Heblo.Tests/Xcc/BackgroundRefresh/BackgroundRefreshTaskRegistryTests.cs` (new)
