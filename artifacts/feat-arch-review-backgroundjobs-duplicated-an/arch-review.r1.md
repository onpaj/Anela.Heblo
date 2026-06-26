I have enough context. Key finding: the spec's FR-6 proposes placing the helper in `Anela.Heblo.API/Infrastructure/Hangfire/`, but this would create a circular dependency — the `Application` project does not (and per Clean Architecture cannot) reference `API`. The helper must live where both call sites can reach it: in the `Application` project (which already references `Hangfire.Core`).

```markdown
# Architecture Review: Consolidate Hangfire RecurringJob Registration

## Skip Design: true

## Architectural Fit Assessment

The feature is a pure backend refactor inside the BackgroundJobs vertical slice. It does not introduce a new pattern — it removes duplication of an existing one. The cross-cutting concern (binding a runtime `Type` to `RecurringJob.AddOrUpdate<TJob>`) is currently implemented twice, once in `Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs:79–142` and once in `Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs:41–81`, with divergent Hangfire overloads. Both call sites already depend on `IRecurringJob` (domain) and Hangfire's static `RecurringJob` API.

Main integration points:
- **`IRecurringJob`** (`Anela.Heblo.Domain.Features.BackgroundJobs.IRecurringJob`) — the generic constraint and the source of `Metadata.JobName`, `Metadata.CronExpression`, `Metadata.TimeZoneId`, and the `ExecuteAsync(CancellationToken)` entry point.
- **`RecurringJobDiscoveryService`** (`IHostedService` in API) — startup registration.
- **`HangfireRecurringJobScheduler`** (singleton service in Application, behind `IHangfireRecurringJobScheduler`) — runtime CRON updates, consumed by `UpdateRecurringJobCronHandler`.
- **Existing test fixture** (`backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobDiscoveryServiceTests.cs`) — already exercises the registration path via Hangfire's in-memory storage in a `[Collection("Hangfire")]` collection fixture.

### Critical correction to FR-6 (helper placement)

**The spec's proposed location (`Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs`) is not viable.** Project references confirm:

- `Anela.Heblo.API.csproj:58` references `Anela.Heblo.Application`.
- `Anela.Heblo.Application.csproj` does **not** reference `Anela.Heblo.API` (and must not — that would be a circular project reference).
- `Anela.Heblo.Application.csproj:13` already pulls in `Hangfire.Core` 1.8.21, so the Application layer can legally call `RecurringJob.AddOrUpdate<TJob>` and construct `RecurringJobOptions` directly.

The helper **must live in `Anela.Heblo.Application`** so `HangfireRecurringJobScheduler` can call it without inverting the layer dependency. `RecurringJobDiscoveryService` (in API) reaches it via the existing API → Application reference, which is the correct direction.

The spec's rationale (helper sits "next to the only layer that references Hangfire") is factually wrong: both layers already reference Hangfire, and only Application is reachable from both call sites.

## Proposed Architecture

### Component Overview

```
                 ┌─────────────────────────────────────────────────────────┐
                 │  Anela.Heblo.Domain.Features.BackgroundJobs             │
                 │    IRecurringJob                                        │
                 │    RecurringJobMetadata (JobName/Cron/TimeZoneId)       │
                 └─────────────────────────────────────────────────────────┘
                                          ▲
                                          │
   ┌──────────────────────────────────────┴──────────────────────────────┐
   │  Anela.Heblo.Application.Features.BackgroundJobs                    │
   │                                                                     │
   │   Services/                                                         │
   │     HangfireJobRegistrationHelper  ◄────── single call site to      │
   │       └─ RegisterOrUpdate(jobType, name, cron, tzId)                │
   │           └─ reflection → RegisterOrUpdateGeneric<TJob>             │
   │               └─ RecurringJob.AddOrUpdate<TJob>(..., RecurringJobOptions)
   │                                                                     │
   │     HangfireRecurringJobScheduler  ─── calls helper ───┐            │
   │     (IHangfireRecurringJobScheduler)                   │            │
   │                                                        ▼            │
   └────────────────────────────────────────────────────────┼────────────┘
                                          ▲                 │
                                          │  uses           │
   ┌──────────────────────────────────────┴─────────────────┴────────────┐
   │  Anela.Heblo.API.Infrastructure.Hangfire                            │
   │     RecurringJobDiscoveryService (IHostedService)                   │
   │       └─ for each IRecurringJob: calls helper                       │
   └─────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Helper location — Application, not API

**Options considered:**
- (A) Place helper in `Anela.Heblo.API/Infrastructure/Hangfire/` (spec's FR-6).
- (B) Place helper in `Anela.Heblo.Application/Features/BackgroundJobs/Services/`.
- (C) Place helper in a shared cross-cutting project (e.g., `Anela.Heblo.Xcc`).

**Chosen approach:** (B) — `Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs`.

**Rationale:** (A) is impossible (Application cannot reference API). (C) leaks Hangfire knowledge into a layer that today has no business with the BackgroundJobs slice. (B) co-locates the helper with `HangfireRecurringJobScheduler` (already in this folder), keeps the vertical slice cohesive, respects the existing Clean Architecture dependency direction (`API → Application → Domain`), and only requires Application to use `Hangfire.Core` — which it already does.

#### Decision 2: Helper API shape

**Options considered:**
- (A) Spec's signature: `static void RegisterOrUpdate(Type jobType, string jobName, string cronExpression, string timeZoneId)`.
- (B) Accept `IRecurringJob` instance and read metadata internally: `static void RegisterOrUpdate(IRecurringJob job, string cronExpression)`.

**Chosen approach:** (A). The CRON expression is **not** always `job.Metadata.CronExpression` — `RecurringJobDiscoveryService` prefers the DB-stored value over metadata (`RecurringJobDiscoveryService.cs:69–77`), and `HangfireRecurringJobScheduler.UpdateCronSchedule` is invoked precisely because a new CRON arrived from the API. The helper's contract therefore must accept CRON as an explicit parameter; making it derive from metadata would force callers to mutate metadata first or violate the immutability of `RecurringJobMetadata` (its members are `init`-only). The `jobType` + (name, cron, tz) tuple is the minimum stable surface.

#### Decision 3: Standardise on the `RecurringJobOptions` overload

**Options considered:**
- (A) Standardise on the legacy `TimeZoneInfo` overload.
- (B) Standardise on the `RecurringJobOptions` overload (spec's FR-2).

**Chosen approach:** (B). The `RecurringJobOptions` overload is the supported newer API (Hangfire 1.8+). The legacy overload is retained only for backward compatibility and risks divergent defaults (misfire policy, queue assignment) as Hangfire evolves. The startup path already uses (B); standardising the scheduler path closes the gap.

#### Decision 4: Generic dispatch via reflection (no change)

**Options considered:**
- (A) Keep the reflection plumbing (resolve a private generic method by name, close it with `MakeGenericMethod`, invoke).
- (B) Replace reflection with a non-generic Hangfire API path (e.g., `IRecurringJobManager.AddOrUpdate` with a serialised job descriptor).
- (C) Code-generate a dispatcher per `IRecurringJob` implementation.

**Chosen approach:** (A). (B) is explicitly out of scope per the spec, and Hangfire's non-generic surface requires more invasive changes (serialised job descriptor, manual storage interaction) that would break feature parity. (C) is gold-plating for an N≤10 set of recurring jobs invoked once at startup and per-CRON-edit. Reflection cost is negligible at this call rate.

#### Decision 5: Internal vs. public helper visibility

**Options considered:**
- (A) `internal static` (spec's sketch).
- (B) `public static`.

**Chosen approach:** (B) — `public static`. The helper sits in `Anela.Heblo.Application` and is consumed by `Anela.Heblo.API`. Because these are separate assemblies, `internal` would block the API call site. Existing service classes in this folder (`HangfireRecurringJobScheduler`) are also `public`. Keep the private generic dispatcher method (`RegisterOrUpdateGeneric<TJob>`) `private static` since reflection ignores visibility — this preserves the spec's intent of "no public generic surface".

## Implementation Guidance

### Directory / Module Structure

New file (the only file added):
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs`

Modified files:
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs`
  - Remove the `RegisterRecurringJobInternal<TJob>` method (lines 129–142) and the reflection dispatch (lines 79–96).
  - Replace with a single call: `HangfireJobRegistrationHelper.RegisterOrUpdate(jobType, metadata.JobName, cronExpression, metadata.TimeZoneId);`.
  - Add `using Anela.Heblo.Application.Features.BackgroundJobs.Services;`.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs`
  - Remove the `UpdateJobInternal<TJob>` method (lines 71–81) and the reflection dispatch (lines 42–55).
  - Replace with a single call: `HangfireJobRegistrationHelper.RegisterOrUpdate(jobType, jobName, cronExpression, job.Metadata.TimeZoneId);`.
  - Remove the now-unused `using System.Reflection;`.

No DI changes. No `BackgroundJobsModule` changes. No `csproj` changes.

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

public static class HangfireJobRegistrationHelper
{
    /// <summary>
    /// Registers or updates a Hangfire recurring job for the given runtime job type.
    /// Always uses the RecurringJobOptions overload to keep startup and live-update
    /// paths identical.
    /// </summary>
    /// <exception cref="ArgumentException">Any string argument is null/empty/whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="jobType"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="jobType"/> does not implement <see cref="IRecurringJob"/>.</exception>
    /// <exception cref="TimeZoneNotFoundException">The time zone id is not resolvable on this host.</exception>
    public static void RegisterOrUpdate(
        Type jobType,
        string jobName,
        string cronExpression,
        string timeZoneId);
}
```

Internal contract (private, reflection target):

```csharp
private static void RegisterOrUpdateGeneric<TJob>(
    string jobName,
    string cronExpression,
    string timeZoneId)
    where TJob : IRecurringJob
{
    RecurringJob.AddOrUpdate<TJob>(
        jobName,
        job => job.ExecuteAsync(default),
        cronExpression,
        new RecurringJobOptions
        {
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)
        });
}
```

Input validation order (fail fast, in this order, to keep error messages deterministic):
1. `ArgumentNullException.ThrowIfNull(jobType)`.
2. `ArgumentException.ThrowIfNullOrWhiteSpace(jobName)`.
3. `ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression)`.
4. `ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId)`.
5. `if (!typeof(IRecurringJob).IsAssignableFrom(jobType)) throw new ArgumentException($"{jobType.FullName} does not implement {nameof(IRecurringJob)}.", nameof(jobType));`.
6. Reflection: `typeof(HangfireJobRegistrationHelper).GetMethod(nameof(RegisterOrUpdateGeneric), BindingFlags.NonPublic | BindingFlags.Static)` → close → `Invoke`.

**Exception propagation:** Wrap the `Invoke` call so that `TargetInvocationException` is unwrapped (`ex.InnerException ?? ex`) and rethrown. Today `HangfireRecurringJobScheduler.UpdateCronSchedule` catches `Exception` and logs at line 57–64 — that catch must stay in the scheduler. Do **not** swallow exceptions inside the helper; let callers decide logging policy. (`RecurringJobDiscoveryService` already wraps each registration in a per-job try/catch at lines 67–107.)

### Data Flow

**Startup registration:**
```
RecurringJobDiscoveryService.StartAsync
  → resolve IEnumerable<IRecurringJob> from DI
  → load RecurringJobConfiguration list from repository
  → for each job:
      compute cronExpression (DB override or metadata default)
      → HangfireJobRegistrationHelper.RegisterOrUpdate(
            job.GetType(), metadata.JobName, cronExpression, metadata.TimeZoneId)
        → RegisterOrUpdateGeneric<TJob>(...)
          → RecurringJob.AddOrUpdate<TJob>(..., RecurringJobOptions { TimeZone = ... })
```

**Runtime CRON update:**
```
UpdateRecurringJobCronHandler.Handle
  → repository.UpdateAsync (persist new CRON)
  → IHangfireRecurringJobScheduler.UpdateCronSchedule(jobName, cron)
      → resolve IRecurringJob instance by JobName
      → HangfireJobRegistrationHelper.RegisterOrUpdate(
            job.GetType(), jobName, cronExpression, job.Metadata.TimeZoneId)
        → same code path as startup
```

Both paths now produce identical Hangfire `RecurringJob` records: same overload, same `RecurringJobOptions.TimeZone`, same defaults for the unspecified options.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Hangfire metadata drift between old and new code paths after deploy (existing recurring jobs in storage were last written with the legacy overload) | LOW | First scheduler call (or first startup after deploy) overwrites the entry with the `RecurringJobOptions` overload. The `RecurringJob.AddOrUpdate` upsert semantics handle this. No data migration required. |
| `TargetInvocationException` swallowed at the helper boundary, hiding root cause | MEDIUM | Helper unwraps `TargetInvocationException` and rethrows the inner exception. Existing per-job try/catch blocks in both callers log structured context. |
| `TimeZoneInfo.FindSystemTimeZoneById` fails on non-IANA hosts (Windows containers, exotic Linux images) | LOW | Behaviour is unchanged from today — both call sites already invoke this. Helper preserves the existing failure mode; scheduler keeps its catch + warning log. |
| Reflection target method renamed without test coverage breaking | MEDIUM | Helper passes `nameof(RegisterOrUpdateGeneric)` to `GetMethod`, so renames in modern IDEs follow. Add a unit test that asserts the helper can resolve and invoke the private dispatcher for an `IRecurringJob` test double (see Testing below). |
| Application now contains Hangfire registration logic that previously lived only in API infrastructure — perceived as a layering smell | LOW | Application already references `Hangfire.Core` and already contains `HangfireRecurringJobScheduler` and `HangfireJobEnqueuer` services. The helper is a peer to those existing services, not a new layering exception. |
| `RecurringJobOptions`-based registration may use different default queue/misfire policy than the legacy overload, causing a subtle behaviour change in production | LOW | Hangfire's `RecurringJobOptions` defaults match the legacy overload's defaults for everything except explicitly-set properties (only `TimeZone` is set). Verified by Hangfire docs. Behaviour parity test (see below) confirms. |

## Specification Amendments

### Amendment 1: Correct FR-6 helper location

Replace FR-6 acceptance criteria with:

> - The helper lives under `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs`.
> - The helper is `public static` so the API project can consume it across assembly boundaries.
> - Both consumers (`RecurringJobDiscoveryService` in `Anela.Heblo.API` and `HangfireRecurringJobScheduler` in `Anela.Heblo.Application`) compile without introducing new project references — the existing `Anela.Heblo.API → Anela.Heblo.Application` reference is sufficient.

**Rationale:** `Anela.Heblo.Application` cannot reference `Anela.Heblo.API`; placing the helper in API as the spec proposes would create a circular project reference. Application already references `Hangfire.Core`, so all required types are available.

### Amendment 2: Make helper visibility `public`

Replace the spec's `internal static class HangfireJobRegistrationHelper` with `public static class HangfireJobRegistrationHelper`. Cross-assembly consumption from the API project is required; `internal` would break the build. The private generic dispatcher remains `private static` — reflection resolves it via `BindingFlags.NonPublic`.

### Amendment 3: Explicit exception unwrapping

Add to FR-1 acceptance criteria:

> - The helper unwraps `TargetInvocationException` thrown by `MethodInfo.Invoke` and rethrows the inner exception so callers see the underlying Hangfire/TimeZone failure directly.

### Amendment 4: Add a behaviour-parity test

Add to FR-5 acceptance criteria (under existing test coverage):

> - A new test in `RecurringJobDiscoveryServiceTests.cs` (or a sibling `HangfireRecurringJobSchedulerTests.cs`) registers a recurring job via `RecurringJobDiscoveryService.StartAsync`, then updates its CRON via `HangfireRecurringJobScheduler.UpdateCronSchedule`, and asserts that the resulting Hangfire `RecurringJob` entry has the expected `Cron`, `TimeZoneId`, and method signature. This protects against future overload drift.

### Amendment 5: Helper unit tests

Add to FR-1 acceptance criteria:

> - A new `HangfireJobRegistrationHelperTests` class (mirroring `RecurringJobDiscoveryServiceTests`'s use of `[Collection("Hangfire")]` and in-memory storage) covers:
>   - Successful registration of a test `IRecurringJob` implementation.
>   - `ArgumentException`/`ArgumentNullException` on each invalid input.
>   - `ArgumentException` when `jobType` does not implement `IRecurringJob`.
>   - Exception unwrapping behaviour (e.g., invalid time-zone id surfaces as `TimeZoneNotFoundException`, not `TargetInvocationException`).

## Prerequisites

None. The refactor is self-contained:

- No database migration (Hangfire storage schema unchanged; `RecurringJobConfiguration` table unchanged).
- No DI / module registration changes (`BackgroundJobsModule` and `ServiceCollectionExtensions` are untouched).
- No new NuGet packages (`Hangfire.Core` is already a dependency of `Anela.Heblo.Application`).
- No configuration changes (`HangfireOptions`, `appsettings`, etc.).
- No public API surface change (HTTP, MediatR, DTOs, frontend, OpenAPI client all unaffected).
- Existing test infrastructure (`HangfireTestFixture`, `[Collection("Hangfire")]`) is reusable as-is for the new helper tests.
```