# Specification: Resolve Azure Blob Storage 409 Conflict Failures

## Summary
Eliminate the 6.9% HTTP 409 Conflict failure rate observed on the `stheblo.blob.core.windows.net` dependency by identifying the specific blob operation path that produces concurrent-write conflicts and adding idempotent write semantics (existence guard or graceful 409 handling). The work proceeds in two phases: a diagnostic phase that pinpoints the failing operation via App Insights, and a remediation phase that hardens the offending write path.

## Background
Application Insights telemetry for the window 2026-06-05 to 2026-06-12 shows 16 of 231 Azure Blob calls returning HTTP 409 Conflict (6.9%). InProc `BlobClient.Upload` operations show 0 failures across 160 calls, so the 409s originate from a different blob operation path — likely a conditional create, metadata update, or finalise step that lacks an idempotency guard.

The most probable origin is the Photobank module (thumbnail uploads) or another file-storage workflow where concurrent requests for the same resource attempt to write the same blob without a distributed lock, existence check, or `overwrite: true` semantics. A 6.9% failure rate is high enough to surface as user-visible errors and to pollute telemetry, so the issue warrants a focused remediation rather than waiting for a broader storage refactor.

## Functional Requirements

### FR-1: Identify the exact blob operation producing 409s
Run an App Insights query against the `dependencies` table scoped to `type == "Azure blob"` and `resultCode == "409"` and retrieve the `name`, `operation_Name`, `operation_Id`, `cloud_RoleName`, and `customDimensions` fields for each failed call. Cross-reference `operation_Name` to the originating controller, MediatR handler, or background job.

**Acceptance criteria:**
- A query result identifies the specific Azure SDK method (e.g. `BlobClient.UploadAsync`, `BlobClient.CreateAsync`, `BlobContainerClient.CreateAsync`) responsible for ≥80% of the 16 observed 409s.
- The originating code path (file + method + handler/controller) is documented in the implementation PR.
- If multiple operations contribute, each is listed with its share of the 409 volume.

### FR-2: Add idempotent write semantics to the identified path
Once the failing operation is identified, modify the code so concurrent writes of the same blob no longer produce a 409 that propagates as an error. The chosen strategy depends on the operation:

- **For blob creation (no existing content expected):** treat 409 as a benign no-op when the existing blob's content matches; surface as an error only when content differs.
- **For blob upload of derived/deterministic content (e.g. thumbnails):** call `UploadAsync` with `overwrite: true`, OR perform an existence check and skip when the blob already exists, OR catch `RequestFailedException` with `Status == 409` and log-and-continue when the operation is idempotent.
- **For leased blobs:** ensure the active lease token is supplied on every write/delete.
- **For container-level conflicts:** treat "container already exists" as success on `CreateIfNotExistsAsync` semantics.

The chosen strategy must be documented inline (one-line comment naming the invariant being relied on) and covered by a unit test.

**Acceptance criteria:**
- A unit test simulates the concurrent-write scenario (two writers, same key) and asserts no exception is propagated to the caller.
- A unit test asserts that a genuine 409 from a non-idempotent path (e.g. different content) still surfaces as an error — graceful handling does not mask real conflicts.
- The Azure SDK call uses explicit conflict-handling semantics (not a blanket `catch (Exception)`).

### FR-3: Verify failure rate drops post-deployment
After deployment, the 409 failure rate on `stheblo.blob.core.windows.net` must drop to ≤0.5% (effectively zero, allowing for genuine conflicts on non-idempotent paths) over a rolling 7-day window.

**Acceptance criteria:**
- App Insights query `dependencies | where type == "Azure blob" and resultCode == "409" | where timestamp > ago(7d) | count` returns a value consistent with ≤0.5% of total blob calls in the same window, measured 7 days after deployment.
- No regression in successful-call latency (p50 ≤ 50ms, p95 ≤ 300ms, p99 ≤ 600ms — matching or improving the current baseline of p50 26ms, p95 205ms, p99 446ms).

### FR-4: Preserve observability for genuine conflicts
The remediation must not silently swallow conflicts on non-idempotent paths. Where 409 is treated as success, the handler must emit a structured log entry (info or debug level) noting "idempotent 409 suppressed" with the blob name and operation, so future regressions are diagnosable.

**Acceptance criteria:**
- Log statements exist on every code path that suppresses a 409.
- Log messages include `blobName`, `containerName`, and `operationName` fields.

## Non-Functional Requirements

### NFR-1: Performance
- No measurable regression in p50/p95/p99 latency for blob operations after remediation.
- Existence checks (where used) must use `BlobClient.ExistsAsync` rather than fetching content; the additional HEAD call is acceptable but should not be added on hot paths where `overwrite: true` is equally valid.

### NFR-2: Security
- No change to authentication, authorization, or blob ACLs.
- Suppressed 409s must not be exposed to end users as success unless the operation is genuinely idempotent (i.e. the resulting state matches the intended state).

### NFR-3: Observability
- The dependency telemetry sampling rate for `Azure blob` calls must remain unchanged so future 409 spikes are detectable.
- Logging added for suppressed 409s must respect the existing log level configuration; no PII or secret material in log fields.

### NFR-4: Backwards compatibility
- Public API contracts (HTTP endpoints, controller signatures, MediatR command/query shapes) must not change.
- Existing callers of any modified internal storage method must continue to work without code changes.

## Data Model
No schema changes are required. The work touches the blob storage layer only:
- **Blob container:** identified by the diagnostic step (likely the Photobank thumbnail container, but to be confirmed).
- **Blob naming convention:** unchanged; the remediation assumes deterministic blob names so that "same name = same content" holds for idempotent paths.

## API / Interface Design
No public API or UI changes. The remediation is internal to the storage-access code path identified in FR-1.

If the identified path is exposed through a MediatR handler or controller, its signature and response shape remain unchanged — the 409 is simply no longer surfaced as an exception.

## Dependencies
- **Azure.Storage.Blobs** SDK — already in use; no version change required unless a specific method (`ExistsAsync`, conditional upload) is unavailable in the current version.
- **Microsoft.ApplicationInsights** — used for the diagnostic query; no code dependency change.
- **Azure Application Insights** — operator access required to run the diagnostic query in FR-1.

## Out of Scope
- Broader refactor of the Photobank or file-storage modules.
- Introduction of a distributed lock (e.g. Redis-based) for blob writes — idempotency is the preferred pattern.
- Migration to a different storage backend.
- Retroactive cleanup of any orphaned blobs created during prior conflicts.
- Changes to dependency sampling, retention, or alerting rules in App Insights beyond what FR-3 verification requires.
- Addressing any other dependency failures unrelated to the 409 Conflict signal.

## Open Questions
None.

## Status: COMPLETE