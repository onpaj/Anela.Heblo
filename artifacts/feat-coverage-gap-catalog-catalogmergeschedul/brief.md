## Module / File
`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs`

## Coverage
Line coverage: 54.3% (filter threshold: 60%)

## What's not tested
The scheduler has several concurrency-sensitive branches with no test coverage:
- **Max-interval force-execute**: when `timeSinceFirstInvalidation >= MaxMergeInterval`, the scheduler bypasses debounce and fires immediately — this branch is unreachable in tests, so a condition reversal would make the max-interval guarantee silently disappear
- **Disposed/cancelled early returns**: `ScheduleMerge` has double-checked disposed guard (before and inside `lock`); `ExecuteMergeAsync` checks `_disposed`, `_mergeScheduled`, and `_mergeCallback != null` — all are uncovered
- **Semaphore already-locked skip**: `ExecuteMergeAsync` returns early when the merge semaphore cannot be acquired within 100 ms (merge already running) — the concurrent-call scenario is untested
- **`WaitForCurrentMergeAsync`**: returns immediately when no merge is in progress; otherwise waits for semaphore and immediately releases — the "no merge in progress" fast-path is the only covered path
- **`IsMergeInProgress` invariant**: `CurrentCount == 0` is only true while `ExecuteMergeAsync` holds the semaphore

## Why it matters
`CatalogMergeScheduler` coordinates background catalog refreshes. A broken debounce lets redundant merges pile up. A broken max-interval guard means invalidations can accumulate indefinitely without triggering a merge. The double-disposed check prevents timer callbacks firing after disposal — if that breaks, a disposed scheduler can still write to the ConcurrentDictionary or call the merge callback.

## Suggested approach
Unit tests with a fake `IHostApplicationLifetime` and a callback delegate:
1. Single `ScheduleMerge` within debounce window → verify callback is called once after delay
2. Multiple rapid `ScheduleMerge` calls → verify only one callback fires (debounce resets)
3. `ScheduleMerge` beyond `MaxMergeInterval` since first → verify immediate fire without waiting
4. Concurrent `ExecuteMergeAsync` call while merge in progress → verify second call skips (semaphore branch)
5. `WaitForCurrentMergeAsync` when merge is in progress → verify it blocks until merge completes
Effort: ~2 hours

---
_Filed by weekly coverage-gap routine on 2026-06-14. Based on CI run #27416879267 (3a6b7f99ee715caf82fc0efa17de5a5ede7b46fb)._