# Review — BackgroundRefreshTaskRegistry: honour explicit RefreshTaskConfiguration

## Verdict: done

## What was checked

1. **Diff scope** — exactly two files changed: `BackgroundRefreshTaskRegistry.cs` (+4/-1) and the new `BackgroundRefreshTaskRegistryTests.cs` (+134). Matches the plan's "in scope" boundary; no changes to `BackgroundRefreshExtensions.cs`, `RefreshTaskConfiguration.cs`, or module registration call sites.

2. **The fix** — `InitializeTasksFromSetup` now reads:
   ```csharp
   if (taskInfo.Configuration != null)
   {
       RegisterTask(taskInfo.TaskId, taskInfo.RefreshMethod, taskInfo.Configuration);
   }
   else
   {
       RegisterTask(taskInfo.TaskId, taskInfo.RefreshMethod);
   }
   ```
   This is exactly the `if`/`else` restructure specified in plan-01.md and design-01.md — the unconditional second `RegisterTask` call (the bug) is gone, and the two registration paths are now mutually exclusive. Satisfies FR-1 (explicit config no longer overwritten), FR-2 (no-config fallback unchanged), and FR-3 (no startup crash for explicit-config tasks lacking an app-settings section — the app-settings path is simply never invoked in that case).

3. **New tests** — `BackgroundRefreshTaskRegistryTests.cs` contains exactly the three tests specified in design-01.md, mapping 1:1 to FR-1/2/3:
   - `ExplicitConfiguration_IsHonoured_NotOverwrittenByAppSettings` — explicit config with distinctive values vs. a *different* in-memory app-settings section; asserts the explicit values win.
   - `NoConfiguration_FallsBackToAppSettings_Unchanged` — asserts registered config matches `RefreshTaskConfiguration.FromAppSettings` output independently computed in the test.
   - `ExplicitConfiguration_NoAppSettingsSection_DoesNotThrow` — empty `IConfiguration`, explicit config supplied; asserts construction succeeds.
   Construction pattern (`Mock<ILogger>`, `Mock<IServiceProvider>`, `Options.Create(setup)`, real `ConfigurationBuilder`-based `IConfiguration`) matches the existing `BackgroundRefreshSchedulerServiceTests.cs`/`RefreshTaskConfigurationTests.cs` conventions, as called out in architecture-01.md.

4. **Independent verification performed in this review** (not just trusting development-01.md's claims):
   - `dotnet build backend/src/Anela.Heblo.Xcc/Anela.Heblo.Xcc.csproj` → succeeded, 0 errors.
   - `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~BackgroundRefresh -p:UseAppHost=false` → **29/29 passed**, confirming the 3 new tests plus all pre-existing `BackgroundRefreshSchedulerServiceTests`/`RefreshTaskConfigurationTests` pass with no regression.
   - `dotnet format Anela.Heblo.sln --verify-no-changes --include <the two touched files>` → exit code 0, clean.
   - Read the full production diff (`git diff HEAD~1 HEAD -- BackgroundRefreshTaskRegistry.cs`) and the full new test file directly — content matches what development-01.md describes, no discrepancies.

## Assessment

- Conforms to spec: the missing-`else` bug is fixed exactly as required by the plan/design/architecture chain, with no scope creep.
- Adheres to architecture: change is entirely internal to `InitializeTasksFromSetup`; no interface, DI registration, or module-boundary changes.
- Completeness: all three required acceptance-criteria tests (FR-1/2/3) are present and passing; no gaps.
- Correctness: no logic errors found; the fix is a straightforward, minimal control-flow restructure with no other side effects.

No changes requested.
