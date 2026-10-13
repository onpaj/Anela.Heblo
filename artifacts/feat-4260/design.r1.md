# Design: Complete the `ICronScheduler.UpdateCronSchedule` contract with `timeZoneId`

## Component Design

### `ICronScheduler` (port) — widened, not restructured
`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs`

Responsibility: describe one infrastructure operation — "apply a CRON schedule change to the running job scheduler, fire-and-forget" — with a signature that already carries everything an implementation needs, so no implementation has to reach back into DI for data the caller already holds.

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Applies a CRON expression update to a running job schedule immediately, without a restart.
/// Fire-and-forget: implementations log and swallow scheduling failures rather than throwing,
/// except for invalid arguments, which throw before any side effect.
/// </summary>
public interface ICronScheduler
{
    /// <param name="jobName">Registered job name; also the Hangfire recurring-job id.</param>
    /// <param name="cronExpression">Validated 5- or 6-field CRON expression.</param>
    /// <param name="timeZoneId">System/IANA time-zone id the schedule is evaluated in; must be resolvable on the host via TimeZoneInfo.FindSystemTimeZoneById.</param>
    /// <exception cref="ArgumentException">Any argument is null, empty or whitespace.</exception>
    void UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId);
}
```

Contract rules (binding on this and any future implementation):
1. **Fire-and-forget** — `void` return; scheduling failures (unknown job, unresolvable time zone, storage error) are logged, never surfaced to the caller.
2. **Eager argument validation** — null/empty/whitespace in any parameter throws `ArgumentException` (or subclass) before any side effect, including before a DI scope is created. This is the one exception to rule 1.
3. **Caller owns the time zone** — the implementation must not re-derive it from any other source (e.g. job metadata).
4. **`jobName` doubles as the Hangfire recurring-job id.**
5. **No infrastructure types in the signature** — no `Type`, no Hangfire type, no `IServiceProvider`.
6. Guard order in any implementation follows parameter order (`jobName`, `cronExpression`, `timeZoneId`) so a caller's mistaken argument produces a matching failure.

No new file, no `Contracts/` folder — stays alongside its siblings `IJobEnqueuer` / `IFailedJobCounter` in `Features/BackgroundJobs/Services/`, since this is an intra-module port over infrastructure, not a cross-module contract.

### `UpdateRecurringJobCronHandler` (consumer) — one-line change
`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs`

Responsibility unchanged: validate CRON, load `RecurringJobConfiguration`, persist the mutation, then trigger the live schedule update. The only change is the third argument passed to the port, sourced from the entity already in hand — no new repository call, no DI resolution:

```csharp
_scheduler.UpdateCronSchedule(job.JobName, job.CronExpression, job.TimeZoneId);
```

Ordering invariant preserved: this call happens strictly **after** `repository.UpdateAsync(job)` (the commit point) and its failure must never flip a successful DB write into an error response — the handler's existing `try`/`catch` around the scheduler call already enforces this and is not touched.

### `HangfireRecurringJobScheduler` (adapter) — narrows its own data reads, keeps its DI dependency for a different reason
`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs`

Responsibility: translate the port call into a Hangfire `RecurringJob.AddOrUpdate<TJob>` registration via `HangfireJobRegistrationHelper.RegisterOrUpdate(Type, jobName, cron, timeZoneId)`, which needs a runtime `Type` that only DI can currently supply for a given `jobName`.

```csharp
public void UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
    ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
    ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

    // Resolves the runtime Type for jobName, which HangfireJobRegistrationHelper.RegisterOrUpdate
    // requires to close its generic RecurringJob.AddOrUpdate<TJob> overload. A scope is used (not a
    // direct IEnumerable<IRecurringJob> injection) because this adapter is a singleton and IRecurringJob
    // implementations are scoped. Job metadata is deliberately not read here — the schedule's time zone
    // comes from the caller.
    using var scope = _serviceProvider.CreateScope();
    var jobType = scope.ServiceProvider.GetServices<IRecurringJob>()
        .FirstOrDefault(j => j.Metadata.JobName == jobName)?.GetType();

    if (jobType == null)
    {
        _logger.LogWarning(
            "Job {JobName} has no registered IRecurringJob implementation in DI — " +
            "its Hangfire schedule was not updated live. The DB configuration row was still saved; " +
            "the new schedule will apply on the next application start if the job type is restored.",
            jobName);
        return;
    }

    try
    {
        HangfireJobRegistrationHelper.RegisterOrUpdate(jobType, jobName, cronExpression, timeZoneId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex,
            "Failed to update live Hangfire schedule for {JobName}. " +
            "TimeZone '{TimeZoneId}' may be invalid or unsupported on this host.",
            jobName, timeZoneId);
        return;
    }

    _logger.LogInformation(
        "Live Hangfire schedule updated for {JobName} → {CronExpression} ({TimeZoneId})",
        jobName, cronExpression, timeZoneId);
}
```

Key boundary decision: `IServiceProvider` and its DI scope are **retained**, but strictly narrowed to `jobName → Type` resolution — `job.Metadata.TimeZoneId` is no longer read anywhere in this class. Removing `IServiceProvider` entirely (injecting `IEnumerable<IRecurringJob>` directly) is not viable: the adapter is `AddSingleton` while `IRecurringJob` implementations are `AddScoped`, so a direct injection would be a captive-dependency bug. `CreateScope()` per call is the correct pattern for a singleton consuming scoped services here, not incidental complexity.

Component/data-flow relationship after the change (one edge removed, one edge narrowed):

```
UpdateRecurringJobCronHandler
   (Application; owns RecurringJobConfiguration: JobName, CronExpression, TimeZoneId)
        │
        │  UpdateCronSchedule(jobName, cronExpression, timeZoneId)   ← DB-sourced tz, NEW
        ▼
ICronScheduler                              (port, Features/BackgroundJobs/Services)
        │  (DI, AddSingleton binding — unchanged)
        ▼
HangfireRecurringJobScheduler : ICronScheduler        (adapter, API/Infrastructure/Hangfire)
   deps: IServiceProvider (TYPE RESOLUTION ONLY, metadata.TimeZoneId no longer read)
         ILogger
        │
        │  1. guard args (jobName, cronExpression, timeZoneId — in that order)
        │  2. scope → GetServices<IRecurringJob>() → jobName → Type   (unchanged mechanism)
        │  3. HangfireJobRegistrationHelper.RegisterOrUpdate(type, jobName, cron, timeZoneId)
        ▼
Hangfire storage (RecurringJob record)
        ▲
        │  same helper, same tz-from-metadata behaviour, unchanged
RecurringJobDiscoveryService (IHostedService) — startup path, NOT touched by this change
```

Startup invariant this design relies on (unchanged, not part of this change's diff): `RecurringJobSeeder.SeedDefaultConfigurationsAsync` runs before `RecurringJobDiscoveryService` starts and re-writes each row's `TimeZoneId` from `IRecurringJob.Metadata.TimeZoneId` on every boot, and `RecurringJobConfiguration` rejects a null/empty `TimeZoneId` in its constructor and `UpdateConfiguration`. This guarantees `dbConfig.TimeZoneId == metadata.TimeZoneId` whenever a CRON edit occurs, which is what makes passing the DB value on the runtime path behaviour-preserving relative to today's metadata-read path.

### Out of scope for this component design
- No change to `RecurringJobDiscoveryService`, `RecurringJobSeeder`, `HangfireJobRegistrationHelper`, or the `RecurringJobConfiguration` entity.
- No `jobName → Type` registry / no removal of `IServiceProvider` (tracked as a separate follow-up; see arch review Decision 2/Q1).
- No change to `UpdateCronSchedule`'s `void`/fire-and-forget shape, and no surfacing of "job type not registered in DI" to the HTTP caller (tracked as a separate follow-up; see arch review Decision 4/Q2).

## Data Schemas

No database schema change. No OpenAPI/HTTP contract change. This section documents the shapes that change internally and confirms what does not.

### Changed: internal port signature (source-level contract, in-process only)

```csharp
// Before
void UpdateCronSchedule(string jobName, string cronExpression);

// After
void UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId);
```

| Parameter | Type | Constraints | Source |
|---|---|---|---|
| `jobName` | `string` | non-null/empty/whitespace; equals the Hangfire recurring-job id | `RecurringJobConfiguration.JobName` |
| `cronExpression` | `string` | non-null/empty/whitespace; validated 5- or 6-field CRON | `RecurringJobConfiguration.CronExpression`, already NCrontab-validated upstream in the handler |
| `timeZoneId` (new) | `string` | non-null/empty/whitespace; must resolve via `TimeZoneInfo.FindSystemTimeZoneById` on the host | `RecurringJobConfiguration.TimeZoneId` (DB row), **not** `IRecurringJob.Metadata.TimeZoneId` |

Consumers of this signature: exactly one call site (`UpdateRecurringJobCronHandler.cs:74`) and one implementation (`HangfireRecurringJobScheduler`), both in-solution — this is a source-level break only, no serialization boundary crosses it.

### Unchanged: `RecurringJobConfiguration` entity (DB-backed, EF Core)
`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs` — no fields added, removed, or retyped:

| Field | Notes |
|---|---|
| `JobName` (key) | `Entity<string>` primary key |
| `DisplayName` | admin-facing |
| `Description` | admin-facing |
| `CronExpression` | admin-owned, preserved across seeding |
| `TimeZoneId` | developer-owned; re-synced from `IRecurringJob.Metadata.TimeZoneId` by `RecurringJobSeeder` on every startup; required non-empty (enforced in constructor and `UpdateConfiguration`) — this is what makes it safe to read on the runtime path |
| `IsEnabled` | admin-owned, preserved across seeding |
| `LastModifiedAt` | set by the handler |
| `LastModifiedBy` | set by the handler |

### Unchanged: `RecurringJobMetadata` (code-side)
`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobMetadata.cs` — still carries `TimeZoneId` as before; still the source the startup discovery path (`RecurringJobDiscoveryService`) reads. Only the runtime CRON-edit path stops reading it.

### Unchanged: public HTTP surface
`UpdateRecurringJobCron` request/response DTOs, route, status codes and error codes (`InvalidCronExpression`, `RecurringJobNotFound`, `RecurringJobUpdateFailed`) are untouched — no OpenAPI diff, no TypeScript client regeneration, no frontend change. Response payload on the happy path remains `{ Success: true, CronExpression, LastModifiedAt, LastModifiedBy }` (unchanged shape).

### Unchanged: Hangfire storage record shape
`HangfireJobRegistrationHelper.RegisterOrUpdate(Type, jobName, cronExpression, timeZoneId)` signature and the record it writes to Hangfire storage (job type, method name, `Task` return type, cron, time zone) are untouched — only the source of the `timeZoneId` value passed into it changes (caller-supplied DB value instead of adapter-resolved metadata value), and per the seeder invariant above, the value itself is identical either way.

### End-to-end data flow (runtime CRON edit)

```
POST /api/recurring-jobs/{jobName}/cron
 → UpdateRecurringJobCronHandler
    1. NCrontab.Advanced validates cron          → InvalidCronExpression on failure
    2. repository.GetByJobNameAsync(jobName)     → RecurringJobNotFound on null
    3. job.UpdateCronExpression(cron, user, now)
    4. repository.UpdateAsync(job)               ── COMMIT POINT
    5. scheduler.UpdateCronSchedule(job.JobName, job.CronExpression, job.TimeZoneId)
       → HangfireRecurringJobScheduler
          a. guards (throw before any side effect)
          b. scope → GetServices<IRecurringJob>() → jobName → Type
             ├─ not found → LogWarning, return          (DB row already saved)
             └─ found ─► RegisterOrUpdate(type, name, cron, timeZoneId)
                          ├─ throws → LogError(jobName, timeZoneId), return
                          └─ ok     → LogInformation(name, cron, timeZoneId)
    6. return 200 { Success: true, CronExpression, LastModifiedAt, LastModifiedBy }
```

Step 5 is deliberately after the commit point (step 4) and its failure deliberately cannot flip the response to an error — this ordering and the surrounding `try`/`catch` are unchanged by this design.
