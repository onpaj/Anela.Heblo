## Module
Dashboard

## Finding
`DashboardService` (`backend/src/Anela.Heblo.Xcc/Services/Dashboard/DashboardService.cs`, line 12) declares:

```csharp
private static readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();
```

`GetUserLock` (line 24) adds a new `SemaphoreSlim` for every distinct `userId` via `GetOrAdd` but never removes entries. Because the field is `static`, it is shared across all instances of `DashboardService` for the lifetime of the process. Every user who ever loads their dashboard settings permanently occupies a slot in this dictionary.

In a long-running production process with many users the dictionary grows without bound, holding `SemaphoreSlim` objects (each ~72 bytes + object overhead) for users who may never return.

## Why it matters
- Unbounded static state is a memory leak. In practice it is small per entry, but it accumulates over months of uptime.
- `SemaphoreSlim` implements `IDisposable`; the stored semaphores are never disposed.
- The static lifetime means the leak is not reset between request scopes or between test runs, which can cause flaky tests if they share process state.

## Suggested fix
Use a bounded eviction strategy. The simplest safe fix is to replace the static `ConcurrentDictionary` with a `MemoryCache`-backed lock pool with a sliding expiration:

```csharp
// Inject IMemoryCache; cache semaphores with a sliding expiration of e.g. 10 minutes.
private SemaphoreSlim GetUserLock(string userId) =>
    _cache.GetOrCreate($"dashboard-lock:{userId}", entry =>
    {
        entry.SlidingExpiration = TimeSpan.FromMinutes(10);
        return new SemaphoreSlim(1, 1);
    })!;
```

Alternatively, if the write contention concern is only about concurrent requests for the **same** user, a scoped `IUserSettingsLock` service (one instance per DI scope) may be sufficient and simpler.

---
_Filed by daily arch-review routine on 2026-05-28._