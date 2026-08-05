# Design — BackgroundRefreshTaskRegistry: honour explicit RefreshTaskConfiguration

No UI. This is a backend-only bugfix inside a single internal method plus a new unit test file; no wireframes/UX apply.

## Component design

### `BackgroundRefreshTaskRegistry.InitializeTasksFromSetup` (the only production change)

Current shape (`BackgroundRefreshTaskRegistry.cs:32-46`) treats "register with explicit config" and "register from app-settings" as sequential, non-exclusive steps. The fix makes them mutually exclusive branches of the same `if`, with no other change to signatures, field layout, or the surrounding class:

```csharp
private void InitializeTasksFromSetup(BackgroundRefreshTaskRegistrySetup setup)
{
    _logger.LogInformation("Initializing {TaskCount} background refresh tasks from setup", setup.TaskRegistrations.Count);

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

    _logger.LogInformation("Successfully initialized {RegisteredCount} background refresh tasks", _registeredTasks.Count);
}
```

Responsibility of the method is unchanged: for each `TaskRegistrationInfo` in setup, register exactly one `RegisteredTask` into `_registeredTasks`, sourcing its `RefreshTaskConfiguration` either from the explicitly supplied value or (only in its absence) from `RefreshTaskConfiguration.FromAppSettings`. The two `RegisterTask` overloads (`:48-76`), `RegisterTask`'s `AddOrUpdate` write, `RegisterRefreshTask*` extension methods, and `IBackgroundRefreshTaskRegistry` are untouched — the discriminator (`Configuration != null`) and both branch bodies already exist verbatim in the current code; only control flow linking them changes (`if` → `if/else`).

No new component, class, or interface is introduced. This keeps the change inside the file the module-map part owns.

### New test component: `BackgroundRefreshTaskRegistryTests`

New file `backend/test/Anela.Heblo.Tests/Xcc/BackgroundRefresh/BackgroundRefreshTaskRegistryTests.cs`, following the existing construction pattern already used in `BackgroundRefreshSchedulerServiceTests.cs` (Moq-based `ILogger`, `IConfiguration`, `IServiceProvider`; real `BackgroundRefreshTaskRegistrySetup` passed via `Options.Create`).

Responsibilities — one test class, three test methods mapping 1:1 to the plan's FRs, each independently constructing its own registry instance (no shared mutable state, matching the existing scheduler test's per-fixture style):

| Test method | FR | Arrange | Assert |
|---|---|---|---|
| `ExplicitConfiguration_IsHonoured_NotOverwrittenByAppSettings` | FR-1 | One `TaskRegistrationInfo` with `Configuration` set to a distinctive `RefreshInterval`/`Enabled`/`HydrationTier`; `IConfiguration` mock returns a *different* value for the matching `BackgroundRefresh:{Owner}:{Method}` section (proves app-settings path did not run and did not win) | `GetRegisteredTasks()` has exactly 1 entry; its values equal the explicit config's values, not the mocked app-settings' |
| `NoConfiguration_FallsBackToAppSettings_Unchanged` | FR-2 | One `TaskRegistrationInfo` with `Configuration == null`; `IConfiguration` mock backed by an in-memory `ConfigurationBuilder` with a matching `BackgroundRefresh:{Owner}:{Method}` section | `GetRegisteredTasks()` has exactly 1 entry whose values equal what `RefreshTaskConfiguration.FromAppSettings` produces for that section |
| `ExplicitConfiguration_NoAppSettingsSection_DoesNotThrow` | FR-3 | One `TaskRegistrationInfo` with `Configuration` set; `IConfiguration` with no `BackgroundRefresh` section at all (empty `ConfigurationBuilder` build, not a mock, so `GetSection(...).Exists()` genuinely returns false) | Constructing `BackgroundRefreshTaskRegistry` does not throw; `GetRegisteredTasks()` returns the explicit config |

Test doubles: reuse the existing pattern from `BackgroundRefreshSchedulerServiceTests` — `Mock<ILogger<BackgroundRefreshTaskRegistry>>`, a plain `Mock<IServiceProvider>` (unused by construction/registration, no scope setup needed since no task execution happens in these tests), and `Options.Create(setup)` wrapping a `BackgroundRefreshTaskRegistrySetup` with one `TaskRegistrations` entry per test. For FR-2/FR-3, build `IConfiguration` via `new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {...}).Build()` rather than mocking `IConfiguration`, since `RefreshTaskConfiguration.FromAppSettings` calls `GetSection(...).Exists()`, which is simplest to satisfy correctly with a real in-memory configuration rather than a hand-mocked `IConfigurationSection`. For FR-1, either approach works since the app-settings path must not be consulted at all — an in-memory config with a deliberately different value is enough to prove precedence without needing mock-verification of "not called".

## Data schemas

No schema changes — no DB, no request/response DTOs, no events. Existing types reused as-is by the tests:

- `TaskRegistrationInfo { TaskId, RefreshMethod, Configuration? }` (`TaskRegistrationInfo.cs`)
- `RefreshTaskConfiguration { TaskId, InitialDelay, RefreshInterval, Enabled, HydrationTier }` (`RefreshTaskConfiguration.cs`)
- `BackgroundRefreshTaskRegistrySetup { TaskRegistrations: List<TaskRegistrationInfo> }` (`BackgroundRefreshTaskRegistrySetup.cs`)

Test-only fixture shape (in-memory config dictionary for FR-2), keyed to match `RefreshTaskConfiguration.FromAppSettings`'s expected path `BackgroundRefresh:{Owner}:{Method}`:

```
BackgroundRefresh:TestOwner:TestMethod:InitialDelay    = "00:00:05"
BackgroundRefresh:TestOwner:TestMethod:RefreshInterval = "00:10:00"
BackgroundRefresh:TestOwner:TestMethod:Enabled         = "true"
BackgroundRefresh:TestOwner:TestMethod:HydrationTier   = "2"
```

`TaskId` for these fixtures must be `"TestOwner.TestMethod"` (dot-separated `Owner.Method`, per `FromAppSettings`'s `taskId.Split('.')` requirement at `RefreshTaskConfiguration.cs:18-22`).

## Validation plan

- `dotnet build` on the solution (or at minimum `Anela.Heblo.Xcc` + test project).
- `dotnet format` on touched files.
- `dotnet test --filter FullyQualifiedName~BackgroundRefresh` to run the new tests plus the existing `BackgroundRefreshSchedulerServiceTests` and `RefreshTaskConfigurationTests` for regression confidence.
