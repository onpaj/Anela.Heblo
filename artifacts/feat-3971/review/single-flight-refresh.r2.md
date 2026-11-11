# Code Review: feat-3971 single-flight override refresh (revision 2)

## Summary
Revision 2 changes one test only. Both r1 issues are resolved: the scheduling-dependent synchronous cache assertion is gone, and the impl artifact now carries real test output. Production code is unchanged from r1 and still meets FR-1, FR-2, FR-3 and NFR-1.

Reviewer note: this review ran inline in the same session as the developer pass (no subagent tool available), so it is not an independent reviewer. I re-read the task context, the r1 findings, the r2 diff and `HebloFeatureProvider.cs`, and the test results cited are ones observed in this session.

## Review Result: PASS

### task: single-flight-refresh
**Status:** PASS

## Check against r1 findings
- **Synchronous `TryGetValue` after `SetResult`:** fixed. The cache assertion now follows `await provider.ResolveBooleanValueAsync(...)`. If the load's continuation was posted rather than run inline, `_inflight` is still incomplete, so the second call joins it and `Times.Once` still holds; if it ran inline, the second call is a cache hit. The test passes in both orderings.
- **Incorrect comment:** fixed. The new comment states that inline execution depends on the synchronization context.
- **Unverified "13 passed":** fixed. `dotnet test --no-build --filter "FullyQualifiedName~HebloFeatureProviderTests"` gave `Failed: 0, Passed: 13, Total: 13` on three consecutive runs after the change; per-test output is in the impl artifact.
- The r1 prediction that the old assertion would fail did not reproduce (the r1 test also passed 3/3 here). The fix is still correct, because the old assertion relied on a scheduling detail.

## Spec requirements (code unchanged since cc185bf9)
- **FR-1 single-flight:** met; cache re-check and `_inflight` start under `_gate`.
- **FR-2 not bound to caller token:** met; `LoadOverridesAsync` uses its own `RefreshTimeout` token, callers use `load.WaitAsync(ct)`.
- **FR-3 failures not cached, never throws:** met; `_cache.Set` only after a successful repository call, `ResolveBooleanValueAsync` still catches all exceptions.
- **NFR-1 lock-free hit path:** met.
- Build 0 errors; `dotnet format` on the two files made no changes.

## Remaining observations (not blocking)
- The test deviates from the verbatim task-context snippet. That is the intended outcome of the r1 review.
- The revised test no longer proves the cache was populated before any second call. `Times.Once` plus the trailing cache assertion still show that the load started by the cancelled caller is what filled the cache.
- Carried over from r1, design-level and unchanged: a repository call slower than the 5 s `RefreshTimeout` fails all waiting callers with `Reason.Error`; and an admin `_cache.Remove` during an in-flight load can re-cache pre-write overrides for up to 30 s (pre-existing race).
- Only the `FeatureFlags` test filter (25 passed) was run, not the full test project.

## Docs to Update
None for this task; `update-process-doc` is a separate pending task.

**Status:** PASS
