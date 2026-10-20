# Design: Feature-flag override single-flight refresh (#3971)

## Component Design
`HebloFeatureProvider` (internal sealed singleton) gains:
- `private readonly SemaphoreSlim _gate = new(1, 1);`
- `private Task<Dictionary<string,bool>>? _inflight;`
- `internal static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(5);`

`GetOverridesAsync(ct)`:
1. `ct.ThrowIfCancellationRequested();`
2. `if (_cache.TryGetValue(CacheKey, out Dictionary<string,bool>? hit) && hit is not null) return hit;`
3. Under `_gate` (acquired with `WaitAsync(ct)`): re-check cache; if `_inflight` null or completed, start `LoadAsync()`; capture the task; release gate.
4. `return await task.WaitAsync(ct);`

`LoadAsync()`: `using var cts = new CancellationTokenSource(RefreshTimeout)`; create scope; resolve `IFeatureFlagOverrideRepository`; `GetAllAsDictionaryAsync(cts.Token)`; `_cache.Set(CacheKey, result, CacheTtl)` on success only; `finally` clears `_inflight` (under gate or via Interlocked compare).

`ResolveBooleanValueAsync` catch: `if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)` -> `LogDebug("... cancelledBy=caller")`, else existing Warning with `cancelledBy=refresh|none`.

Tests (xUnit, existing fixture style): single DB call under 20 concurrent callers; caller cancel does not cancel repo token and cache still populated; failed load not cached then succeeds; hit path does not touch repository; caller-cancel logs no Warning.

## Data Schemas
None.
