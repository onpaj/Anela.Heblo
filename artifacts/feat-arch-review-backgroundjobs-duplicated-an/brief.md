## Module
BackgroundJobs

## Finding
Two separate classes build a generic `RecurringJob.AddOrUpdate<TJob>` call via reflection, but use different `Hangfire` API overloads:

**`RecurringJobDiscoveryService.RegisterRecurringJobInternal<TJob>`**  
`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs` lines 129–142:
```csharp
RecurringJob.AddOrUpdate<TJob>(
    jobName,
    job => job.ExecuteAsync(default),
    cronExpression,
    new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId) });
```

**`HangfireRecurringJobScheduler.UpdateJobInternal<TJob>`**  
`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs` lines 71–80:
```csharp
RecurringJob.AddOrUpdate<TJob>(
    jobName,
    j => j.ExecuteAsync(default),
    cronExpression,
    TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
```

The two overloads of `RecurringJob.AddOrUpdate` have different behaviour: the `RecurringJobOptions` overload is the newer API and supports additional options; the bare `TimeZoneInfo` overload is a legacy signature. This means startup registration (via `RecurringJobDiscoveryService`) and live CRON update (via `HangfireRecurringJobScheduler`) silently use different code paths, which could produce subtle inconsistencies when Hangfire updates its defaults for the legacy overload.

Both methods also duplicate the same reflection plumbing to resolve the generic method by name (`GetMethod(nameof(...), NonPublic | Static)` → `MakeGenericMethod(jobType)` → `Invoke`).

## Why it matters
- Real code duplication: the same reflection pattern appears in two places; future changes (e.g., adding a queue name or priority) must be applied twice
- Divergent overloads: a change to timezone handling or job options will only be applied to whichever site is edited first

## Suggested fix
Extract a single shared static helper (e.g., `HangfireJobRegistrationHelper.RegisterOrUpdate<TJob>`) in the API infrastructure layer, called by both `RecurringJobDiscoveryService` and `HangfireRecurringJobScheduler`. Standardise on the `RecurringJobOptions` overload. This removes the duplication and ensures startup registration and live updates follow identical paths.

---
_Filed by daily arch-review routine on 2026-05-27._