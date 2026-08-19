# Design: Move the `JobStorage` DI registration out of `DashboardModule`

## Component Design

### `DashboardModule` (`backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`)
- **Responsibility after change:** registers only bindings Dashboard's own feature code actually consumes — `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `IUserDashboardSettingsMutator`.
- **Removed:** the `services.AddSingleton(_ => JobStorage.Current);` line and its preceding comment. Remove the `using Hangfire;` directive too, if the file has no other reference to the `Hangfire` namespace after this line is removed (verify with a build — do not remove speculatively if some other line in the file still needs it).
- **Contract:** `AddDashboardModule(this IServiceCollection services) : IServiceCollection` — signature unchanged.

### `ServiceCollectionExtensions.AddHangfireServices` (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`)
- **Responsibility after change:** in addition to its existing responsibilities (configure Hangfire storage backend, register `IBackgroundWorker`, `IJobEnqueuer`, `IFailedJobCounter`, `ICronScheduler`, the dashboard-auth filter, memory cache, `HangfireOptions`, and the global job filter), this method now also registers the `JobStorage` singleton that two of those adapters (`HangfireBackgroundWorker`, `HangfireFailedJobCounter`) depend on.
- **New line**, placed immediately before the existing "Register Hangfire adapter implementations..." comment block (currently around line 355), after the storage-configuration `if/else` (`AddHangfire(...)` with `UseMemoryStorage`/`UsePostgreSqlStorage`) and after `AddHangfireServer`/dashboard-auth-filter registration:
  ```csharp
  // Hangfire storage singleton — resolved lazily after Hangfire is configured
  services.AddSingleton(_ => JobStorage.Current);
  ```
- **Contract:** `AddHangfireServices(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment) : IServiceCollection` — signature unchanged.

### Consumers (unchanged — verify continued resolvability, no code changes)
- `HangfireBackgroundWorker` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs`) — constructor `HangfireBackgroundWorker(IOptions<HangfireOptions> options, JobStorage jobStorage)`.
- `HangfireFailedJobCounter` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireFailedJobCounter.cs`) — constructor `HangfireFailedJobCounter(JobStorage jobStorage)`.

Neither consumer's constructor or registration (`services.AddTransient<IBackgroundWorker, HangfireBackgroundWorker>()`, `services.AddScoped<IFailedJobCounter, HangfireFailedJobCounter>()`) changes — only the location of the `JobStorage` binding they resolve against.

## Data Schemas

Not applicable. This change touches only `IServiceCollection` DI registrations — no database schema, API request/response shape, or event payload is created, removed, or altered.
