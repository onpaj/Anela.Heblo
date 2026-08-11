## Summary
Every time-dependent class in `Catalog/Infrastructure/` takes `TimeProvider` by constructor injection — `CatalogMergeService.cs:21,26`, `CatalogCacheStore.cs:46,61`, `CatalogDataRefreshService.cs:40,83`. `CatalogMergeScheduler` is the one exception: it calls `DateTime.UtcNow` directly and builds a raw `System.Threading.Timer`.

## Evidence
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs:47` — `var now = DateTime.UtcNow;` inside `ScheduleMerge`, used to compute `timeSinceFirstInvalidation` against `_options.MaxMergeInterval` (lines 55-70).
- `CatalogMergeScheduler.cs:102` — `_lastMergeCompleted = DateTime.UtcNow;` in `ExecuteMergeAsync`.
- `CatalogMergeScheduler.cs:18,74` — `private Timer? _debounceTimer;` / `_debounceTimer = new Timer(async _ => await ExecuteMergeAsync(), null, _options.DebounceDelay, Timeout.InfiniteTimeSpan);` instead of `TimeProvider.CreateTimer(...)`, which .NET 8's `TimeProvider` supports for exactly this use.
- The constructor (`CatalogMergeScheduler.cs:26-34`) takes `ILogger`, `IOptions`, `IHostApplicationLifetime` — no `TimeProvider`, unlike every sibling class in the same folder.

## Rule / intent violated
The project has fixed this exact gap — direct `DateTime.Now`/`UtcNow` instead of injected `TimeProvider` — in six other closed `arch-review` issues (#3773, #3748, #3403, #3495, #3488, #3633), establishing it as an actively-enforced, if undocumented, convention. Within this part specifically, every sibling class already follows it; `CatalogMergeScheduler` is the outlier.

## Why it matters (concrete)
`backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs` cannot fake time because the class doesn't accept an injected clock, so its tests drive debounce/max-interval logic with real wall-clock waits: `await Task.WhenAny(callbackFired.Task, Task.Delay(2000))` (line 80), `await Task.Delay(300); // > 2 × DebounceDelay` (line 300), `await Task.Delay(450); // extra window to confirm no second callback` (line 121), and similarly at lines 150-156, 196-203, 249-254, 324, 360+. Every one of the scheduler's test cases pays real sleep time (tens of ms to 2 full seconds), and the "assert nothing extra fires" cases are inherently timing-race-prone since they assert absence within a fixed real-time window rather than a controllable fake clock tick.

## Suggested direction (not a prescription)
Inject `TimeProvider` into `CatalogMergeScheduler` and replace `DateTime.UtcNow`/`new Timer(...)` with `TimeProvider.GetUtcNow()`/`TimeProvider.CreateTimer(...)`, matching the pattern already used by every other class in this file group.
