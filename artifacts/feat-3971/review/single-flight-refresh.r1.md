# Code Review: feat-3971 single-flight override refresh

## Summary
The production change in `HebloFeatureProvider.cs` matches the task spec: the hit path is lock-free, misses are single-flight behind a `SemaphoreSlim` gate, the shared load runs on its own `RefreshTimeout` token rather than the caller's, callers only abandon their own wait, and failed loads are never cached. The defect is in the test suite: one new test asserts a synchronous cache write that depends on an inline continuation, which xUnit's test synchronization context does not provide. The implementer's "13 passed" claim has no test output behind it in the artifacts, so I could not confirm it.

## Review Result: REVISION_NEEDED

### task: single-flight-refresh
**Status:** REVISION_NEEDED
**Issues:**
- `ResolveBooleanValueAsync_LoadStartedByCancelledCaller_StillPopulatesCache` (`HebloFeatureProviderTests.cs`) calls `pending.SetResult(...)` and then immediately asserts `cache.TryGetValue(HebloFeatureProvider.CacheKey, ...)`. That assertion only holds if the continuation that calls `_cache.Set` (the `await repo.GetAllAsDictionaryAsync(...)` inside `LoadOverridesAsync`, line 140-141) runs inline. The test body runs on xUnit 2.9.2's `AsyncTestSyncContext`, and the `await` captures that context, so the continuation is posted and does not run before the next statement. The assertion is expected to fail, and the test comment ("the load's continuation (cache.Set) runs inline inside SetResult") is incorrect. Confidence is medium: I could not run the tests.
- Fix: remove the synchronous `TryGetValue` assertion, or make it observe population after a yield (for example, await the in-flight load through a second `ResolveBooleanValueAsync` call, which the existing `next` call and `Verify(Times.Once)` already do). Correct the comment. Then record a real `dotnet test --filter HebloFeatureProviderTests` run (all 13 passing) in the impl artifact.
- The impl artifact states "13 passed" but includes no test output, log, or TRX. The pass claim is therefore unverified.

## Verification of the spec requirements (by reading the code at cc185bf90)
- **FR-1 (single-flight):** Met. The miss path re-checks the cache under `_gate` (line 112), and only one `LoadOverridesAsync` is started while `_inflight` is incomplete (line 117). The 20-caller tests cover this.
- **FR-2 (refresh not bound to caller token):** Met. `LoadOverridesAsync` uses `new CancellationTokenSource(RefreshTimeout)` (line 137), not the caller's `ct`. Callers wait with `load.WaitAsync(ct)` (line 128).
- **FR-3 (failures not cached, never throws):** Met. `_cache.Set` runs only after a successful repository call (line 141). `ResolveBooleanValueAsync` still catches `Exception` (line 63), so a cancelled caller returns `Reason.Error`.
- **NFR-1 (lock-free hit path):** Met. The first `TryGetValue` (line 105) runs before any gate acquisition.
- **Doc accuracy:** `docs/development/feature-flags.md` line 87 ("cached for 30 seconds; invalidated immediately on admin writes") is still accurate. No doc update is needed.

## Overall Notes
- The task text was the only spec available to me. I judged FR-1 to FR-3 and NFR-1 against the summaries in `task-context/single-flight-refresh.md`, not the full `spec.r1.md`.
- `RefreshTimeout` of 5 seconds (spec-provided) means a repository call that takes longer than 5 seconds now fails every waiting caller with `Reason.Error` and a `TaskCanceledException` from the internal timeout. For a slow database, the cancellation telemetry this change targets is moved rather than removed. This is a consequence of the given design, not a defect in the implementation.
- `LoadOverridesAsync()` is called while `_gate` is held, so `CreateScope`, `GetRequiredService`, and the synchronous start of the repository call run under the lock. The lock is held only briefly, so this is minor.
- Pre-existing and not a regression: an admin write that calls `_cache.Remove(CacheKey)` while a load is in flight can let the load re-cache pre-write overrides for up to 30 seconds. The old `GetOrCreateAsync` had the same race.

**Status:** REVISION_NEEDED
