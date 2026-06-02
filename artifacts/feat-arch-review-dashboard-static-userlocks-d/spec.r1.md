# Specification: Bounded eviction for DashboardService per-user lock pool

## Summary
Replace the unbounded `static ConcurrentDictionary<string, SemaphoreSlim>` lock pool in `DashboardService` with a bounded, eviction-aware mechanism so that per-user `SemaphoreSlim` instances cannot accumulate for the lifetime of the process. The replacement must preserve the current per-user mutual exclusion guarantee across concurrent HTTP requests while ensuring evicted semaphores are disposed.

## Background
`DashboardService` (`backend/src/Anela.Heblo.Xcc/Services/Dashboard/DashboardService.cs:12`) is registered as **scoped** (`XccModule.cs:23`), but it currently uses a `static` dictionary to coordinate read-modify-write access to a user's dashboard settings across HTTP requests:

```csharp
private static readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();
```

The dictionary is keyed by `userId` and entries are created on first use via `GetOrAdd` (`DashboardService.cs:24`). Entries are never removed, so:

1. **Memory leak.** In a long-running production process, the dictionary grows unboundedly as new users (or deleted-and-recreated user IDs, or test user IDs) visit the dashboard. Each entry holds a `SemaphoreSlim` (~72 bytes + object overhead) plus the dictionary slot.
2. **Undisposed `IDisposable`.** `SemaphoreSlim` implements `IDisposable` and the existing code never disposes its instances.
3. **Test pollution / flakiness.** The `static` field is shared across all `DashboardService` constructions, including across xUnit test classes that share an `AppDomain`. Tests that exercise the same `userId` reuse the same `SemaphoreSlim` across runs, which can mask ordering bugs and produce flakiness.

The locking is needed because `GetUserSettingsAsync` performs a read-then-create-or-update on user settings, which can race when two concurrent requests for the same user arrive simultaneously (e.g. browser issuing dashboard + tile-data requests in parallel). The mutex must be process-wide (not per-scope) since each HTTP request gets its own DI scope.

## Functional Requirements

### FR-1: Per-user mutual exclusion preserved
Concurrent calls to `GetUserSettingsAsync(userId)` and `SaveUserSettingsAsync(userId, ...)` for the **same** `userId`, even across different DI scopes / HTTP requests, must be serialized exactly as they are today.

**Acceptance criteria:**
- Two concurrent `GetUserSettingsAsync` calls for the same `userId` against a repository where the user does not exist result in exactly one `AddAsync` invocation (no duplicate default-settings rows).
- Two concurrent calls for **different** `userId` values proceed in parallel (no cross-user blocking).
- A test reproducing the current behaviour passes both before and after the change.

### FR-2: Bounded memory footprint
The lock pool must not grow without bound. Idle per-user lock state must be released within a configurable time window after the user's last activity.

**Acceptance criteria:**
- After a user has not loaded/saved dashboard settings for the configured eviction window, the entry corresponding to that user is removed from the lock pool and the underlying `SemaphoreSlim` is disposed.
- Adding N distinct user IDs, waiting past the eviction window, and triggering eviction reduces the lock pool count back to 0 (verified via an exposed test hook or by a unit test using a controllable `IMemoryCache` / clock).

### FR-3: Safe disposal of evicted semaphores
A `SemaphoreSlim` removed from the lock pool must not be disposed while a caller still holds or is awaiting a permit on it. If a request is in flight when its lock is evicted, the in-flight operation must complete without error and the next request for that user must acquire a fresh semaphore safely.

**Acceptance criteria:**
- A unit test that (a) acquires a user lock, (b) forces eviction of that user's cache entry, (c) releases the held lock — completes without `ObjectDisposedException` and without losing exclusivity for the user.
- Concurrent acquisition during eviction either (a) re-uses the still-live semaphore until release, then disposes, or (b) yields a new semaphore and the old one is disposed after its last waiter releases. No path may dispose a semaphore that has live waiters.

### FR-4: DI registration unchanged at consumer level
`IDashboardService` remains registered as `Scoped` and its public interface (`IDashboardService.cs`) is unchanged. Callers (the MediatR handlers in `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/**`) require no modification.

**Acceptance criteria:**
- The `services.AddScoped<IDashboardService, DashboardService>()` line in `XccModule.cs` is preserved.
- No changes are made to `IDashboardService` or to any of the five `*Handler.cs` files under `Features/Dashboard/UseCases`.

### FR-5: Configurable eviction window
The sliding expiration for an idle per-user lock must be configurable through the existing `DashboardOptions` configuration section (`appsettings*.json` → `Dashboard:...`) with a sensible default.

**Acceptance criteria:**
- A new option `Dashboard:UserLockSlidingExpirationMinutes` (or equivalently named property on `DashboardOptions`) controls the sliding expiration window.
- Default value: **10 minutes**.
- Setting the option to a non-default value at startup is honoured by the implementation (verified by reading the bound option in the implementation, not by re-reading configuration on each call).

### FR-6: Tests no longer share per-user lock state
Unit tests that construct `DashboardService` directly (see `DashboardServiceTests.cs`) must not share lock state across test instances.

**Acceptance criteria:**
- Removing the `static` field eliminates the static field as a vehicle for cross-test state.
- Each `DashboardServiceTests` instance starts with an empty lock pool (verified by inspecting the count of cached lock entries, exposed via an internal hook if needed, or by demonstrating no cross-test interference).

## Non-Functional Requirements

### NFR-1: Performance
The per-call cost of acquiring a user lock must remain `O(1)` and not regress measurably from the current `ConcurrentDictionary.GetOrAdd` baseline. A microbenchmark or simple stopwatch test under 1k sequential acquisitions for the same user should remain within 2× of the current implementation.

### NFR-2: Memory bound
For an idle process (no traffic) for longer than the sliding expiration window, the lock pool entry count returns to 0. Steady-state lock pool size is bounded by the count of distinct users active within the sliding window — not by total historical users.

### NFR-3: Thread safety
The replacement must be safe under concurrent `GetOrAdd`-style access. The implementation must not introduce a race where two callers obtain different `SemaphoreSlim` instances for the same `userId` at the same time.

### NFR-4: Security
No change to authorization model. `userId` continues to be supplied by the caller (handlers extract it from the authenticated principal). The cache key derived from `userId` must use a stable, unambiguous prefix (e.g. `"dashboard-lock:{userId}"`) to avoid collision with any other cache consumer that shares the same `IMemoryCache`.

### NFR-5: Observability
No new logging is required by default, but disposal failures (e.g. evicting a semaphore whose `Dispose` throws) must not crash the eviction callback. Any unexpected exception during eviction is swallowed (caught and ignored) to avoid surfacing through `IMemoryCache`'s internal eviction path.

## Data Model
No domain model changes. Lock state is purely runtime/process state:

- **Lock entry** (in-memory only): `userId` → `SemaphoreSlim(1, 1)` with sliding expiration.

## API / Interface Design

### Implementation approach (recommended)
Use `IMemoryCache` keyed by `"dashboard-lock:{userId}"` with a sliding expiration and an eviction callback that disposes the semaphore:

```csharp
// In DashboardService (no longer uses any static field):
private readonly IMemoryCache _lockCache;
private readonly TimeSpan _lockSlidingExpiration;

private SemaphoreSlim GetUserLock(string userId)
{
    return _lockCache.GetOrCreate($"dashboard-lock:{userId}", entry =>
    {
        entry.SlidingExpiration = _lockSlidingExpiration;
        entry.RegisterPostEvictionCallback((_, value, _, _) =>
        {
            if (value is SemaphoreSlim sem)
            {
                try { sem.Dispose(); } catch { /* swallow */ }
            }
        });
        return new SemaphoreSlim(1, 1);
    })!;
}
```

### DI changes
- Register a **shared** `IMemoryCache` for the lock pool. The standard `services.AddMemoryCache()` in `Program.cs` is sufficient if it is already registered; otherwise add it in `XccModule.AddXccServices`. The same singleton cache is shared by all scoped `DashboardService` instances, which is what restores process-wide locking semantics.
- Inject `IMemoryCache` into `DashboardService` constructor.
- Bind `Dashboard:UserLockSlidingExpirationMinutes` to a new property on `DashboardOptions` (default `10`).

### Public interface
`IDashboardService` is unchanged. The constructor signature of `DashboardService` gains a single `IMemoryCache` dependency. Tests that construct `DashboardService` directly must pass a `MemoryCache` instance (e.g. `new MemoryCache(Options.Create(new MemoryCacheOptions()))`).

### Test hook (optional, internal)
To support FR-2 and FR-6 acceptance criteria, expose either:
- An `internal` constructor overload accepting an explicit `IMemoryCache`, **and**
- Either rely on `MemoryCache.Count` (available on the concrete type) or accept the `MemoryCache` concrete type in tests to inspect entry counts.

`InternalsVisibleTo` is already wired for the test assembly if needed; otherwise, the explicit `IMemoryCache` injection is enough.

## Dependencies
- `Microsoft.Extensions.Caching.Memory` — already a transitive dependency in any ASP.NET Core app; verify it is referenced from `Anela.Heblo.Xcc.csproj` and add if missing.
- No external services.
- No database / migration impact.

## Files expected to change
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/DashboardService.cs` — remove static dictionary; inject `IMemoryCache`; use new `GetUserLock`.
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/DashboardOptions.cs` — add `UserLockSlidingExpirationMinutes` (default 10).
- `backend/src/Anela.Heblo.Xcc/XccModule.cs` — ensure `services.AddMemoryCache()` is invoked (idempotent via `TryAdd*` if uncertain).
- `backend/src/Anela.Heblo.Xcc/Anela.Heblo.Xcc.csproj` — add `Microsoft.Extensions.Caching.Memory` reference if not already present.
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/DashboardServiceTests.cs` — update constructor calls to pass `IMemoryCache`; add new tests covering FR-1 (per-user mutex), FR-2 (eviction), FR-3 (safe disposal under contention).

## Out of Scope
- Replacing the locking strategy with optimistic concurrency / database-level row locking. Even though that would be a stronger design, it is a larger refactor of `GetUserSettingsAsync`/`SaveUserSettingsAsync` semantics and is not justified by the leak.
- Distributed locking (e.g. Redis). The application is a single-process Azure Web App; in-process locking is sufficient.
- Adding telemetry/metrics for lock contention or eviction rate.
- Refactoring `DashboardService` to use a scoped-only `IUserSettingsLock` service. (Discussed in the brief as an alternative; rejected because each HTTP request has its own DI scope, so a scoped lock would not serialize concurrent requests for the same user.)
- Changing any consumer (`*Handler.cs`) or `IDashboardService` shape.
- Touching `GetTileDataAsync` semantics (it calls `GetUserSettingsAsync` internally and inherits the new behaviour transparently).

## Open Questions
None.

## Status: COMPLETE