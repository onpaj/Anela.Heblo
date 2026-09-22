### task: widen-icronscheduler-port-and-call-site


**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs:1-11`
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs:74`

- [ ] **Step 1: Replace the whole contents of `ICronScheduler.cs` with the widened, documented port**

The file is currently exactly this (11 lines):

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Applies a CRON expression update to a running Hangfire job schedule immediately,
/// without requiring a restart.
/// </summary>
public interface ICronScheduler
{
    void UpdateCronSchedule(string jobName, string cronExpression);
}
```

Replace it with exactly this:

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Applies a CRON expression update to a running job schedule immediately,
/// without requiring a restart.
/// Fire-and-forget: implementations log and swallow scheduling failures (unknown job,
/// unresolvable time zone, storage error) rather than throwing. The one exception is
/// invalid arguments, which throw before any side effect occurs.
/// The caller owns the time zone: implementations must not re-derive it from any other
/// source, such as job metadata.
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

Do **not** add any `using` directive to this file — it has none today and must still have none. No Hangfire, ASP.NET Core or `IServiceProvider` type may appear in it.

- [ ] **Step 2: Confirm the file still has zero `using` directives**

Run: `grep -c "^using" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs`

Expected output: `0`

- [ ] **Step 3: Pass the entity's `TimeZoneId` at the single call site**

In `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs`, the current lines 71–74 are:

```csharp
            job.UpdateCronExpression(request.CronExpression, modifiedBy, now);
            await _repository.UpdateAsync(job, cancellationToken);

            _scheduler.UpdateCronSchedule(job.JobName, job.CronExpression);
```

Change to:

```csharp
            job.UpdateCronExpression(request.CronExpression, modifiedBy, now);
            await _repository.UpdateAsync(job, cancellationToken);

            _scheduler.UpdateCronSchedule(job.JobName, job.CronExpression, job.TimeZoneId);
```

Change **nothing else** in this file: the CRON validation, the not-found branch, the `try`/`catch`, the response construction, the logging statements and the ordering (scheduler call strictly after `_repository.UpdateAsync`) all stay exactly as they are. Do not add a repository call, a DI resolution or a configuration lookup to obtain the time zone — `job` is the already-loaded `RecurringJobConfiguration` and carries `TimeZoneId`.

- [ ] **Step 4: Build the Application project**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

Expected: `Build succeeded.` with 0 errors and no new warnings.

Note: `cd backend && dotnet build` (the whole solution) is **expected to fail** at this point with `CS0535: 'HangfireRecurringJobScheduler' does not implement interface member 'ICronScheduler.UpdateCronSchedule(string, string, string)'` plus `CS1501` errors in the two test files. That is intentional — the next two tasks fix the adapter and the tests. Build only the Application project in this task.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs
git commit -m "refactor(background-jobs): add timeZoneId to ICronScheduler.UpdateCronSchedule and forward it from the handler"
```
