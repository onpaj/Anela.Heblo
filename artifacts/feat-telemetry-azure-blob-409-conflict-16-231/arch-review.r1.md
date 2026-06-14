I have enough context to write the architecture review.

```markdown
# Architecture Review: Resolve Azure Blob Storage 409 Conflict Failures

## Skip Design: true

This is a backend telemetry/observability remediation with zero UI surface area — no new screens, components, or visual decisions are required.

## Architectural Fit Assessment

The codebase has **two distinct Azure Blob write paths**, both of which must be considered when identifying the 409 source:

1. **`Anela.Heblo.Application.Features.FileStorage.Services.AzureBlobStorageService`** — implements `IBlobStorageService`. Used by `FileStorage`, `ExpeditionListArchive`, `KnowledgeBase`, `Catalog`, and `Invoices`. Registered as a singleton with a `ConcurrentDictionary<string,bool> _containerExists` cache that guards `CreateIfNotExistsAsync` once per process per container (`AzureBlobStorageService.cs:271-280`). Uses an **unconditional PUT** (`Conditions == null`) on upload (`AzureBlobStorageService.cs:91-94`) — intentionally chosen so re-uploads do not 409 (tests at `AzureBlobStorageServiceTests.cs:262-304` explicitly verify this).
2. **`Anela.Heblo.Adapters.Azure.Features.ExpeditionList.AzureBlobPrintQueueSink`** — direct `BlobContainerClient` consumer for printer-queue sink. Uses `SemaphoreSlim`-gated `CreateIfNotExistsAsync` once per process (`AzureBlobPrintQueueSink.cs:54-71`) and `UploadAsync(..., overwrite: true)` (`AzureBlobPrintQueueSink.cs:46`).

Both upload paths already use overwrite semantics, which matches the brief's observation that `InProc BlobClient.Upload` shows 0 failures across 160 calls. The 409s therefore almost certainly originate from **`BlobContainerClient.CreateIfNotExistsAsync`**: the Azure SDK issues an unconditional `PUT container` REST call that returns HTTP 409 `ContainerAlreadyExists` when the container exists, swallows the exception in user code, but Application Insights still records the underlying dependency call as a 409 failure. The 16/231 (≈6.9%) ratio is consistent with "one container-existence probe per (process, container) × deployment count over 7 days" against ongoing upload/list/download volume.

Observability strategy is documented in `docs/architecture/observability.md`. Two telemetry processors already exist (`CostOptimizedTelemetryProcessor`, `CustomSamplingTelemetryProcessor`) — extending the processor pipeline is an established pattern.

## Proposed Architecture

The work splits cleanly along the spec's two phases. Phase 1 is a diagnostic deliverable (no code); Phase 2 is the remediation.

### Component Overview

```
┌─────────────────────────────────────────────────────────────────┐
│ Phase 1 — Diagnostic (no code; deliverable is documentation)    │
│ ─────────────────────────────────────────────────────────────── │
│ App Insights KQL → dependencies | type=="Azure blob"            │
│                  | resultCode=="409"                            │
│                  → name, target, operation_Name, cloud_RoleName │
│ Output: confirmation that source is CreateIfNotExistsAsync      │
│         (or, if not, the specific code path identified)         │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ Phase 2 — Remediation (primary path: CreateIfNotExistsAsync)    │
│ ─────────────────────────────────────────────────────────────── │
│                                                                 │
│  AzureBlobStorageService    AzureBlobPrintQueueSink             │
│        │                            │                           │
│        └─── EnsureContainerExistsAsync ────┐                    │
│             (new shared helper or inlined) │                    │
│                                            ▼                    │
│                         BlobContainerClient.ExistsAsync()       │
│                                  │                              │
│                          exists? │ no → CreateAsync             │
│                                  │ yes → no-op (no 409)         │
│                                                                 │
│  + Idempotent409TelemetryProcessor (defensive backstop)         │
│    Marks 409 from BlobContainerClient PUT container as Success  │
│    so future restart cycles do not pollute telemetry            │
└─────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Use `ExistsAsync` + conditional `CreateAsync` instead of `CreateIfNotExistsAsync`

**Options considered:**
- **A. Pre-provision containers via Bicep/IaC, remove all `CreateIfNotExistsAsync` calls.** Cleanest long-term but expands scope beyond the spec — containers are currently created lazily by app code, no IaC manages them. Defer.
- **B. Replace `CreateIfNotExistsAsync` with `ExistsAsync` + `CreateAsync` (only if missing).** One HEAD call when container exists (no 409), one HEAD + CreateAsync when missing. App Insights records a 200 (or 404 → 201), no 409 noise.
- **C. Keep `CreateIfNotExistsAsync` but filter 409s in a telemetry processor.** Masks the symptom without fixing the underlying call shape; future readers will be misled by suppressed dependencies.
- **D. Catch `RequestFailedException` with `Status == 409` and log-and-continue.** Already what the SDK does internally — does not help.

**Chosen approach:** **B**, augmented with **C** as a defensive backstop for any code path still hitting the failing operation.

**Rationale:** B fixes the root cause: the SDK's `CreateIfNotExistsAsync` always issues a `PUT container` that returns 409 when the container exists, which AI records as a failed dependency regardless of whether user code sees the exception. Switching to `ExistsAsync` (HEAD container) turns the steady-state into a 200, eliminating the 409 telemetry signal. C as a backstop ensures any code path the diagnostic missed cannot continue polluting telemetry. Tests already explicitly validate the unconditional-PUT upload semantics (`AzureBlobStorageServiceTests.cs:253-260`), so the contract that "blobs overwrite freely" is preserved.

#### Decision 2: Share the container-existence helper across `AzureBlobStorageService` and `AzureBlobPrintQueueSink`

**Options considered:**
- **A. Inline the fix in both classes.** Simple but duplicates the `Exists → Create` logic in two places, drifting over time.
- **B. Extract an internal static helper `BlobContainerClientExtensions.EnsureExistsAsync(BlobContainerClient, ConcurrentDictionary<string,bool>, CancellationToken)` in `Anela.Heblo.Application/Features/FileStorage/Infrastructure/`.**
- **C. Move `AzureBlobPrintQueueSink` onto `IBlobStorageService`.** Out of scope — sink lives in the Azure adapter for a reason (different DI lifecycle, container client is pre-resolved from options).

**Chosen approach:** **B** — one helper, two consumers.

**Rationale:** The two classes already implement near-identical "ensure-once-per-process" logic with subtly different patterns (`ConcurrentDictionary` vs `SemaphoreSlim`+`bool`). A single helper canonicalises the existence-check semantics and ensures both paths benefit from the same fix and the same test coverage. Place it in `Anela.Heblo.Application/Features/FileStorage/Infrastructure/` alongside `IDownloadResilienceService` and `FileDownloadOptions` — same module, same access tier.

#### Decision 3: Add a `BlobIdempotent409TelemetryProcessor` as defensive backstop

**Options considered:**
- **A. No processor — rely on Decision 1 alone.** Risk: if a code path was missed in diagnosis, 409s continue.
- **B. Add a processor that re-marks `Azure blob` dependencies as `Success=true` only when `Data` matches the `PUT container` shape (URI ends with the container name and no blob path) and `ResultCode == "409"`.** Targeted, auditable.
- **C. Suppress all `Azure blob` 409s.** Hides genuine conflicts on non-idempotent paths — violates FR-4.

**Chosen approach:** **B** — narrowly scoped to the `PUT container` shape.

**Rationale:** Belt-and-braces. The processor only neutralises the specific "container-already-exists" telemetry pattern. Real blob 409s (lease collisions, conditional puts) still surface. The processor lives next to the existing `CostOptimizedTelemetryProcessor` and follows its registration pattern (`AddApplicationInsightsTelemetryProcessor<T>()`).

## Implementation Guidance

### Directory / Module Structure

**Phase 1 — diagnostic artefacts** (no code, documentation only):
- `docs/features/azure-blob-409-diagnostic.md` — KQL query, raw results table (operation_Name → count), and the conclusion identifying the offending code path. This document satisfies the FR-1 acceptance criteria ("documented in the implementation PR").

**Phase 2 — remediation code** (Application layer + Azure adapter):
```
backend/src/
├── Anela.Heblo.Application/
│   └── Features/FileStorage/
│       ├── Infrastructure/
│       │   └── BlobContainerEnsurance.cs              ← NEW shared helper (Decision 2)
│       ├── Services/
│       │   └── AzureBlobStorageService.cs             ← MODIFY GetOrCreateContainerAsync
│       └── FileStorageModule.cs                       ← no change unless processor goes here
├── Anela.Heblo.API/
│   └── Telemetry/  (existing folder pattern — confirm path during impl)
│       └── BlobIdempotent409TelemetryProcessor.cs     ← NEW processor (Decision 3)
└── Adapters/Anela.Heblo.Adapters.Azure/
    └── Features/ExpeditionList/
        └── AzureBlobPrintQueueSink.cs                 ← MODIFY EnsureContainerAsync
```

Tests follow the mirrored convention from `docs/architecture/filesystem.md`:
```
backend/test/Anela.Heblo.Tests/
├── Features/FileStorage/
│   ├── BlobContainerEnsuranceTests.cs                 ← NEW
│   └── AzureBlobStorageServiceTests.cs                ← EXTEND with concurrent-write tests
├── Features/ExpeditionList/
│   └── AzureBlobPrintQueueSinkTests.cs                ← EXTEND
└── Telemetry/
    └── BlobIdempotent409TelemetryProcessorTests.cs    ← NEW
```

### Interfaces and Contracts

**New helper** (internal to the FileStorage infrastructure namespace):

```csharp
namespace Anela.Heblo.Application.Features.FileStorage.Infrastructure;

internal static class BlobContainerEnsurance
{
    // Ensures the container exists without producing a 409 in App Insights telemetry.
    // Caller supplies a process-scoped cache so each (instance, container) pair issues
    // at most one ExistsAsync probe. A returned `false` from ExistsAsync issues CreateAsync;
    // a 409 from CreateAsync is treated as "another writer won the race" and logged-and-continued.
    public static Task EnsureExistsAsync(
        BlobContainerClient client,
        ConcurrentDictionary<string, bool> cache,
        ILogger logger,
        CancellationToken cancellationToken);
}
```

**Telemetry processor contract** (mirrors `CostOptimizedTelemetryProcessor`):

```csharp
namespace Anela.Heblo.API.Telemetry;

internal sealed class BlobIdempotent409TelemetryProcessor : ITelemetryProcessor
{
    public BlobIdempotent409TelemetryProcessor(ITelemetryProcessor next);
    public void Process(ITelemetry item); // re-marks Success=true on PUT container 409s
}
```

**No public API changes.** `IBlobStorageService`, all controller signatures, all MediatR command/query shapes remain unchanged. The fix is internal to the storage layer (NFR-4).

### Data Flow

**Steady-state upload (after first call has populated cache):**

```
Caller → IBlobStorageService.UploadAsync
       → AzureBlobStorageService.GetOrCreateContainerAsync
       → BlobContainerEnsurance.EnsureExistsAsync
         (cache hit → no-op)
       → BlobClient.UploadAsync (unconditional PUT, overwrite)
       → return blob URL
```

**First call per (process, container):**

```
Caller → IBlobStorageService.UploadAsync
       → AzureBlobStorageService.GetOrCreateContainerAsync
       → BlobContainerEnsurance.EnsureExistsAsync
         → BlobContainerClient.ExistsAsync       (HTTP 200 — telemetry success)
         → exists == true  → cache[name] = true; return
                            (no CreateAsync, no 409)
         OR
         → exists == false → BlobContainerClient.CreateAsync (HTTP 201)
                            cache[name] = true
                            (if a competing writer beat us: 409 caught, logged as
                             "idempotent 409 suppressed", cache populated, continue)
       → BlobClient.UploadAsync
       → return blob URL
```

**Genuine 409 on a non-idempotent path (preserved):**

```
Caller → conditional upload with Conditions.IfNoneMatch = ETag.All
       → BlobClient.UploadAsync → RequestFailedException(Status=409)
       → propagates to caller (NOT caught by EnsureExistsAsync — that helper
         only runs on PUT container, not on PUT blob)
       → App Insights still records the 409 dependency call
         (telemetry processor only filters PUT container, not PUT blob)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Diagnostic identifies an operation outside the two known paths (e.g. metadata update, lease conflict, container delete) | Medium | Spec already covers this — FR-1 lists the strategies by operation type. Architecture allows extending `BlobContainerEnsurance` or adding an analogous helper without touching public contracts. |
| Telemetry processor masks a real ContainerBeingDeleted state | Low | Processor matches only on `PUT container` request shape returning 409 ContainerAlreadyExists. If we observe `ContainerBeingDeleted` (different error code, same HTTP 409), it would be a separate alert; document this in the processor's XML doc. |
| `ExistsAsync` adds an extra HEAD call on hot paths | Low | The cache ensures one HEAD per (process, container). Steady-state cost is zero (cache hit). NFR-1 explicitly allows the HEAD probe. |
| Race condition: two pods start, both probe `ExistsAsync`, container missing, both call `CreateAsync` → one 409 | Low | Helper catches the 409 from `CreateAsync` (genuine race), logs once at info level with `blobName=null, containerName=X, operationName=CreateContainer`, populates cache, and returns. Net effect: at most one telemetry-recorded 409 per cold deployment, which the telemetry processor then neutralises. |
| 7-day verification window (FR-3) overlaps deployment churn from this very change | Low | Schedule the FR-3 verification 7 days after the merge merges to staging, not from PR open. Document the start time in the PR description. |
| Diagnostic phase requires App Insights operator access | Low (operational) | Solo developer has access. Capture the raw KQL query and date range in `docs/features/azure-blob-409-diagnostic.md` so the run is reproducible. |
| `_containerExists` cache currently uses `TryAdd` then runs `CreateIfNotExistsAsync` outside the lock — concurrent first-callers race; same race exists in the new helper unless we use a `Lazy<Task>` per container | Medium | Use `ConcurrentDictionary<string, Lazy<Task>>` keyed by container name with `GetOrAdd`, so all callers await the same in-flight check. This eliminates the existing race in `AzureBlobStorageService.cs:271-280` as a side benefit. |

## Specification Amendments

The spec is largely sound. Two refinements:

1. **Add an architectural note that the most likely source is `CreateIfNotExistsAsync` (PUT container)**, supported by the InProc Upload 0-failure observation. The diagnostic phase remains required to confirm, but the proposed remediation (`BlobContainerEnsurance` helper) should be the default plan unless the diagnostic surfaces a different operation. This sets developer expectations without prejudging the diagnostic.

2. **Extend FR-2's acceptance criteria** with a concurrency-race assertion: "A unit test asserts that two concurrent callers to the helper for the same container result in **at most one** `CreateAsync` call (verified with `Moq.Verify(..., Times.AtMostOnce())`)". This locks in the `Lazy<Task>` pattern called out in the risk table and prevents the existing `TryAdd` race from re-emerging.

3. **FR-3 verification clock**: clarify that the 7-day window starts from production deployment, not PR open. Add to acceptance: "The KQL query is re-run at deployment + 7d; the count and percentage are recorded in the PR comment thread (or follow-up issue)".

## Prerequisites

None. All required pieces exist:
- `BlobServiceClient` and `BlobContainerClient` are already singletons.
- `IBlobStorageService` and `ExistsAsync` are already part of the contract (`IBlobStorageService.cs:42`).
- The telemetry-processor pipeline is already wired up (`docs/architecture/observability.md:382-388`).
- Test infrastructure (`Moq`, `xUnit`) already covers `BlobContainerClient` mocking patterns (see `AzureBlobStorageServiceTests.cs:443-524`).

The only operational prerequisite is **access to Application Insights** to run the Phase 1 KQL query — already available to the solo developer.
```