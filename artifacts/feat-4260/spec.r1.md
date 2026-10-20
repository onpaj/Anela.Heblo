# Specification: Complete the `ICronScheduler.UpdateCronSchedule` contract with `timeZoneId`

## Summary
`ICronScheduler.UpdateCronSchedule(string jobName, string cronExpression)` does not expose the time zone that its only implementation (`HangfireRecurringJobScheduler`) needs, so the implementation performs service location — it opens a DI scope and resolves `IEnumerable<IRecurringJob>` — purely to re-derive `Metadata.TimeZoneId`, a value the caller already holds. This spec adds `string timeZoneId` to the interface method, updates the single call site (`UpdateRecurringJobCronHandler`) to pass `job.TimeZoneId` from the already-loaded `RecurringJobConfiguration`, and tightens the implementation and its logging accordingly. This is a pure backend refactor: no DB schema change, no API contract change, no frontend change.

## Background
The BackgroundJobs module stores recurring-job schedules in the `RecurringJobConfiguration` table and applies CRON edits to the running Hangfire instance immediately, without a restart. The write path is:

1. `UpdateRecurringJobCronHandler` (Application layer) validates the CRON, loads the `RecurringJobConfiguration` entity, mutates it, persists it, then calls `ICronScheduler.UpdateCronSchedule(job.JobName, job.CronExpression)`.
2. `HangfireRecurringJobScheduler` (API/Infrastructure layer) resolves the job instance from DI to obtain its `Metadata.TimeZoneId`, then calls `HangfireJobRegistrationHelper.RegisterOrUpdate(jobType, jobName, cronExpression, timeZoneId)`.

The interface therefore describes a narrower contract than the implementation actually needs, and the gap is closed by service location inside the adapter. Consequences observed in the finding (issue #4260):

- Dependency Inversion violation: the abstraction does not carry all the data the implementation requires; the adapter reaches back into the container to find it.
- The `job == null` branch (job type not registered in DI, DB row still present) makes the Hangfire update a no-op while the handler still returns HTTP 200 with a success payload. It logs a warning, but nothing surfaces to the caller.
- Two independent sources for the same value (DB `RecurringJobConfiguration.TimeZoneId` and code `RecurringJobMetadata.TimeZoneId`) are read on different code paths.

**Important correction to the brief.** The brief's suggested fix states that the `IServiceProvider` dependency and DI scope can be removed entirely. That is not correct as written: `HangfireJobRegistrationHelper.RegisterOrUpdate` takes a `Type jobType` as its first argument (it closes a generic `RecurringJob.AddOrUpdate<TJob>` over that type by reflection, at `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs` lines 22–85). The scheduler still needs the runtime `Type` for the given `jobName`, and DI is the only place that mapping exists today. This spec therefore keeps the DI lookup for the **type** only and removes the dependency on DI for the **time zone**. See FR-3 and Open Questions Q1.

**Time-zone equivalence.** Passing the DB value instead of the metadata value is behaviour-preserving: `RecurringJobSeeder.SeedDefaultConfigurationsAsync` (`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` lines 30–59) re-writes `TimeZoneId` from `job.Metadata.TimeZoneId` on every startup for both new and existing rows — `TimeZoneId` is a developer-owned field, unlike `CronExpression`/`IsEnabled` which are admin-owned and preserved. `RecurringJobConfiguration` also rejects a null/empty `TimeZoneId` in both its constructor and `UpdateConfiguration`, so a persisted row always carries a non-empty value.

## Functional Requirements

### FR-1: Extend the `ICronScheduler` contract
`ICronScheduler.UpdateCronSchedule` gains a third parameter `string timeZoneId`, so the abstraction carries everything its implementations need.

File: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs`

```csharp
public interface ICronScheduler
{
    void UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId);
}
```

The XML doc comment on the interface is extended to state that `timeZoneId` must be an IANA/system time-zone id resolvable on the host, that the method is fire-and-forget (returns `void`, does not throw for a job it cannot find), and that failures are logged by the implementation rather than surfaced to the caller.

**Acceptance criteria:**
- `ICronScheduler` declares exactly one method, with the three-parameter signature above, in the same file and namespace as today.
- The parameter is `string` (non-nullable), positioned last, named `timeZoneId`.
- The interface remains in `Anela.Heblo.Application.Features.BackgroundJobs.Services` and takes no new `using` directives — no Hangfire, infrastructure, or `IServiceProvider` type appears in it.
- `dotnet build` on the solution succeeds with 0 errors and 0 new warnings.

### FR-2: Caller passes the time zone it already holds
`UpdateRecurringJobCronHandler` passes `job.TimeZoneId` from the `RecurringJobConfiguration` it has already loaded from the repository.

File: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs` (line 74)

```csharp
_scheduler.UpdateCronSchedule(job.JobName, job.CronExpression, job.TimeZoneId);
```

No other handler logic changes: CRON validation, the not-found branch, the DB write, the response shape, the logging statements, and the surrounding `try`/`catch` stay exactly as they are.

**Acceptance criteria:**
- The call at line 74 passes `job.TimeZoneId` as the third argument; no other line of the handler is modified.
- No extra repository call, DI resolution, or configuration lookup is introduced to obtain the time zone.
- The handler still returns `UpdateRecurringJobCronResponse` with `Success = true` and the persisted `CronExpression`/`LastModifiedAt`/`LastModifiedBy` on the happy path.
- The scheduler call remains **after** the successful `_repository.UpdateAsync(...)` — ordering is unchanged.

### FR-3: Implementation consumes the passed time zone; DI scope retained only for job-type resolution
File: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs`

Changes:
1. Signature becomes `public void UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId)`.
2. Add `ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);` alongside the existing guards for `jobName` and `cronExpression`, so an invalid argument fails fast before any DI scope is created.
3. The DI scope is still created to map `jobName` → runtime `Type` (required by `HangfireJobRegistrationHelper.RegisterOrUpdate`), but `job.Metadata.TimeZoneId` is **no longer read**. The value passed by the caller is used instead.
4. The error log in the `catch` block uses the passed `timeZoneId` (it currently reads `job.Metadata.TimeZoneId`).
5. The `IServiceProvider` constructor dependency and its `ArgumentNullException` guard stay, and the class stays registered as `services.AddSingleton<ICronScheduler, HangfireRecurringJobScheduler>();` in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (line 376).

Resulting body (illustrative):

```csharp
public void UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
    ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
    ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

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

**Acceptance criteria:**
- `job.Metadata.TimeZoneId` no longer appears anywhere in `HangfireRecurringJobScheduler.cs`.
- Calling with a null/empty/whitespace `timeZoneId` throws `ArgumentException` before any DI scope is created (assertable with a `IServiceProvider` that would throw if used, or by asserting the exception type and that no Hangfire record was written).
- When the job name is not registered in DI, the method returns without touching Hangfire storage and logs a warning that names the job and explains that the DB row was still saved. No exception is thrown.
- When `RegisterOrUpdate` throws (e.g. `TimeZoneNotFoundException` for an unknown zone id), the exception is caught, logged at Error with `{JobName}` and `{TimeZoneId}`, and not rethrown — unchanged from today's behaviour.
- The method still delegates to `HangfireJobRegistrationHelper.RegisterOrUpdate`, so the Hangfire record produced by a runtime update remains structurally identical to the one produced by startup discovery (job type, method name, `Task` return type, time zone).
- `dotnet format` reports no changes needed for the touched files.

### FR-4: Behaviour parity of the live Hangfire update
The time zone applied to the Hangfire recurring-job record after a CRON edit must be unchanged from today's behaviour for every job in the system.

**Acceptance criteria:**
- After `POST`ing a CRON change for a job whose DB row and `RecurringJobMetadata` agree on `TimeZoneId` (the invariant maintained by `RecurringJobSeeder`), the Hangfire recurring job record has the same `TimeZoneId` as before the change and the new `Cron`.
- The existing test `UpdateCronSchedule_AfterDiscoveryRegistration_UpdatesCronInStorage` still asserts `Assert.Equal("Europe/Prague", job.TimeZoneId)` and passes.
- The existing test `UpdateCronSchedule_ProducesIdenticalRecordStructureToDiscoveryRegistration` still passes with the discovery-registered time zone compared against the post-update record.

### FR-5: Update all tests to the new signature
Two test files call the affected members and must be updated. No test may be deleted or weakened to make the change compile.

- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs`
  - Lines 61 and 78: `_schedulerMock.Verify(s => s.UpdateCronSchedule(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);`
  - Line 125: strengthen to assert the time zone is forwarded from the entity, e.g. `_schedulerMock.Verify(s => s.UpdateCronSchedule("my-job", newCron, <the time zone the CreateTestJob helper builds the entity with>), Times.Once);`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs`
  - Lines 42, 67 and 107: pass `"Europe/Prague"` (matching `ParityTestRecurringJob.Metadata.TimeZoneId`) as the third argument.

New tests to add (in `HangfireRecurringJobSchedulerTests`):
- `UpdateCronSchedule_UsesPassedTimeZone_NotJobMetadataTimeZone` — register `parity-test-job` via discovery (metadata zone `Europe/Prague`), then call the scheduler with a different valid zone id (e.g. `UTC`) and assert the stored record's `TimeZoneId` is the passed value. This is the regression test that proves the argument, not the metadata, drives the registration.
- `UpdateCronSchedule_WithMissingTimeZoneId_ThrowsArgumentException` — `[Theory]` over `null`, `""`, `"   "`; asserts `ArgumentException` and that no recurring job record was created.
- `UpdateCronSchedule_WithUnresolvableTimeZone_LogsErrorAndLeavesScheduleUnchanged` — pass a bogus zone id (e.g. `"Not/AZone"`) for a job registered via discovery and assert no exception escapes and the stored record keeps its previous cron/time zone.

**Acceptance criteria:**
- `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~BackgroundJobs|FullyQualifiedName~Hangfire"` passes with 0 failures.
- No `It.IsAny<string>()` is used for the `timeZoneId` argument in the happy-path verification of `UpdateRecurringJobCronHandlerTests` (it must assert the concrete forwarded value).
- A repo-wide search for `UpdateCronSchedule(` returns no two-argument call site in `backend/`.

### FR-6: Update stale references in living docs (documentation only)
`docs/superpowers/plans/2026-03-29-db-driven-cron-config.md` shows the two-parameter signature. These are historical plan documents, not living specs.

**Acceptance criteria:**
- No change is made to files under `docs/superpowers/plans/` — they are point-in-time records of completed work. If any *non-plan* doc under `docs/` documents the `ICronScheduler` signature, it is updated to the three-parameter form. (A grep at spec time found the signature only in the two plan files, so the expected outcome is "no doc changes".)

## Non-Functional Requirements

### NFR-1: Performance
No measurable change is expected or required. The DI scope creation and `GetServices<IRecurringJob>()` enumeration remain (one scope per CRON edit, on a low-frequency admin action), but one fewer property read occurs. The method must remain synchronous and non-blocking beyond the existing Hangfire storage write.

**Acceptance criteria:** a single CRON update still performs exactly one DI scope creation, one `IRecurringJob` enumeration, and one `RecurringJob.AddOrUpdate` call.

### NFR-2: Security
No change to the security posture. The endpoint backing `UpdateRecurringJobCronHandler` keeps its existing authentication/authorisation attributes; no new data is exposed. `timeZoneId` originates from the application's own database (seeded from code metadata) and is never user-supplied through this path, so it is not a new injection surface. It is still passed to `TimeZoneInfo.FindSystemTimeZoneById`, whose failure mode (`TimeZoneNotFoundException`) is already caught and logged.

**Acceptance criteria:**
- No controller attribute, policy or feature flag is modified.
- Logged values are limited to job name, CRON expression and time-zone id — no user identity or secret is added to the log statements.

### NFR-3: Architectural conformance
The change must improve, not merely relocate, the layering.

**Acceptance criteria:**
- `Anela.Heblo.Application` gains no reference to Hangfire or to `Anela.Heblo.API`.
- `ICronScheduler` continues to be the only coupling point between the handler and the Hangfire adapter.
- The adapter's use of `IServiceProvider` is documented in a code comment stating precisely what it is still needed for (resolving the runtime `Type` for `HangfireJobRegistrationHelper.RegisterOrUpdate`), so the remaining service location is intentional and explained rather than incidental.

### NFR-4: Backward compatibility
`ICronScheduler` is an internal application abstraction with one implementation and one caller, both in this solution. The break is source-level only and fully contained.

**Acceptance criteria:** no generated OpenAPI change, no TypeScript client regeneration diff, no EF Core migration.

## Data Model
No schema change.

Entities involved (unchanged):

- `RecurringJobConfiguration` (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`) — `Entity<string>` keyed by `JobName`; fields `DisplayName`, `Description`, `CronExpression`, `TimeZoneId`, `IsEnabled`, `LastModifiedAt`, `LastModifiedBy`. `TimeZoneId` is required (non-empty enforced in the constructor and `UpdateConfiguration`) and is developer-owned: re-synced from code on each startup by `RecurringJobSeeder`. `CronExpression` and `IsEnabled` are admin-owned and preserved across seeding.
- `RecurringJobMetadata` (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobMetadata.cs`) — code-side defaults carried by each `IRecurringJob` implementation, including `TimeZoneId`.
- Hangfire's own recurring-job record in Hangfire storage — written by `HangfireJobRegistrationHelper.RegisterOrUpdate` from both the startup discovery path and the runtime update path.

Relationship after this change: the runtime update path reads `TimeZoneId` from the **database row** (via the handler) rather than from **metadata** (via DI). The startup path (`RecurringJobDiscoveryService`, line 78–82) continues to read it from metadata. Both remain consistent because of the seeder's re-sync.

## API / Interface Design

### Changed internal interface
```csharp
// Anela.Heblo.Application.Features.BackgroundJobs.Services
public interface ICronScheduler
{
    /// <summary>
    /// Applies a CRON expression update to a running job schedule immediately, without a restart.
    /// Fire-and-forget: implementations log and swallow scheduling failures rather than throwing,
    /// except for invalid arguments.
    /// </summary>
    /// <param name="jobName">Registered job name; also the Hangfire recurring-job id.</param>
    /// <param name="cronExpression">Validated 5- or 6-field CRON expression.</param>
    /// <param name="timeZoneId">System time-zone id the schedule is evaluated in; must be resolvable on the host.</param>
    /// <exception cref="ArgumentException">Any argument is null, empty or whitespace.</exception>
    void UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId);
}
```

### Unchanged public HTTP surface
The `UpdateRecurringJobCron` request/response DTOs, route, status codes and error codes (`InvalidCronExpression`, `RecurringJobNotFound`, `RecurringJobUpdateFailed`) are untouched. The frontend Background Jobs screen and generated TypeScript client require no change.

### Call flow after the change
```
POST (cron edit)
  → UpdateRecurringJobCronHandler
      ├─ validate CRON (NCrontab.Advanced)
      ├─ repository.GetByJobNameAsync  → RecurringJobConfiguration (has TimeZoneId)
      ├─ job.UpdateCronExpression(...) ; repository.UpdateAsync(...)
      └─ ICronScheduler.UpdateCronSchedule(job.JobName, job.CronExpression, job.TimeZoneId)
            → HangfireRecurringJobScheduler
                ├─ guard args
                ├─ DI scope → resolve runtime Type for jobName   (type only)
                └─ HangfireJobRegistrationHelper.RegisterOrUpdate(type, name, cron, timeZoneId)
```

## Dependencies
- Hangfire (`RecurringJob.AddOrUpdate<TJob>`, `RecurringJobOptions`, `JobStorage`) — already in use, no version change.
- `Microsoft.Extensions.DependencyInjection` — `IServiceProvider.CreateScope()` / `GetServices<IRecurringJob>()`, retained for type resolution.
- `NCrontab.Advanced` — CRON validation in the handler, untouched.
- Moq + xUnit + FluentAssertions, and `HangfireTestFixture` / the `"Hangfire"` xUnit collection for the in-memory-storage scheduler tests.
- No new NuGet package, no configuration key, no feature flag, no Key Vault secret.

## Out of Scope
- Making `UpdateCronSchedule` return a result or throw so that the handler can surface "job type not registered in DI" to the API caller. Today a missing job type yields HTTP 200 with the DB row updated and Hangfire silently unchanged; changing that alters the public API contract and the frontend's success handling, and belongs in a separate issue. (See Q2.)
- Removing the `IServiceProvider` dependency altogether by introducing a `jobName → Type` registry populated at startup. (See Q1.)
- Any change to `RecurringJobDiscoveryService`, `RecurringJobSeeder`, `HangfireJobRegistrationHelper`, the `RecurringJobConfiguration` entity, or the enable/disable use case.
- Allowing administrators to edit `TimeZoneId` from the UI; it remains developer-owned and re-synced from metadata on every startup.
- Any database migration, OpenAPI/TypeScript client regeneration, or frontend change.
- Rewriting the historical plan documents under `docs/superpowers/plans/`.

## Open Questions

**Q1. Should the `IServiceProvider` dependency be removed in this change, or kept?**
The brief asks for its removal, but `HangfireJobRegistrationHelper.RegisterOrUpdate` requires the runtime job `Type`, which only DI can supply today. *Assumption taken in this spec:* keep `IServiceProvider` for type resolution only, document why in a code comment, and treat a `jobName → Type` registry (built once at startup, injected as a typed abstraction) as a separate follow-up. Confirm this is acceptable, or the scope grows to include that registry and its startup wiring.

**Q2. Should a missing DI registration remain a silent HTTP 200?**
With this change the handler knows nothing about the adapter's failure to apply the schedule, exactly as today. The finding calls this out as a problem, but fixing it means either a return value on `ICronScheduler` or an exception (which the handler's existing `catch` would convert into `RecurringJobUpdateFailed` *after* the DB write already committed — a partial-success response). *Assumption taken in this spec:* out of scope; only the warning log is improved to explain the consequence. Confirm, or specify the desired API behaviour (e.g. a `202`-style "saved, applies after restart" signal in the response payload).

**Q3. Which time-zone value should the runtime path treat as authoritative if DB and metadata ever diverge?**
This spec uses the DB value (the caller's), which today always equals the metadata value because `RecurringJobSeeder` re-syncs it on startup. If a future requirement makes `TimeZoneId` admin-editable, the startup discovery path (which reads metadata) would need to change too, or the two paths would disagree until the next restart. *Assumption taken in this spec:* DB is authoritative on the runtime path; no change to the startup path. Confirm this is the intended direction.

**Q4. Should the new `UpdateCronSchedule_UsesPassedTimeZone_NotJobMetadataTimeZone` test use `"UTC"` as the contrasting zone?**
`"UTC"` resolves on both Linux containers and Windows dev machines, unlike most IANA ids on Windows without ICU. *Assumption taken in this spec:* yes, use `"UTC"` against the metadata default `"Europe/Prague"`. Flag if CI runs anywhere this would be brittle.

## Status: HAS_QUESTIONS
