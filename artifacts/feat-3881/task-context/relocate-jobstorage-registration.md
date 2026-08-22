### task: relocate-jobstorage-registration

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Dashboard/DashboardModuleTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Infrastructure/HangfireServicesTests.cs`

**Interfaces:**
- No new interfaces or types. `Hangfire.JobStorage` is a pre-existing concrete type from the `Hangfire` NuGet package (already referenced by both projects touched here).
- `DashboardModule.AddDashboardModule(this IServiceCollection services) : IServiceCollection` — signature unchanged, one fewer registration inside.
- `ServiceCollectionExtensions.AddHangfireServices(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment) : IServiceCollection` — signature unchanged, one more registration inside.

- [ ] **Step 1: Write the failing test asserting `DashboardModule` no longer registers `JobStorage`**

Create `backend/test/Anela.Heblo.Tests/Features/Dashboard/DashboardModuleTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Dashboard;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

/// <summary>
/// Regression test: DashboardModule must not register Hangfire's JobStorage singleton.
///
/// Bug: DashboardModule.AddDashboardModule() registered the backend's only JobStorage
/// binding, even though nothing under Dashboard's own owned code consumes it. The real
/// consumers (HangfireBackgroundWorker, HangfireFailedJobCounter) live in
/// API/Infrastructure/Hangfire and are registered by AddHangfireServices. If
/// AddDashboardModule() were ever skipped or removed, those adapters would fail DI
/// resolution at startup with no discoverable root cause.
///
/// Fix: JobStorage is now registered inside AddHangfireServices, next to its consumers.
/// </summary>
public class DashboardModuleTests
{
    [Fact]
    public void AddDashboardModule_DoesNotRegisterJobStorage()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDashboardModule();

        // Assert
        services
            .Any(d => d.ServiceType == typeof(JobStorage))
            .Should().BeFalse(
                "JobStorage is consumed by HangfireBackgroundWorker and HangfireFailedJobCounter " +
                "in the API project and must be registered in AddHangfireServices, not in the " +
                "unrelated DashboardModule");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "DashboardModuleTests" -v minimal
```

Expected: FAIL — `AddDashboardModule_DoesNotRegisterJobStorage` fails because `DashboardModule.cs` still registers `JobStorage` at this point. (If the test project doesn't compile because `services.Any(...)` needs `System.Linq`, that's expected too — `Microsoft.Extensions.DependencyInjection.ServiceCollection` implements `IEnumerable<ServiceDescriptor>`, and `Anela.Heblo.Tests` already uses LINQ elsewhere, so no new using is needed beyond the ones listed above; if the build reports a missing `Any` extension, add `using System.Linq;` to the test file.)

- [ ] **Step 3: Write the failing test asserting `AddHangfireServices` registers `JobStorage`**

Create `backend/test/Anela.Heblo.Tests/Infrastructure/HangfireServicesTests.cs`:

```csharp
using Anela.Heblo.API.Extensions;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure;

/// <summary>
/// Regression test: AddHangfireServices must register the JobStorage singleton that
/// HangfireBackgroundWorker and HangfireFailedJobCounter depend on, since it is the one
/// module where every other Hangfire adapter is already registered. See
/// Anela.Heblo.Tests.Features.Dashboard.DashboardModuleTests for the companion assertion
/// that DashboardModule no longer owns this registration.
/// </summary>
public class HangfireServicesTests
{
    [Fact]
    public void AddHangfireServices_RegistersJobStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hangfire:UseInMemoryStorage"] = "true",
                ["Hangfire:WorkerCount"] = "1",
                ["Hangfire:SchemaName"] = "hangfire",
                ["Hangfire:ConnectionLimit"] = "0",
            })
            .Build();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns("Test");

        // Act
        services.AddHangfireServices(configuration, environment.Object);

        // Assert
        services
            .Any(d => d.ServiceType == typeof(JobStorage))
            .Should().BeTrue(
                "AddHangfireServices must register JobStorage next to the Hangfire adapters " +
                "(HangfireBackgroundWorker, HangfireFailedJobCounter) that consume it");
    }
}
```

- [ ] **Step 4: Run tests to verify they fail for the right reason**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "DashboardModuleTests|HangfireServicesTests" -v minimal
```

Expected: `AddDashboardModule_DoesNotRegisterJobStorage` FAILS (JobStorage is still registered by DashboardModule); `AddHangfireServices_RegistersJobStorage` PASSES already (the registration exists today, just in the wrong place) — confirming the second test is a safety net for the *destination*, not yet proof of the fix. If `AddHangfireServices_RegistersJobStorage` fails instead with a configuration or environment-mocking error, fix the test's `IConfiguration`/`IWebHostEnvironment` setup to match `HangfireOptions`'s actual bound shape (check `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/HangfireOptions.cs` for the exact property names before proceeding) — do not weaken the assertion.

- [ ] **Step 5: Remove the `JobStorage` registration from `DashboardModule`**

In `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`, remove lines 19-20 (the comment and the registration):

```csharp
        // Hangfire storage singleton — resolved lazily after Hangfire is configured
        services.AddSingleton(_ => JobStorage.Current);

```

The file becomes:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Persistence.Dashboard;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Dashboard;

public static class DashboardModule
{
    public static IServiceCollection AddDashboardModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by the ApplicationModule

        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IUserDashboardSettingsRepository, UserDashboardSettingsRepository>();

        // Per-user async lock for serializing concurrent UserDashboardSettings mutations
        services.AddSingleton<IUserDashboardSettingsLock, UserDashboardSettingsLock>();

        // Shared scaffold for Enable/Disable tile (and future) mutations
        services.AddScoped<IUserDashboardSettingsMutator, UserDashboardSettingsMutator>();

        return services;
    }
}
```

Note the `using Hangfire;` line is removed too — it was only needed for the `JobStorage` reference.

- [ ] **Step 6: Add the `JobStorage` registration to `AddHangfireServices`**

In `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, find this block (around line 344-360):

```csharp
        services.AddHangfireServer(options =>
        {
            options.WorkerCount = hangfireOptions.WorkerCount;
        });

        // Register Hangfire dashboard authorization filter
        services.AddTransient<HangfireDashboardTokenAuthorizationFilter>();

        // Register IBackgroundWorker implementation
        services.AddTransient<IBackgroundWorker, HangfireBackgroundWorker>();

        // Register Hangfire adapter implementations (interfaces live in Application,
        // concrete types live in API/Infrastructure/Hangfire — relocated to keep the
        // Application project free of Hangfire imports for these specific adapters).
        services.AddScoped<IJobEnqueuer, HangfireJobEnqueuer>();
        services.AddScoped<IFailedJobCounter, HangfireFailedJobCounter>();
        services.AddSingleton<ICronScheduler, HangfireRecurringJobScheduler>();
```

Replace it with (inserting the new registration and its comment right after the dashboard-authorization-filter registration, before `IBackgroundWorker`, since `HangfireBackgroundWorker` is the first consumer below it):

```csharp
        services.AddHangfireServer(options =>
        {
            options.WorkerCount = hangfireOptions.WorkerCount;
        });

        // Register Hangfire dashboard authorization filter
        services.AddTransient<HangfireDashboardTokenAuthorizationFilter>();

        // Hangfire storage singleton — resolved lazily after Hangfire is configured above.
        // Consumed by HangfireBackgroundWorker and HangfireFailedJobCounter below. Moved here
        // from DashboardModule (Application layer), which had no local consumer of JobStorage
        // and no declared dependency relationship with this method.
        services.AddSingleton(_ => JobStorage.Current);

        // Register IBackgroundWorker implementation
        services.AddTransient<IBackgroundWorker, HangfireBackgroundWorker>();

        // Register Hangfire adapter implementations (interfaces live in Application,
        // concrete types live in API/Infrastructure/Hangfire — relocated to keep the
        // Application project free of Hangfire imports for these specific adapters).
        services.AddScoped<IJobEnqueuer, HangfireJobEnqueuer>();
        services.AddScoped<IFailedJobCounter, HangfireFailedJobCounter>();
        services.AddSingleton<ICronScheduler, HangfireRecurringJobScheduler>();
```

No new `using` is needed — `ServiceCollectionExtensions.cs` already has `using Hangfire;` at the top of the file (needed for `JobStorage`, `GlobalJobFilters`, `CompatibilityLevel`, etc.).

- [ ] **Step 7: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "DashboardModuleTests|HangfireServicesTests" -v minimal
```

Expected: both tests PASS.

- [ ] **Step 8: Build the full backend and verify formatting**

```bash
cd backend && dotnet build && dotnet format --verify-no-changes
```

Expected: build succeeds with no errors or new warnings (specifically: no unused-`using` warning for the removed `using Hangfire;` in `DashboardModule.cs`, and no missing-type error in `ServiceCollectionExtensions.cs`); `dotnet format --verify-no-changes` reports no formatting drift.

- [ ] **Step 9: Run the full Dashboard and Hangfire-adjacent test suites**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Features.Dashboard|FullyQualifiedName~Infrastructure.HangfireServicesTests|FullyQualifiedName~BackgroundJobs" -v minimal
```

Expected: all tests pass — in particular, confirm no other test in `Features/Dashboard/` or `Features/BackgroundJobs/` implicitly depended on `DashboardModule` providing `JobStorage` (none were found in the codebase during architecture review, but this run is the definitive check).

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs \
        backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs \
        backend/test/Anela.Heblo.Tests/Features/Dashboard/DashboardModuleTests.cs \
        backend/test/Anela.Heblo.Tests/Infrastructure/HangfireServicesTests.cs
git commit -m "fix(dashboard): move JobStorage DI registration from DashboardModule to AddHangfireServices

DashboardModule.AddDashboardModule() registered the only JobStorage singleton in the
backend even though nothing in Dashboard's own code consumes it. The actual consumers,
HangfireBackgroundWorker and HangfireFailedJobCounter, are registered in
AddHangfireServices — the JobStorage binding now lives there too, next to them."
```

---

## Self-Review

### Spec coverage check

| Spec requirement | Covered by |
|-------------------|------------|
| FR-1: remove `JobStorage` registration from `DashboardModule` | Step 5 |
| FR-1: remove now-unused `using Hangfire;` from `DashboardModule.cs` (verified at build time, not assumed) | Step 5 (removed), Step 8 (build verifies) |
| FR-1: add `JobStorage` registration to `AddHangfireServices`, after storage is configured, near the adapters that consume it | Step 6 |
| FR-1: no other file changes required for `HangfireBackgroundWorker`/`HangfireFailedJobCounter` to keep resolving | Step 7 (tests pass without touching those files), Step 9 (broader suite confirms) |
| FR-2: pure relocation, no behavior change, `DashboardModule` still registers its other 3 bindings | Step 5 (shows full resulting file — `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `IUserDashboardSettingsMutator` all retained) |
| FR-2: registration/resolution order in `Program.cs` unaffected (lazy factory, order doesn't matter) | Explained in Overview/arch-review; Step 9 running the real DI graph via `dotnet build`+tests is the empirical proof — no `Program.cs` change is made |
| NFR-1: no runtime behavior change | Step 8 build, Step 9 full suite run |
| NFR-2: discoverability — `AddHangfireServices` now shows the full dependency set for its adapters | Step 6 (registration + explanatory comment placed directly above `IBackgroundWorker`/adapter registrations) |
| Arch review Specification Amendment: confirm via `dotnet build`, not just inspection, that the `using` change is clean | Step 8 |
| Arch review optional regression test suggestion | Steps 1-4 (`DashboardModuleTests`) plus the companion `HangfireServicesTests` (arch review's suggestion only covered the "must not be in Dashboard" direction; this plan also locks in the "must be in AddHangfireServices" direction, since a test that only forbids the old location wouldn't catch the registration being dropped entirely) |

### Placeholder scan

No TBDs, TODOs, or "similar to Task N" phrases. All code blocks are complete and copy-pasteable.

### Type consistency

- `JobStorage` — same `Hangfire.JobStorage` type referenced identically in both new tests and both modified source files; no renaming across steps.
- `AddDashboardModule` / `AddHangfireServices` — signatures unchanged throughout; test calls match the real method signatures verified by reading the current source in Steps 5-6.
- Test namespaces (`Anela.Heblo.Tests.Features.Dashboard`, `Anela.Heblo.Tests.Infrastructure`) follow the existing convention seen in sibling files (`backend/test/Anela.Heblo.Tests/Features/Dashboard/*.cs`, `backend/test/Anela.Heblo.Tests/Persistence/PersistenceModuleTests.cs` for the `Infrastructure`-style regression-test pattern this plan mirrors).
