# Architecture Review: Feature-flag override cache single-flight (#3971)

## Skip Design: true
Backend-only, no UI.

## Architectural Fit Assessment
Change is confined to `Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs` (singleton OpenFeature provider). Fits vertical-slice layout; no layer or contract changes. Verified: cache factory at `GetOverridesAsync` forwards caller `ct` to `FeatureFlagOverrideRepository.GetAllAsDictionaryAsync`, which goes through `PollyExecutionStrategy.ExecuteAsync` (retry + 3s per-attempt timeout; caller-token cancellation is not classified transient). `EvaluateFlagsForClientHandler` fans out one call per flag via `Task.WhenAll`, so a cold cache yields an N-wide DB stampede. Hypothesis in the issue is refined: the token is the request token (confirmed by code), and it is the combination with non-single-flight refresh that creates correlated bursts.

## Proposed Architecture

### Component Overview
```
Request(ct) -> FeatureFlagChecker -> OpenFeature -> HebloFeatureProvider.ResolveBooleanValueAsync(ct)
   -> GetOverridesAsync(ct)
        cache hit  -> return
        miss       -> await shared Lazy<Task<Dict>> (single-flight, SemaphoreSlim gate)
                          refresh uses CancellationTokenSource(RefreshTimeout), NOT ct
        caller wait = task.WaitAsync(ct)   // caller cancel abandons the wait only
```

### Key Design Decisions

#### Decision 1: Single-flight via SemaphoreSlim + double-checked cache
**Options:** (a) `Lazy<Task>` stored in cache; (b) SemaphoreSlim gate; (c) background timer refresh.
**Chosen:** (b). Fast path `TryGetValue`; on miss, `await _gate.WaitAsync(ct)`, re-check cache, load, `Set`. Simple, failures naturally not cached.
**Rationale:** Minimal, testable; (a) caches faulted tasks unless handled; (c) is larger scope.

#### Decision 2: Load detached from caller token
Load runs with `new CancellationTokenSource(RefreshTimeout)`. Because the gate holder is a caller, run the load as `Task.Run`-free inline `await` but with its own token; a cancelled holder caller still completes the load only if it keeps awaiting — so the load is started as a shared `Task` (field `_inflight`) under the gate and awaited via `WaitAsync(ct)` by all callers including the starter. The starter's cancellation therefore never cancels the DB command.
**Rationale:** satisfies FR-2 for the starter case as well.

#### Decision 3: Logging
In catch: if `cancellationToken.IsCancellationRequested` and exception is OperationCanceledException -> LogDebug (cancelledBy=caller); else existing Warning.

## Implementation Guidance
### Directory / Module Structure
Edit `HebloFeatureProvider.cs`; extend `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs`. Update feature-flags process doc under `docs/processes/` if one covers it (check INDEX.md; run `python3 scripts/process-docs/check.py index`).

### Interfaces and Contracts
No interface change. `_inflight` is `Task<Dictionary<string,bool>>?` guarded by the gate; cleared in `finally` when completed. Provider is a singleton: use `SemaphoreSlim(1,1)`; do not dispose scope before the load task finishes (scope created inside the load).

### Data Flow
Hit: dictionary returned. Miss: first caller creates inflight task; others await it; result cached 30s; invalidation via existing `CacheKey` remove (admin writes) must still work, an in-flight load started before invalidation must not re-cache stale data (acceptable: <=30s, same as today).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Load hangs, all callers blocked | Medium | RefreshTimeout 5s on load; callers still bounded by own ct |
| Stale re-cache after admin invalidation | Low | Same window as current behaviour; document |
| Residual cancellations from health checks / other queries | Medium | Out of scope; FR-4 diagnostics allow measuring |

## Specification Amendments
None.

## Prerequisites
None (no migration/config). Optional config key not required.
