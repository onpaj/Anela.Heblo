# Architecture Review: Bounded eviction for DashboardService per-user lock pool

## Skip Design: true

Backend-only change. No UI components, screens, or visual design decisions are affected. `IDashboardService`'s public surface and the React frontend remain untouched.

## Architectural Fit Assessment

The change lives entirely inside `Anela.Heblo.Xcc`, which is the project explicitly designated for cross-cutting technical concerns (process-wide singletons, infrastructure helpers). Replacing the static lock pool with a bounded mechanism fits that mandate cleanly.

Two existing realities make the spec's "just inject `IMemoryCache`" approach workable in principle but **architecturally risky in practice**:

1. **`services.AddMemoryCache()` is already wired centrally** in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:352` and again in several feature modules (`CatalogModule`, `FeatureFlagsModule`, `PhotobankModule`, `KnowledgeBaseModule`, `FinancialOverviewModule`, `CatalogDocumentsModule`). The default `MemoryCache` singleton is **shared with ~10+ unrelated consumers**, all using string-key prefixes. Co-tenanting a *correctness primitive* (mutex lifetime) on the same cache as performance caches couples its disposal behaviour to choices those features may make in the future (`MemoryCacheOptions.SizeLimit`, manual `Compact()`, custom eviction reasons).
2. **The spec's recommended `RegisterPostEvictionCallback(... => sem.Dispose())` snippet does not satisfy its own FR-3.** Eviction (sliding-expiration or `Compact`) can fire *while a caller still holds or is awaiting the permit*. `SemaphoreSlim.Dispose()` on a held semaphore causes `ObjectDisposedException` at `Release()`. Worse, the next caller cache-misses and creates a **new** semaphore S2, violating mutual exclusion against the still-running S1 holder. The spec acknowledges this in FR-3 prose ("no path may dispose a semaphore that has live waiters") but its example code does not enforce it.

The right architectural shape isolates both concerns: a small, dedicated, internal lock-pool singleton in Xcc that owns its own `MemoryCache` instance, uses a reference-counted entry to make disposal race-free, and exposes a RAII handle to callers. `DashboardService` depends on that abstraction — not directly on `IMemoryCache`. This:

- Keeps the project's existing scoped/singleton conventions (`DashboardService` stays scoped, lock pool is a singleton).
- Removes the global `IMemoryCache` coupling, so future cache configuration cannot accidentally break locking semantics.
- Makes the abstraction reusable for the next "per-key serialize" need without copy-paste.
- Yields an obvious test seam (construct the concrete pool, inject it).

## Proposed Architecture

### Component Overview

```
backend/src/Anela.Heblo.Xcc/
└── Services/
    ├── Dashboard/
    │   ├── DashboardService.cs            (scoped) ── depends on ──┐
    │   ├── DashboardOptions.cs            (+ UserLockSlidingExpirationMinutes)
    │   └── IDashboardService.cs           (unchanged)              │
    └── Concurrency/                                                │
        ├── IKeyedAsyncLock.cs            (internal, new)  <───────┘
        ├── KeyedAsyncLock.cs             (singleton, new)
        │       ├── owns: dedicated MemoryCache instance
        │       ├── stores: LockEntry { SemaphoreSlim Sem; int RefCount }
        │       └── returns: IAsyncDisposable handle (releases + decrements refcount)
        └── XccModule.cs                  (+ AddSingleton<IKeyedAsyncLock,…>)
```

**Data flow for `GetUserSettingsAsync(userId)`**:
```
HTTP request → MediatR handler → DashboardService (scoped)
    → IKeyedAsyncLock.AcquireAsync("dashboard:{userId}", ct)
        → MemoryCache.GetOrCreate(key, entry { SlidingExpiration; eviction → TryDispose })
        → Interlocked.Increment(entry.RefCount)
        → entry.Sem.WaitAsync(ct)
        → returns Handle (IAsyncDisposable)
    → executes read-modify-write on _settingsRepository
    → Handle.DisposeAsync()
        → entry.Sem.Release()
        → Interlocked.Decrement(entry.RefCount); if 0 && evicted → entry.Sem.Dispose()
```

### Key Design Decisions

#### Decision 1: Dedicated lock-pool singleton instead of injecting the shared `IMemoryCache`

**Options considered:**
- **A.** Inject the shared `IMemoryCache` directly into `DashboardService` (as the spec recommends).
- **B.** Introduce an internal `IKeyedAsyncLock` singleton in Xcc that owns a *private* `MemoryCache` instance and exposes acquire-as-handle.
- **C.** Replace with a `ConcurrentDictionary<string, LockEntry>` + a single `Timer`-based reaper that periodically prunes idle entries.

**Chosen approach: B.**

**Rationale:**
- The shared `IMemoryCache` is a generic key/value cache; using it as a *lifetime owner of correctness primitives* couples our mutex correctness to every other module's caching choices (`SizeLimit`, `Compact`, eviction policies). A future "let's bound the cache to 100 MB" change in another module could begin evicting semaphores under memory pressure with disastrous effect.
- A dedicated `MemoryCache` instance is essentially free (a few-hundred-byte object) and gives the lock pool full ownership of its own expiration/compaction policy.
- The abstraction also hides ref-count + RAII handle plumbing from `DashboardService`, keeping the call-site dramatically smaller than today's nested try/finally and trivially correct for future callers.
- Option C is also viable and slightly leaner but requires hand-rolling a reaper timer; `MemoryCache`'s built-in sliding expiration gets us the timer behaviour for free.

#### Decision 2: Reference-counted disposal, not immediate dispose-on-evict

**Options considered:**
- **A.** Dispose the `SemaphoreSlim` immediately inside the eviction callback (the spec snippet).
- **B.** Wrap the semaphore in a `LockEntry { SemaphoreSlim Sem; int RefCount; bool Evicted }`. Eviction marks `Evicted=true`; the handle's release path checks `if (--RefCount == 0 && Evicted) Sem.Dispose()`.
- **C.** Skip `Dispose()` entirely — `SemaphoreSlim.Dispose()` is documented as optional unless `AvailableWaitHandle` is used (this code never uses it). Let GC reclaim.

**Chosen approach: B.**

**Rationale:**
- A defeats FR-3 — there is a real, easily-reproducible race between sliding-expiration eviction and an in-flight `WaitAsync()` call. The spec calls this out in prose but its code example is wrong.
- C is technically safe but the spec explicitly requires disposal (FR-2, FR-3 acceptance criteria). Refcounting satisfies the spec and is also defensive if the implementation ever switches to a primitive whose `Dispose` matters more (e.g. `AsyncReaderWriterLock` patterns).
- B is the standard solution for "shared, lifetime-bounded synchronization primitives" and is the only option that satisfies both FR-2 (entries evictable) and FR-3 (no premature disposal).

#### Decision 3: RAII handle (IAsyncDisposable), not raw "acquire/release" pair

**Options considered:**
- **A.** `IKeyedAsyncLock.AcquireAsync(key)` returns `Task`; caller wraps in try/finally and calls `ReleaseAsync(key)`.
- **B.** `IKeyedAsyncLock.AcquireAsync(key)` returns an `IAsyncDisposable` handle that releases + decrements refcount on dispose; caller writes `await using (await pool.AcquireAsync(key)) { ... }`.

**Chosen approach: B.**

**Rationale:**
- The refcount/release pair must execute *exactly once* in lockstep. The RAII handle makes it impossible to call them mismatched. `try/finally` with a manual release would be one source-edit away from a leak.
- The `await using` site at the caller is shorter and more readable than the current try/finally.
- Pattern matches the rest of the .NET ecosystem (`SemaphoreSlim`-based "AsyncLock" libraries all expose this shape).

#### Decision 4: Cache key prefix and configuration scope

**Chosen approach:**
- Keys inside `KeyedAsyncLock`'s private `MemoryCache` carry a class-internal prefix (`"lock:" + userKey`) purely as a defensive measure; because the cache is private to the pool, collision risk is nil.
- `DashboardService` passes a domain-specific key prefix (`$"dashboard:{userId}"`) when calling `AcquireAsync` so different consumers of `IKeyedAsyncLock` cannot collide.
- `Dashboard:UserLockSlidingExpirationMinutes` on `DashboardOptions` (default `10`) is read in `DashboardService` constructor and passed per-`AcquireAsync` call. `IKeyedAsyncLock` itself does not depend on `DashboardOptions` (keeps it generic).

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Xcc/
├── Anela.Heblo.Xcc.csproj                       # ADD PackageReference Microsoft.Extensions.Caching.Memory 8.0.1
├── XccModule.cs                                  # REGISTER singleton IKeyedAsyncLock, KeyedAsyncLock
├── Services/
│   ├── Concurrency/                              # NEW folder
│   │   ├── IKeyedAsyncLock.cs                    # NEW (internal interface)
│   │   └── KeyedAsyncLock.cs                     # NEW (internal sealed class)
│   └── Dashboard/
│       ├── DashboardOptions.cs                   # ADD UserLockSlidingExpirationMinutes (default 10)
│       └── DashboardService.cs                   # REMOVE static dict; INJECT IKeyedAsyncLock + IOptions<DashboardOptions>

backend/test/Anela.Heblo.Tests/
└── Features/Dashboard/
    ├── DashboardServiceTests.cs                  # MODIFY ctor; pass concrete KeyedAsyncLock
    └── KeyedAsyncLockTests.cs                    # NEW (covers FR-2/FR-3 in isolation)
```

`Anela.Heblo.Tests.csproj` already references `Anela.Heblo.Xcc` directly and pulls `Microsoft.Extensions.Caching.Memory` transitively via `Anela.Heblo.Application` — no extra package ref needed for tests.

### Interfaces and Contracts

```csharp
// backend/src/Anela.Heblo.Xcc/Services/Concurrency/IKeyedAsyncLock.cs
namespace Anela.Heblo.Xcc.Services.Concurrency;

internal interface IKeyedAsyncLock
{
    /// Acquires the per-key mutex and returns a handle that MUST be disposed to release.
    /// Caller controls sliding-expiration window per call so different consumers can
    /// reuse the singleton with their own configuration semantics.
    Task<IAsyncDisposable> AcquireAsync(
        string key,
        TimeSpan slidingExpiration,
        CancellationToken cancellationToken = default);
}
```

```csharp
// backend/src/Anela.Heblo.Xcc/Services/Concurrency/KeyedAsyncLock.cs (sketch)
internal sealed class KeyedAsyncLock : IKeyedAsyncLock, IDisposable
{
    private readonly MemoryCache _entries = new(new MemoryCacheOptions());

    public async Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        while (true)
        {
            var entry = _entries.GetOrCreate(key, e =>
            {
                e.SlidingExpiration = ttl;
                var le = new LockEntry();
                e.RegisterPostEvictionCallback((_, value, _, _) =>
                {
                    if (value is LockEntry ev) ev.MarkEvicted();
                });
                return le;
            })!;

            // CAS-style retry: ensure the entry we incremented is still the cached one.
            entry.AddRef();
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                await entry.Sem.WaitAsync(ct).ConfigureAwait(false);
                return new Handle(entry);
            }
            entry.ReleaseRef(); // may dispose if last reference and evicted
        }
    }

    public void Dispose() => _entries.Dispose();

    private sealed class LockEntry
    {
        public readonly SemaphoreSlim Sem = new(1, 1);
        private int _refCount;
        private int _evicted;

        public void AddRef() => Interlocked.Increment(ref _refCount);
        public void MarkEvicted() { Interlocked.Exchange(ref _evicted, 1); TryDispose(); }
        public void ReleaseRef() { Interlocked.Decrement(ref _refCount); TryDispose(); }
        private void TryDispose()
        {
            if (Volatile.Read(ref _evicted) == 1 && Volatile.Read(ref _refCount) <= 0)
            {
                try { Sem.Dispose(); } catch { /* NFR-5 */ }
            }
        }
    }

    private sealed class Handle : IAsyncDisposable
    {
        private readonly LockEntry _entry;
        private int _disposed;
        public Handle(LockEntry e) => _entry = e;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _entry.Sem.Release();
                _entry.ReleaseRef();
            }
            return ValueTask.CompletedTask;
        }
    }
}
```

**`DashboardService` call-site change:**

```csharp
private readonly IKeyedAsyncLock _lockPool;
private readonly TimeSpan _lockTtl;
// ctor: assign from injected IKeyedAsyncLock and IOptions<DashboardOptions>

public async Task<UserDashboardSettings> GetUserSettingsAsync(string userId)
{
    await using (await _lockPool.AcquireAsync($"dashboard:{userId}", _lockTtl))
    {
        // existing body, unchanged
    }
}
```

### Data Flow

1. **Cold-cache, single user:** `AcquireAsync` cache-misses → creates `LockEntry`, refcount=1, takes semaphore → handler runs → `DisposeAsync` releases semaphore + decrements refcount. Entry remains cached, idle, until sliding window expires.
2. **Concurrent same-user:** Both callers get the **same** `LockEntry` (refcount=2). First acquires permit; second blocks on `WaitAsync` until first disposes handle. Mutual exclusion preserved (FR-1).
3. **Eviction while in flight:** Sliding window elapses while one caller is inside the critical section. Eviction callback marks `_evicted=1`; `TryDispose` sees `refCount>0`, defers. Caller finishes, `ReleaseRef` → refcount=0 → `Sem.Dispose()`. The *next* caller for the same userId cache-misses and gets a fresh entry — safe because the previous one is already drained (FR-3).
4. **Eviction with no live holder:** Refcount=0 at eviction time → `MarkEvicted` calls `TryDispose` immediately → semaphore disposed → memory reclaimed (FR-2).
5. **CAS retry path:** Rare. A caller calls `GetOrCreate` (returns stale evicted entry already torn down) and increments refcount post-eviction. The `TryGetValue` re-check fails (current entry differs / absent) → `ReleaseRef` → loop, get a fresh entry. Prevents NFR-3 race where two callers obtain different live semaphores for the same key.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec's `sem.Dispose()` in eviction callback would corrupt mutual exclusion | High | Mandated refcount + deferred dispose (Decision 2) |
| Sharing the global `IMemoryCache` couples our locks to other modules' future cache config | Medium | Dedicated private `MemoryCache` owned by `KeyedAsyncLock` (Decision 1) |
| CAS race on `GetOrCreate` (entry evicted between create and ref-increment) | Medium | Re-check via `TryGetValue + ReferenceEquals` inside acquire loop |
| `Microsoft.Extensions.Caching.Memory` not currently referenced by `Xcc.csproj` (build failure) | Medium | Add the package reference (`8.0.1` to match the rest of the solution) |
| Cancellation during `WaitAsync` after `AddRef` would leak the refcount | Medium | Wrap `WaitAsync` in try/catch in `AcquireAsync`; on exception, `ReleaseRef` before rethrowing |
| Manual `try/finally`-style misuse if a future caller forgets `await using` | Low | Single chokepoint (`IKeyedAsyncLock`), return type forces `IAsyncDisposable` |
| `KeyedAsyncLock` becomes a footgun shared across unrelated keys (collision) | Low | Caller supplies fully-qualified key prefix (`"dashboard:..."`); document in interface XML doc |
| Test pollution between fixtures (FR-6) | Low | Each `DashboardServiceTests` instance constructs its own `KeyedAsyncLock` → no shared state |

## Specification Amendments

The spec is largely correct in *intent*. The following amendments make it correct in *implementation*:

1. **Replace the recommended `IMemoryCache` injection with an internal `IKeyedAsyncLock` abstraction in `Anela.Heblo.Xcc.Services.Concurrency`.** `DashboardService` no longer takes any `IMemoryCache` dependency. Rationale: see Decision 1.
2. **Replace the eviction snippet that calls `sem.Dispose()` directly with reference-counted disposal.** The current snippet does not meet the spec's own FR-3. Acceptance test for FR-3 should explicitly cover: (a) acquire, (b) force eviction via `Compact(1.0)` or short sliding window, (c) release — verify no `ObjectDisposedException` and that a concurrent acquirer arriving during eviction does not gain parallel access. Rationale: see Decision 2.
3. **`DashboardOptions.UserLockSlidingExpirationMinutes`** stays on `DashboardOptions` (default `10`). `DashboardService` reads it once in its constructor and passes it per-call to `AcquireAsync`. `IKeyedAsyncLock` itself takes the TTL per call — keeps the abstraction unaware of `DashboardOptions` and reusable.
4. **Add `Microsoft.Extensions.Caching.Memory` (8.0.1) to `Anela.Heblo.Xcc.csproj`.** It is currently absent. The Application project pulls 8.0.1; match that version.
5. **`XccModule.cs` registers the singleton:** `services.AddSingleton<IKeyedAsyncLock, KeyedAsyncLock>();`. No need to call `services.AddMemoryCache()` from `XccModule` — `KeyedAsyncLock` owns its own private `MemoryCache` instance and does not depend on the DI'd `IMemoryCache`.
6. **Cancellation hygiene:** `AcquireAsync` must accept `CancellationToken` and propagate. If `WaitAsync` throws (cancel or otherwise), the refcount increment must be undone before the exception leaves the method.
7. **`internal` visibility for `IKeyedAsyncLock`/`KeyedAsyncLock`** with `[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]` added to `Anela.Heblo.Xcc` so tests can construct the concrete pool directly. The Xcc project currently has no `InternalsVisibleTo` declaration — add one in a new `Properties/AssemblyInfo.cs` or via `<InternalsVisibleTo>` MSBuild item in the csproj.
8. **Test plan expansion:** add `KeyedAsyncLockTests.cs` that tests the primitive in isolation (concurrent acquisition, eviction safety, refcount correctness). The existing `DashboardServiceTests` only needs constructor updates and a single new test confirming per-user mutex (FR-1) end-to-end; the deep concurrency tests belong in the primitive's own test class.
9. **Document non-goal explicitly:** mention in the spec that `IKeyedAsyncLock` is process-local and not safe across multiple Web App instances; if Azure ever scales out the Web App, this needs revisiting (Redis / database row lock). The spec already calls out "single-process Azure Web App" as the deployment assumption — promote that to a comment on `KeyedAsyncLock` so future readers see the constraint.

## Prerequisites

- **None operational.** No DB migrations, no infrastructure changes, no Key Vault secrets, no Azure config.
- **One package addition** before implementation begins: `Microsoft.Extensions.Caching.Memory` 8.0.1 → `backend/src/Anela.Heblo.Xcc/Anela.Heblo.Xcc.csproj`.
- **One assembly-attribute addition:** `[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]` on `Anela.Heblo.Xcc` to enable tests to reach `internal` `IKeyedAsyncLock`.
- **No `appsettings*.json` change required for default behaviour;** adding `Dashboard:UserLockSlidingExpirationMinutes` is only needed if a non-default window is desired in a specific environment.