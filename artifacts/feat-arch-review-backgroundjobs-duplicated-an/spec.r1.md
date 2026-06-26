# Specification: Consolidate Hangfire RecurringJob Registration

## Summary
Two classes in the BackgroundJobs module duplicate the reflection-based plumbing that invokes the generic `RecurringJob.AddOrUpdate<TJob>` Hangfire API, and they call different overloads (legacy `TimeZoneInfo` vs. newer `RecurringJobOptions`). This refactor extracts a single shared helper that standardises on the `RecurringJobOptions` overload, eliminating duplication and ensuring startup registration and live CRON updates follow identical code paths.

## Background
`RecurringJobDiscoveryService` registers recurring jobs at application startup by discovering `IRecurringBackgroundJob` implementations and binding them to Hangfire. `HangfireRecurringJobScheduler` is the runtime-facing scheduler that updates the CRON expression of an existing recurring job when configuration changes (e.g., via admin UI or API).

Both classes use reflection to resolve a generic private method by name (`GetMethod(..., NonPublic | Static)` → `MakeGenericMethod(jobType)` → `Invoke`) so that they can call `RecurringJob.AddOrUpdate<TJob>` with a `TJob` only known at runtime. The reflection plumbing is identical, but the inner method bodies bind to different Hangfire overloads:

- `RecurringJobDiscoveryService.RegisterRecurringJobInternal<TJob>` — uses the `RecurringJobOptions` overload (newer API, extensible with queue name, priority, misfire handling, etc.).
- `HangfireRecurringJobScheduler.UpdateJobInternal<TJob>` — uses the bare `TimeZoneInfo` overload (legacy signature).

Risks of the status quo:
- **Divergent behaviour**: Hangfire may apply different defaults to the legacy overload (e.g., misfire policy, queue assignment) than to the `RecurringJobOptions` overload. Startup and runtime updates can therefore produce subtly different `RecurringJob` records in storage.
- **Double-edit burden**: Adding a queue name, priority, or any other Hangfire option requires editing both files. A miss leaves the system half-configured.
- **Reflection drift**: Two copies of the reflection plumbing must stay in sync (method name, binding flags, generic dispatch).

## Functional Requirements

### FR-1: Single Hangfire registration helper
Introduce a single static helper that encapsulates both the reflection-based generic dispatch and the call to `RecurringJob.AddOrUpdate<TJob>`. The helper accepts the runtime `Type` of the job, the job name, the CRON expression, and the time zone identifier.

**Acceptance criteria:**
- A new static class (e.g., `HangfireJobRegistrationHelper`) exposes a method such as `RegisterOrUpdate(Type jobType, string jobName, string cronExpression, string timeZoneId)` (or equivalent signature) that performs the registration.
- The helper internally calls the generic `RecurringJob.AddOrUpdate<TJob>` overload that accepts `RecurringJobOptions` (the newer API).
- The reflection plumbing (`GetMethod(...) → MakeGenericMethod → Invoke`) lives in this helper only — no other class duplicates it.
- The helper validates inputs (non-null/non-empty job name, CRON expression, time zone id) and fails fast with a clear exception if the supplied `Type` does not implement the expected job interface.

### FR-2: Standardise on `RecurringJobOptions` overload
Both startup registration and runtime CRON updates must go through the helper and therefore use the same `RecurringJobOptions`-based overload.

**Acceptance criteria:**
- After refactor, the codebase contains exactly one call site for `RecurringJob.AddOrUpdate<TJob>` (inside the helper).
- That call site passes a `RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId) }`.
- The bare `TimeZoneInfo` overload (`RecurringJob.AddOrUpdate<TJob>(name, expr, cron, TimeZoneInfo)`) is no longer referenced anywhere in the solution.

### FR-3: Refactor `RecurringJobDiscoveryService`
`RecurringJobDiscoveryService.RegisterRecurringJobInternal<TJob>` (and any associated reflection helpers) is removed; the service now delegates to the shared helper.

**Acceptance criteria:**
- `RecurringJobDiscoveryService` no longer contains a private generic `RegisterRecurringJobInternal<TJob>` method nor its reflection-based dispatcher.
- The service calls `HangfireJobRegistrationHelper.RegisterOrUpdate(...)` (or equivalent) once per discovered job.
- Discovered jobs continue to be registered at startup with the same job name, CRON expression, and time zone as before the refactor.

### FR-4: Refactor `HangfireRecurringJobScheduler`
`HangfireRecurringJobScheduler.UpdateJobInternal<TJob>` (and its reflection dispatcher) is removed; runtime updates go through the shared helper.

**Acceptance criteria:**
- `HangfireRecurringJobScheduler` no longer contains a private generic `UpdateJobInternal<TJob>` method nor its reflection-based dispatcher.
- The scheduler calls `HangfireJobRegistrationHelper.RegisterOrUpdate(...)` (or equivalent) when applying a CRON update.
- An existing recurring job updated through the scheduler ends up with identical Hangfire metadata as one freshly registered at startup with the same CRON and time zone.

### FR-5: Behaviour parity
The refactor must not change observable behaviour: the same set of recurring jobs is registered with the same names, CRON expressions, and time zones; runtime CRON updates take effect as before.

**Acceptance criteria:**
- After the refactor, the set of recurring jobs visible in the Hangfire dashboard (`/hangfire/recurring`) for a given configuration is identical to the pre-refactor set (same names, CRON expressions, time zones).
- A CRON update through `HangfireRecurringJobScheduler` updates the `Cron` field of the corresponding Hangfire recurring job entry in the same way as before.
- Backend tests covering startup registration and CRON-update flows continue to pass.

### FR-6: Helper placement
The helper lives in the API infrastructure layer (next to `RecurringJobDiscoveryService`), since that is the only layer that already references the Hangfire API directly. `HangfireRecurringJobScheduler` (in `Application.Features.BackgroundJobs.Services`) already references Hangfire types, so a dependency on the API infrastructure helper is acceptable.

**Acceptance criteria:**
- The helper lives under `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/` (e.g., `HangfireJobRegistrationHelper.cs`).
- Both consumers (`RecurringJobDiscoveryService` and `HangfireRecurringJobScheduler`) compile and resolve the helper without introducing new project-level circular dependencies.

## Non-Functional Requirements

### NFR-1: Performance
No performance regression. Registration is invoked once per job at startup and per ad-hoc update; reflection cost is unchanged (still one `GetMethod` + `MakeGenericMethod` + `Invoke` per call). No need to cache the reflected `MethodInfo` in this refactor; if a future profile shows it matters, that's a follow-up.

### NFR-2: Security
No new security surface. The helper is internal to the application and only invoked from trusted server-side code paths that already have authority to register Hangfire jobs.

### NFR-3: Maintainability
After the refactor, future changes to recurring-job registration (e.g., adding a queue name, priority, misfire handling, or attempts) must be made in exactly one place. The reflection plumbing exists in exactly one location.

### NFR-4: Testability
The helper should be unit-testable in isolation where practical. Where Hangfire's static API makes direct unit testing impractical, existing integration coverage of the two consumers is sufficient.

## Data Model
No data model changes. Hangfire's existing `RecurringJob` storage records are unaffected in structure; only the code that writes them is consolidated.

## API / Interface Design

### New helper (sketch)
```csharp
namespace Anela.Heblo.API.Infrastructure.Hangfire;

internal static class HangfireJobRegistrationHelper
{
    public static void RegisterOrUpdate(
        Type jobType,
        string jobName,
        string cronExpression,
        string timeZoneId);
}
```

Internally:
1. Validates inputs.
2. Resolves a private generic method `RegisterOrUpdateGeneric<TJob>` via reflection.
3. Constructs the closed generic via `MakeGenericMethod(jobType)`.
4. Invokes it; the generic body calls:
   ```csharp
   RecurringJob.AddOrUpdate<TJob>(
       jobName,
       job => job.ExecuteAsync(default),
       cronExpression,
       new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId) });
   ```

No public API surface (HTTP endpoints, MediatR requests, DTOs, frontend) changes.

## Dependencies
- **Hangfire** — existing dependency; standardising on `RecurringJobOptions` requires no version bump (the overload is already in use at the startup-registration site).
- **`IRecurringBackgroundJob`** (or equivalent interface defining `ExecuteAsync(CancellationToken)`) — assumed to be the contract that both call sites rely on; the helper closes the generic over types that implement this interface.

## Out of Scope
- Adding new Hangfire options (queue name, priority, misfire handling, retry attempts) — the refactor only consolidates current behaviour; new options are a follow-up.
- Caching the reflected `MethodInfo` for micro-optimisation.
- Rewriting recurring-job discovery (assembly scanning, interface conventions) — `RecurringJobDiscoveryService`'s discovery logic stays as-is; only its registration call is rerouted.
- Migrating away from reflection by introducing a non-generic Hangfire API path or a code-generated dispatcher.
- Changes to the admin UI or API endpoints that drive runtime CRON updates.
- Any database migration — Hangfire's storage schema is untouched.

## Open Questions
None.

## Status: COMPLETE