# Specification: Decouple feature-flag override cache refresh from request cancellation (#3971)

## Summary
Npgsql cancellation exceptions (TaskCanceledException at NpgsqlWriteBuffer.Flush and related) recurred at ~200/week after #3779. Code inspection shows `HebloFeatureProvider.GetOverridesAsync` passes the caller's (request-scoped) CancellationToken into the shared, 30s-TTL cache factory, and refreshes are not single-flight. This work makes the refresh independent of any single caller's token, serialises it, and adds diagnostics so the true cancellation source is observable.

## Background
- `EvaluateFlagsForClientHandler` runs `Task.WhenAll` over every registered flag; each call reaches `HebloFeatureProvider.GetOverridesAsync(ct)` with the HTTP request token.
- `IMemoryCache.GetOrCreateAsync` does not lock: on TTL expiry every concurrent caller runs the factory, each opening its own DI scope, DbContext and pooled connection (N-wide burst, matching the "3-6 simultaneous" bursts in the issue).
- The factory captures the *calling* request's `ct`. If that client aborts (or the burst contends for pool slots), the DB command is cancelled mid-write (`NpgsqlWriteBuffer.Flush`), surfacing TaskCanceledException/OperationCanceledException. Polly (#3779) only converts its own timeout into `TimeoutRejectedException`; caller-token cancellation is deliberately not retried, so #3779 cannot affect this path.
- `/health/ready` runs a DB health check on the same pool, so it fails in the same bursts (pool contention / shared stall), consistent with the issue's co-occurrence data.
- Provider swallows the exception and returns the default, so there is no 5xx, but the value is wrong-ish (default instead of DB override) and telemetry is noisy.

## Functional Requirements

### FR-1: Single-flight override refresh
Concurrent cache misses must result in exactly one DB query.
**Acceptance criteria:**
- N concurrent `ResolveBooleanValueAsync` calls on a cold cache invoke `IFeatureFlagOverrideRepository.GetAllAsDictionaryAsync` once.
- After TTL expiry the same holds.

### FR-2: Refresh not bound to a caller's token
The DB load uses its own bounded token (linked to application shutdown if available, plus a timeout, default 5s), never the caller's `ct`.
**Acceptance criteria:**
- Cancelling one caller's token does not cancel the in-flight load and does not fail other callers waiting on it.
- The cancelled caller observes `OperationCanceledException` from its own wait (or returns default through the existing catch) without a DB-level cancellation.
- A completed load triggered by a cancelled caller still populates the cache.

### FR-3: Failure behaviour preserved
Refresh failures are not cached; next call retries. Provider still falls back to config then registry default and never throws from `ResolveBooleanValueAsync`.
**Acceptance criteria:**
- Existing `HebloFeatureProviderTests` pass unchanged (or with only token-related adjustments).
- A failed load is not cached; a subsequent call reloads.

### FR-4: Cancellation-source diagnostics
Log distinguishes caller-cancelled (`ct.IsCancellationRequested`) from DB/timeout failure. Caller-cancelled is logged at Debug (not Warning) and carries a `cancelledBy=caller|refresh` field.
**Acceptance criteria:**
- Warning log no longer emitted when only the caller token was cancelled.
- Existing `Anela.DbRetryAttempts` fields retained.

## Non-Functional Requirements
- NFR-1 Performance: cache hit path stays lock-free (no semaphore acquisition on hit).
- NFR-2 Security: none; no data model or API change.
- NFR-3 Reliability: reduces peak concurrent connections from N(flags) to 1 on refresh.

## Data Model
None.

## API / Interface Design
No public change. `HebloFeatureProvider` internal only. Optional new internal constant `RefreshTimeout` (5s).

## Dependencies
Microsoft.Extensions.Caching.Memory, OpenFeature, existing `PollyExecutionStrategy`. Docs: `docs/processes/` entry for feature flags if one exists (process-doc rule).

## Out of Scope
- Changing Polly pipeline / `TotalTimeBudget`.
- `/health/ready` DB check tuning, Npgsql pool sizing (`Max Pool Size`), other cancellation sources (ConnectAsync, ExecuteReader elsewhere).
- 53300 slot-exhaustion (#3301).

## Open Questions
None. (Assumption: root cause is partly unprovable offline; FR-4 diagnostics let a follow-up confirm the residual volume after this change.)

## Status: COMPLETE
