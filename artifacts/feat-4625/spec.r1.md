# Specification: Photobank delta sync recovers from Graph 410 Gone

## Summary
The nightly `PhotobankIndexJob` fails for a root whose stored Graph delta token has expired (HTTP 410 Gone) and retries the same stale token forever, so incremental indexing silently stops. The job must discard the stale token on 410 and re-run once as a full enumeration.

## Background
`PhotobankIndexJob.IndexRootAsync` calls `IPhotobankGraphService.GetDeltaAsync(driveId, rootItemId, root.DeltaLink, ct)`. `PhotobankGraphService.GetDeltaAsync` calls `EnsureSuccessStatusCode`, throwing `HttpRequestException` (StatusCode = Gone) on 410. The job's catch-all logs and swallows it; `root.DeltaLink` is only updated on success, so the next night uses the same token. Telemetry: 4 nightly failures 2026-11-03..11-07. Graph documents 410 as "resync required": discard the token and enumerate afresh.

## Functional Requirements

### FR-1: Reset stale delta token on 410
When `GetDeltaAsync` fails with `HttpRequestException` whose `StatusCode == HttpStatusCode.Gone` and the call used a non-empty `root.DeltaLink`, the job clears `root.DeltaLink` (sets null), logs a warning, and retries `GetDeltaAsync` once with a null token (full enumeration).
**Acceptance criteria:**
- Retry call is made with `deltaLink == null`; the first call used the stored token.
- A successful retry flows through the normal upsert/delete path and persists the new `DeltaLink` and `LastIndexedAt`.
- Exactly two `GetDeltaAsync` calls occur (no loops).

### FR-2: No retry when not applicable
- 410 with an already-empty `DeltaLink` is not retried (propagates to the existing catch/log).
- Non-410 errors are not retried and behave as today.
- If the retry itself fails, the exception propagates to the existing per-root catch (logged, other roots continue).

### FR-3: Tests
Unit tests in `PhotobankIndexJobTests` cover: 410 with token then success (retry with null, new link persisted); 410 with no token (single call, no persist of new link); non-410 error with token (single call); 410 on retry (two calls, logged, no exception escapes).

### FR-4: Docs
Update `docs/processes/sync-photobank-index.md` (flow step 2 and the "expired DeltaLink" known quirk) in the same PR; bump `verified_at`.

## Non-Functional Requirements
### NFR-1: Performance
A full enumeration happens at most once per affected root per run; the delta result is already buffered in memory today.
### NFR-2: Security
No change; no new data exposure. Log messages must not include the delta token URL (it carries a token).

## Data Model
No schema change. `PhotobankIndexRoot.DeltaLink` is set to null in memory on 410 and then overwritten by the new link on success.

## API / Interface Design
No public API or `IPhotobankGraphService` signature change. 410 detection is in the job via `HttpRequestException.StatusCode`.

## Dependencies
Microsoft Graph delta API; existing Moq/xUnit test setup.

## Out of Scope
- Reconciling photos deleted while the token was stale (a full enumeration reports no deletions; orphan rows remain). Documented as a quirk.
- Surfacing per-root failures in Hangfire/UI.
- Handling 410 mid-pagination of a fresh enumeration.

## Open Questions
None.

## Status: COMPLETE
