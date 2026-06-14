# Specification: Eliminate Azure Blob 409 Conflict Failures

## Summary
Investigate and resolve the 6.9% HTTP 409 Conflict failure rate on Azure Blob Storage (`stheblo.blob.core.windows.net`) observed over a 7-day window (16 of 231 operations). Introduce idempotent, conflict-tolerant write semantics so concurrent writers of the same blob no longer surface as application errors, and add structured telemetry to confirm the fix and detect regressions.

## Background
Application Insights telemetry shows that 16 of 231 Azure Blob dependency calls (6.9%) returned HTTP 409 Conflict over P7D (2026-06-05 – 2026-06-12). Direct `BlobClient.Upload` InProc calls (160 in the same window) report **zero** failures, so the 409s originate from a different code path — most likely a conditional create, a leased-blob operation, or a finalisation step that is not guarded against the race where two callers attempt to materialise the same blob simultaneously.

The most plausible source is the Photobank thumbnail-generation workflow, where two concurrent requests can independently compute and write the same derived asset. Without a distributed lock, an `IfNoneMatch: *` precondition, or an explicit "overwrite=false → swallow 409" pattern, the second writer surfaces a 409 as an unhandled dependency failure even though the end state (blob exists with correct content) is correct.

These failures inflate the dependency error rate, pollute alerting, and obscure genuine storage problems. They do not (so far) appear to cause user-visible bugs, but a 6.9% failure rate on a storage dependency is a latent operational risk.

## Functional Requirements

### FR-1: Identify the exact failing blob operations
Run an App Insights query to enumerate every 409 dependency record in the last 14 days, capturing the blob `name`, `operation_Name`, `cloud_RoleInstance`, and `customDimensions`. Group by `name` and `operation_Name` to identify the top failing operations and the controller/handler that originates each.

**Acceptance criteria:**
- A documented query (stored under `docs/integrations/` or `memory/gotchas/`) returns the breakdown for the analysis window.
- For each distinct `operation_Name` producing 409s, the originating C# call site (file:line) is identified and recorded in the spec follow-up notes.
- The breakdown distinguishes between (a) conditional-create races, (b) lease conflicts, and (c) container-level conflicts.

### FR-2: Make blob writes idempotent against concurrent writers
For each call site identified in FR-1, change the write semantics so that two concurrent writers producing the same content (or the same logical target) do **not** raise a 409 to the caller. The default strategy is:
- If the target content is **deterministic for a given key** (e.g. thumbnails, derived assets), treat an existing blob as a success — either skip the upload after a HEAD/exists check, or call `UploadAsync` with `overwrite: false` and catch `RequestFailedException` with `Status == 409` / `ErrorCode == "BlobAlreadyExists"` as a no-op.
- If the target content is **not deterministic** (e.g. user uploads where the latest wins), switch to `overwrite: true` or supply a unique blob name (GUID suffix / content hash).
- If the operation is **lease-bound**, ensure the active lease token is supplied; if leasing is no longer required, remove the lease.

**Acceptance criteria:**
- Every call site identified in FR-1 has an explicit strategy chosen and documented inline (one short comment per site explaining the chosen semantics).
- A unit test exists for each modified call site that simulates a 409 from the storage client and asserts the caller does not surface an error when the conflict is benign.
- An integration test (against Azurite or a staging container) exercises two concurrent writers of the same blob key and asserts both calls complete successfully with the expected blob contents.

### FR-3: Distinguish benign from genuine blob conflicts in telemetry
A 409 that the application has decided to treat as a no-op must not appear as a `Success = false` dependency record in App Insights. Genuine, unexpected 409s (e.g. lease violation on a path where we did not expect a lease) **must** continue to surface as failures.

**Acceptance criteria:**
- Benign 409s are either (a) suppressed via a `DependencyTelemetryProcessor` / `ITelemetryProcessor` that marks them `Success = true` with a `customDimensions["BlobConflictHandled"] = "true"` tag, or (b) avoided entirely by an existence check before the upload (so no 409 is emitted to telemetry in the first place).
- The dependency failure rate for `Azure blob` / `stheblo.blob.core.windows.net` returns to ≤ 0.5% over a 7-day window after deployment.
- An App Insights alert exists for `dependencies | where type == "Azure blob" and success == false | summarize count() by bin(timestamp, 1h)` firing when the hourly failure count exceeds a configured threshold (default: 10 / hour).

### FR-4: Prevent recurrence via shared helper
Introduce a single helper (e.g. `IdempotentBlobUploader` or an extension method `UploadIfAbsentAsync`) that encapsulates the "write-once, swallow benign 409" pattern. All Photobank and file-storage workflows that write deterministic blobs must use this helper rather than calling `BlobClient.UploadAsync` directly.

**Acceptance criteria:**
- The helper lives in the appropriate infrastructure module per `docs/architecture/filesystem.md`.
- A lint or architecture test (or a `dotnet format`-enforced banned API analyser, if available) flags direct `BlobClient.UploadAsync` calls in feature modules and recommends the helper.
- At least one call site (the highest-volume Photobank upload path) is migrated to the helper as part of this work.

## Non-Functional Requirements

### NFR-1: Performance
- Successful blob operations must retain current latency: p50 ≤ 30 ms, p95 ≤ 250 ms, p99 ≤ 500 ms (current baseline: 26 / 205 / 446 ms).
- The existence check (if used) must not add more than 50 ms p95 to the upload path; prefer the `overwrite: false` + catch-409 pattern over a HEAD+PUT round-trip when latency matters.
- The helper must not introduce additional round-trips for cold-path uploads (first writer for a given key).

### NFR-2: Security
- No change to authentication: continue using Managed Identity / connection string from Azure Key Vault (`kv-heblo-stg`) per project rules.
- No new public surface; all changes are internal.
- Suppressed 409 telemetry must not leak blob names that contain user-identifying tokens beyond what is already logged.

### NFR-3: Observability
- A new structured log entry (Information level) fires when a benign 409 is suppressed, with fields `{ BlobName, OperationName, CallerHandler, DurationMs }`.
- The App Insights query from FR-1 is retained as a saved query / workbook for future investigations.
- The hourly alert from FR-3 is wired into the existing notification channel.

### NFR-4: Reliability
- The helper must be safe under cancellation: a `CancellationToken` passed by the caller must propagate to the underlying SDK call.
- A benign 409 must not be retried; a transient 5xx must be retried per the existing Polly policy (if one exists) — do not introduce a new retry policy that masks 409s as retryable.

## Data Model
No persistent schema changes. Affected entities:
- **Blob (Azure Storage):** containers used by Photobank and file-storage workflows in account `stheblo`. Blob naming and container layout are unchanged.
- **Telemetry (App Insights):** `dependencies` table records gain an optional `customDimensions["BlobConflictHandled"]` flag when benign 409s are suppressed.

## API / Interface Design

### Internal helper (new)
```csharp
public interface IIdempotentBlobUploader
{
    Task<BlobUploadOutcome> UploadIfAbsentAsync(
        BlobClient blob,
        Stream content,
        BlobHttpHeaders? headers = null,
        CancellationToken cancellationToken = default);
}

public enum BlobUploadOutcome
{
    Uploaded,           // we wrote the blob
    AlreadyExisted,     // benign 409 — another writer won the race
}
```

The helper is the canonical entry point for deterministic-content writes. Implementation calls `UploadAsync(content, overwrite: false, ...)` and catches `RequestFailedException` where `Status == 409` and `ErrorCode == "BlobAlreadyExists"`, returning `AlreadyExisted`. Any other exception propagates.

### Public API
No changes to HTTP endpoints, MediatR requests, or controller signatures.

### Telemetry processor (new or extended)
A `ITelemetryProcessor` registered in DI inspects `DependencyTelemetry` items where `Type == "Azure blob"` and `ResultCode == "409"`; when an ambient `AsyncLocal<bool>` flag set by the helper indicates the 409 was handled, the processor sets `Success = true` and adds the `BlobConflictHandled` custom dimension.

## Dependencies
- **Azure.Storage.Blobs** SDK — already in use; no version bump required for this work.
- **Microsoft.ApplicationInsights** — already in use; the telemetry processor uses existing extension points.
- **Polly** (if currently used for blob retries) — must be inspected to ensure 409 is not in the retry predicate.
- **App Insights workspace** for the staging/production resource — required for FR-1 query and FR-3 alert.
- No new NuGet packages anticipated.

## Out of Scope
- Migrating the storage account, changing the container layout, or introducing a CDN in front of blob storage.
- A general-purpose distributed lock service for Photobank operations (the idempotent-write pattern is sufficient for the observed failure mode).
- Rewriting the Photobank thumbnail pipeline — only the write semantics change.
- Backfilling or replaying historical 409s.
- Lease-based concurrency control (only addressed if FR-1 reveals lease conflicts; the default fix path is idempotent writes).

## Open Questions
None.

## Status: COMPLETE