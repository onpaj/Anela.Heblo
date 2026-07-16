### task: wire-recurring-job-seeder-into-di-and-startup

**Goal:** Register `IRecurringJobSeeder` in the `BackgroundJobsModule` DI composition root and switch the startup seeding call site to depend on it instead of `IRecurringJobConfigurationRepository`. `IRecurringJobConfigurationRepository.SeedDefaultConfigurationsAsync` still exists on the interface at the end of this task (it becomes dead code, removed in the next task) — this keeps every commit in this plan buildable and behavior-preserving.

#### Step 1: Register `IRecurringJobSeeder` in `BackgroundJobsModule`

Edit `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs`.

Old text:
```csharp
        // MediatR handlers are automatically registered by MediatR scan
        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IRecurringJobConfigurationRepository, RecurringJobConfigurationRepository>();
        // Hangfire adapter implementations (IHangfireJobEnqueuer, IHangfireRecurringJobScheduler)
        // are registered in Anela.Heblo.API.Extensions.ServiceCollectionExtensions.AddHangfireServices
        // because their implementations live in the API project (Clean Architecture dependency rule).
```

New text:
```csharp
        // MediatR handlers are automatically registered by MediatR scan
        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IRecurringJobConfigurationRepository, RecurringJobConfigurationRepository>();
        // Startup-only seeding service (Application layer, wraps the repository)
        services.AddScoped<IRecurringJobSeeder, RecurringJobSeeder>();
        // Hangfire adapter implementations (IHangfireJobEnqueuer, IHangfireRecurringJobScheduler)
        // are registered in Anela.Heblo.API.Extensions.ServiceCollectionExtensions.AddHangfireServices
        // because their implementations live in the API project (Clean Architecture dependency rule).
```

`RecurringJobSeeder` lives in the same `Anela.Heblo.Application.Features.BackgroundJobs.Services` namespace as `BackgroundJobsModule`'s sibling registrations, and `IRecurringJobConfigurationRepository`/`RecurringJobConfigurationRepository` are already `using`-imported in this file (`Anela.Heblo.Domain.Features.BackgroundJobs` and `Anela.Heblo.Persistence.BackgroundJobs`) — no new `using` is needed since `IRecurringJobSeeder`/`RecurringJobSeeder` are in the file's own namespace (`Anela.Heblo.Application.Features.BackgroundJobs`, of which `.Services` is a child namespace reachable without an extra `using` only if the compiler resolves nested namespaces automatically — it does not. Add the `using` explicitly to be safe.)

Also add, near the top of the file, alongside the existing `using` directives:

Old text:
```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence.BackgroundJobs;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;
```

New text:
```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence.BackgroundJobs;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;
```

#### Step 2: Update the startup call site

Edit `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`.

Old text:
```csharp
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                var repository = scope.ServiceProvider.GetRequiredService<IRecurringJobConfigurationRepository>();

                // Get all discovered IRecurringJob implementations
                var discoveredJobs = scope.ServiceProvider.GetServices<IRecurringJob>();

                await repository.SeedDefaultConfigurationsAsync(discoveredJobs);
```

New text:
```csharp
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                var seeder = scope.ServiceProvider.GetRequiredService<IRecurringJobSeeder>();

                // Get all discovered IRecurringJob implementations
                var discoveredJobs = scope.ServiceProvider.GetServices<IRecurringJob>();

                await seeder.SeedDefaultConfigurationsAsync(discoveredJobs);
```

No new `using` is needed in this file: `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` is already present (line 24, verified in the current file), which is where `IRecurringJobSeeder` lives.

Logging behavior (success log with discovered-job count; error log + rethrow on failure) is untouched — only the resolved type and local variable name (`repository` → `seeder`) change, per FR-4.

#### Step 3: Build and full test verification

```bash
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln
```
Expect: build succeeds, full backend test suite passes (no test exercises `SeedRecurringJobConfigurationsAsync` directly today — it is startup-only glue code covered by FR-4's acceptance criteria via the moved unit tests in the previous task and manual/staging verification, consistent with the spec).

#### Step 4: Commit

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs \
        backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
git commit -m "#3635: Wire IRecurringJobSeeder into DI and switch startup seeding call site"
```

---
