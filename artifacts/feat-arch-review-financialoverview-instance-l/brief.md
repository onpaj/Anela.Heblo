## Module
FinancialOverview

## Finding
`FinancialAnalysisService` declares an instance-level lock object:

```csharp
// FinancialAnalysisService.cs:22
private readonly object _refreshLock = new();
```

And uses it in `RefreshFinancialDataAsync` (lines 109-117) to guard a 10-minute refresh throttle check:

```csharp
lock (_refreshLock)
{
    var lastRefresh = _memoryCache.Get<DateTime?>(LAST_REFRESH_CACHE_KEY) ?? DateTime.MinValue;
    if (DateTime.UtcNow - lastRefresh < TimeSpan.FromMinutes(10))
    {
        _logger.LogDebug("Skipping refresh, last refresh was too recent");
        return;
    }
}
```

However, `FinancialAnalysisService` is registered as **Scoped** (`FinancialOverviewModule.cs:28`):

```csharp
services.AddScoped<IFinancialAnalysisService, FinancialAnalysisService>();
```

A Scoped service gets a **new instance per HTTP request**. The instance-level lock guards concurrent access *within a single request instance only* — a scenario that cannot occur because a single handler instance is not called concurrently. The lock provides zero protection against two simultaneous HTTP requests both triggering a refresh.

The actual throttle guard is the `IMemoryCache` timestamp check, which is shared across requests (correct). The `lock` around it is misleading: a reader would reasonably assume it prevents concurrent refreshes across requests, but it does not.

## Why it matters
The misleading lock creates false confidence that concurrent refreshes are blocked at the service level. If the background scheduler triggers a refresh concurrently with a user-initiated cache-miss refresh, both will pass the lock check and run in parallel.

## Suggested fix
**Option A (minimal):** Remove the instance lock and rely solely on the `IMemoryCache` timestamp check. `IMemoryCache` reads are thread-safe; the window between read and write is already narrow enough for the 10-minute throttle to be acceptable without a lock.

**Option B (correct for refresh coordination):** Register `FinancialAnalysisService` as **Singleton** — it is a cache coordinator, not a request-scoped service. Verify first that `ILedgerService` and `IStockValueService` are Singleton-compatible (or resolve them from a child scope inside `RefreshFinancialDataAsync`). With Singleton lifetime, the instance lock becomes meaningful.

Option A is the minimal safe fix; Option B is the architecturally correct one if the service is expected to guarantee at-most-one-refresh-in-flight.

---
_Filed by daily arch-review routine on 2026-06-06._