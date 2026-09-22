### task: update-hangfire-adapter-to-use-passed-timezone


**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs:8-61`

- [ ] **Step 1: Extend the class-level XML doc to record who owns the time zone**

The current class doc (lines 8–12) is:

```csharp
/// <summary>
/// Updates a Hangfire recurring job's CRON schedule live by delegating to
/// <see cref="HangfireJobRegistrationHelper"/> so the runtime-update path uses
/// the same registration code as startup discovery.
/// </summary>
```

Change it to:

```csharp
/// <summary>
/// Updates a Hangfire recurring job's CRON schedule live by delegating to
/// <see cref="HangfireJobRegistrationHelper"/> so the runtime-update path uses
/// the same registration code as startup discovery.
/// The caller owns the time zone: it comes from the <c>RecurringJobConfiguration</c> DB row
/// and is never re-derived from <c>IRecurringJob.Metadata</c>. That is equivalent today because
/// <c>RecurringJobSeeder</c> re-syncs each row's <c>TimeZoneId</c> from metadata on every startup,
/// before <see cref="RecurringJobDiscoveryService"/> registers anything. If <c>TimeZoneId</c> ever
/// becomes admin-editable, the startup discovery path must switch to the DB value as well and the
/// seeder must stop re-syncing that field.
/// </summary>
```

- [ ] **Step 2: Replace the `UpdateCronSchedule` method body**

The current method (lines 26–61) is:

```csharp
    public void UpdateCronSchedule(string jobName, string cronExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);

        using var scope = _serviceProvider.CreateScope();
        var jobs = scope.ServiceProvider.GetServices<IRecurringJob>().ToList();
        var job = jobs.FirstOrDefault(j => j.Metadata.JobName == jobName);

        if (job == null)
        {
            _logger.LogWarning("Job {JobName} not found in DI — Hangfire schedule not updated live", jobName);
            return;
        }

        try
        {
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                job.GetType(),
                jobName,
                cronExpression,
                job.Metadata.TimeZoneId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update live Hangfire schedule for {JobName}. " +
                "TimeZone '{TimeZoneId}' may be invalid or unsupported on this host.",
                jobName, job.Metadata.TimeZoneId);
            return;
        }

        _logger.LogInformation(
            "Live Hangfire schedule updated for {JobName} → {CronExpression}",
            jobName, cronExpression);
    }
```

Replace it with exactly this:

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

Points that are load-bearing and must not be "tidied":
- Guard order follows parameter order (`jobName`, `cronExpression`, `timeZoneId`), so a caller's wrong argument produces a matching failure.
- All three guards run **before** `_serviceProvider.CreateScope()`, so an invalid argument causes no side effect and no scope creation.
- `_serviceProvider` and its `ArgumentNullException` guard in the constructor (lines 18–24) stay untouched.
- Exactly one `CreateScope()`, one `GetServices<IRecurringJob>()` enumeration and one `RegisterOrUpdate` call per invocation (NFR-1).
- Do not add `throw` to the `catch` — the method stays fire-and-forget (`void`), because the handler calls it after the DB commit point.

- [ ] **Step 3: Confirm no metadata time-zone read remains**

Run: `grep -n "Metadata.TimeZoneId" backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs`

Expected output: no matches (exit code 1). The only remaining `Metadata` reference in this file must be `j.Metadata.JobName` inside the `FirstOrDefault` predicate — verify with:

Run: `grep -n "Metadata" backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs`

Expected output: exactly two lines — the `/// ... never re-derived from <c>IRecurringJob.Metadata</c> ...` doc line and the `.FirstOrDefault(j => j.Metadata.JobName == jobName)?.GetType();` line, plus the `// ... Job metadata is deliberately not read here ...` comment line (three lines total, one of them the lowercase-`metadata` comment).

- [ ] **Step 4: Build the API project**

Run: `cd backend && dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj`

Expected: `Build succeeded.` with 0 errors and no new warnings.

Note: `cd backend && dotnet build` on the whole solution still fails at this point, because the two test files still call the two-argument overload. The next task fixes that.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs
git commit -m "refactor(background-jobs): use the caller-supplied timeZoneId in HangfireRecurringJobScheduler"
```
