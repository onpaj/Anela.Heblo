# Specification: Eliminate 409 Conflict Noise from `PUT stheblo` Blob Container Creation

## Summary
Application Insights reports the external dependency `PUT stheblo` (Azure Blob storage account `stheblo.blob.core.windows.net`) failing 27 times in 24h, exceeding the failure threshold of 5. Root cause is `AzureBlobPrintQueueSink.SendAsync` invoking `CreateIfNotExistsAsync` on every send — Azure returns HTTP 409 when the container already exists, and although the SDK swallows the exception, App Insights records it as a failed dependency. This work removes the noise by ensuring container existence is verified at most once per process lifetime, matching the pattern already in place for `AzureBlobStorageService`.

## Background
- The `stheblo` storage account hosts two distinct use cases:
  1. **General file storage** via `AzureBlobStorageService` (`backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`). This service was already fixed under issue #505 with a `ConcurrentDictionary<string,bool> _containerExists` cache (lines 17, 232–241). It is registered as Singleton in `FileStorageModule.cs:56`, so the cache survives across requests.
  2. **Expedition list print archive** via `AzureBlobPrintQueueSink` (`backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs`). This sink still calls `_containerClient.CreateIfNotExistsAsync` unconditionally on every `SendAsync` (line 29). It is registered as **Scoped** (`AzureAdapterModule.cs:24`), while its dependency `BlobContainerClient` is **Singleton** (`AzureAdapterModule.cs:18–22`).
- An HTTP 409 from `PUT https://stheblo.blob.core.windows.net/{container}?restype=container` is the documented response when the container already exists. The Azure SDK distinguishes "exists" from "error" internally, but the dependency telemetry is captured at the HTTP layer and therefore counts as a failure.
- The expedition list print queue is the only remaining call site issuing repeated container creates against `stheblo`. In production the target container (`expedition-lists` or `expedition-lists-stg`) is created once and persists; the per-send PUT serves no useful purpose after the first successful invocation.
- The volume (27/day) is consistent with one container-creation PUT per expedition list print operation.

## Functional Requirements

### FR-1: Ensure container existence at most once per process lifetime
`AzureBlobPrintQueueSink.SendAsync` must not issue `CreateIfNotExistsAsync` on every invocation. Container existence verification must happen at most once per `BlobContainerClient` instance for the lifetime of the application process.

**Acceptance criteria:**
- Calling `SendAsync` N times (N ≥ 2) with one or more files per call results in exactly **one** invocation of `BlobContainerClient.CreateIfNotExistsAsync` across the test, regardless of how many `AzureBlobPrintQueueSink` instances are created (because `BlobContainerClient` is a process-wide singleton).
- The first call still creates the container if it does not exist (no regression for fresh environments / new container names).
- If the first `CreateIfNotExistsAsync` throws (non-409 error, e.g. auth failure, network issue), the flag is **not** set, so a retry on the next call is still possible.

### FR-2: Preserve existing send behavior
The fix must not change observable upload behavior.

**Acceptance criteria:**
- Blob naming pattern `yyyy-MM-dd/{originalFileName}` is preserved.
- `overwrite: true` semantics on `UploadAsync` are preserved.
- Empty `filePaths` continues to short-circuit before any blob or container API call (no `CreateIfNotExistsAsync`, no `UploadAsync`).
- Files with `string.IsNullOrEmpty(fileName)` continue to log a warning and be skipped (no upload, no throw).
- Logging output (Info / Debug / Warning) is unchanged in level and structure for the happy path.

### FR-3: Thread-safe caching
Multiple concurrent `SendAsync` calls (possible because the sink is Scoped but shares the singleton `BlobContainerClient`) must not race to issue duplicate `CreateIfNotExistsAsync` calls.

**Acceptance criteria:**
- Concurrent first-time `SendAsync` calls from N parallel tasks result in `CreateIfNotExistsAsync` being awaited at most once (acceptable: exactly once; not acceptable: N times).
- No deadlocks under concurrent first-call contention.

### FR-4: Telemetry validation in lower environments
After deployment to Staging, the App Insights dependency count for `PUT stheblo` must drop to one per process restart for the print queue container.

**Acceptance criteria:**
- Manual verification step in PR description: trigger ≥ 3 expedition list print operations against Staging and confirm only the first generates a `PUT {container}?restype=container` dependency entry.

## Non-Functional Requirements

### NFR-1: Performance
- Cached path adds no Azure SDK round-trips. The cache lookup must be O(1) and lock-free for the steady-state (already-cached) case.
- No measurable latency regression on `SendAsync` for files already destined to the cached container.

### NFR-2: Reliability
- A transient failure on the initial `CreateIfNotExistsAsync` must not permanently disable container creation. The caching mechanism must record success only after the call completes without throwing.
- The fix must not introduce new failure modes for the upload path (e.g. NullReferenceException, unhandled exceptions in cache initialization).

### NFR-3: Observability
- No new logging at Info or higher levels for the steady-state cached path (avoid log spam — there are tens of expedition list prints per day).
- Optionally, log at Debug when the initial `CreateIfNotExistsAsync` is invoked, to allow operators to correlate dependency entries to startup behavior.

### NFR-4: Security
- No change to auth model. Uses existing `BlobConnectionString` from `PrintPickingListOptions`. Connection string remains sourced from configuration (`ExpeditionList:BlobConnectionString`), never hardcoded.
- No new secrets, no new endpoints, no change to access surface.

## Data Model
No schema changes. No persisted state.

In-memory state additions (per-process):
- A single flag tracking whether `CreateIfNotExistsAsync` has succeeded for the singleton `BlobContainerClient`. Container name is implicit (one client = one container), so the cache can be a single `bool` (or `int` for `Interlocked.CompareExchange`) rather than a dictionary.

## API / Interface Design

### Internal change (no public API surface change)
- `IPrintQueueSink.SendAsync(IEnumerable<string>, CancellationToken)` signature unchanged.
- `AzureBlobPrintQueueSink` implementation changes:
  - Add a process-wide latch (`static int _containerEnsured` with `Interlocked.CompareExchange`, **or** a singleton helper service injected into the scoped sink, **or** move the flag to a singleton wrapper around `BlobContainerClient`). The choice is left to the architect; the chosen approach must satisfy FR-1 and FR-3.
  - Wrap `CreateIfNotExistsAsync` in logic that runs it only on the first call per process and only sets the latch after the call returns successfully (FR-3).
- DI registrations in `AzureAdapterModule` remain unchanged in lifetimes (Singleton `BlobContainerClient`, Scoped `IPrintQueueSink`), unless the chosen design requires a new singleton helper.

### No external API or UI changes
- No new endpoints, no contract changes, no frontend impact.

## Dependencies
- `Azure.Storage.Blobs` SDK (already referenced).
- `Microsoft.Extensions.DependencyInjection` (for any new singleton registration).
- Existing test infrastructure: xUnit + Moq (per `AzureBlobPrintQueueSinkTests.cs`).
- No new NuGet packages required.

## Out of Scope
- Changes to `AzureBlobStorageService` — the equivalent fix is already in place (`backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs:17,232–241`).
- Provisioning the print queue container via infrastructure-as-code at deployment time (alternative solution; out of scope for this fix).
- Removing the `CreateIfNotExistsAsync` call entirely and relying on operator-provisioned containers (riskier; would break first-run in fresh environments).
- Migrating the print queue sink to use `IBlobStorageService` — refactor, not a bug fix.
- Tuning the App Insights failure threshold (5) — the threshold is correct; the underlying noise should be eliminated, not masked.
- Investigating any *other* `PUT stheblo` failures unrelated to container creation (e.g. real upload conflicts on blob writes). Brief evidence points to container-create as the source; if a different signature emerges in App Insights after this fix, it is a separate bug.

## Open Questions
None.

## Status: COMPLETE