# Architecture Review: Eliminate Azure Blob 409 Conflict Failures

## Skip Design: true

Backend-only telemetry/reliability work. No UI components, screens, or visual design decisions are introduced.

## Architectural Fit Assessment

**The dominant fix is already in the codebase.** Two relevant components already exist on this branch:

1. `Anela.Heblo.API/Infrastructure/Telemetry/BlobIdempotent409TelemetryProcessor.cs` — neutralises 409s on PUT container shapes, leaves blob-level 409s alone.
2. `Anela.Heblo.Application/Features/FileStorage/Infrastructure/BlobContainerEnsurance.cs` — caches existence probes, swallows benign container-create races, evicts on failure. Used by both `AzureBlobStorageService` and `AzureBlobPrintQueueSink`.

Every current blob-write call site already either uses `overwrite: true` (no 409 possible) or goes through `BlobContainerEnsurance` (409 handled). The Photobank hypothesis in `brief.md` is **incorrect**: Photobank thumbnails are served by `PhotobankGraphService` (Microsoft Graph), not Azure Blob — there is no Photobank blob-write path to fix.

This means the spec's headline ask — "make blob writes idempotent against concurrent writers" — is largely satisfied for the population of call sites that actually exists. The remaining gaps are **verification and operational hardening**, not new write-path code. FR-4's `IIdempotentBlobUploader` is a solution looking for a problem until FR-1 is re-run and produces evidence of a hot blob-level (not container-level) 409 source.

Integration points: Application Insights pipeline (`AddOptimizedApplicationInsights`), `IBlobStorageService` callers, the Adapters.Azure `AzureBlobPrintQueueSink`, and the Azure infra script (`scripts/create-azure-infrastructure.sh`) for the alert rule.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│ App Insights resource (aiHeblo / aiHeblo-test)                       │
│                                                                       │
│   ┌──────────────────┐    ┌──────────────────────┐    ┌──────────┐  │
│   │ DependencyTrack  │ →  │ BlobIdempotent409    │ →  │ rest of  │  │
│   │ (Azure SDK)      │    │ TelemetryProcessor   │    │ chain    │  │
│   └──────────────────┘    │ (re-marks PUT-       │    └──────────┘  │
│                            │  container 409→OK)  │                   │
│                            └──────────────────────┘                   │
└──────────────────────────────────────────────────────────────────────┘
              ▲
              │ dependency emissions
              │
┌─────────────┴────────────────────────────────────────────────────────┐
│ Backend (Anela.Heblo)                                                │
│                                                                       │
│   AzureBlobStorageService ──┐                                        │
│   AzureBlobPrintQueueSink ──┼──► BlobContainerEnsurance              │
│   (any future blob writer) ─┘    (Exists → Create, swallow 409)      │
│                                       │                              │
│                                       └──► BlobContainerClient       │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────┐
│ Azure Monitor                                                         │
│   ALERT: dependencies | type=="Azure blob" && success==false         │
│           > N per hour → notification channel                        │
└──────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Evidence-gate FR-4 (`IIdempotentBlobUploader`)
**Options considered:**
- A. Build the helper, the analyzer rule, and migrate one call site as spec'd.
- B. Re-run FR-1 first; only build the helper if a blob-level 409 source is found.

**Chosen approach:** B — make FR-1 a hard prerequisite for FR-4.

**Rationale:** All current blob-write call sites use `overwrite: true` (unconditional PUT, 409-free) or `BlobContainerEnsurance` (handled). Introducing `IIdempotentBlobUploader` and an architecture test that bans `BlobClient.UploadAsync` would add abstraction without consumers, violating YAGNI. Mismatches between the brief's "Photobank thumbnails" hypothesis and the actual codebase (no Photobank blob writes) confirm we must measure before building. **Spec amendment required — see below.**

#### Decision 2: Suppression via telemetry processor, not custom dependency wrapping
**Options considered:**
- A. Existing `BlobIdempotent409TelemetryProcessor` — pattern-matches on `Data` URI shape.
- B. `AsyncLocal<bool>` flag set inside the helper (as the spec suggests for FR-3).

**Chosen approach:** A — keep the URI-shape processor, do not introduce `AsyncLocal` flags.

**Rationale:** The URI-shape match is local, observable, testable, and decouples telemetry filtering from call-site state. `AsyncLocal` ambient state is fragile under `Task.Run` boundaries, harder to reason about under DI lifetimes (the processor is singleton), and serves a hypothetical second helper that may never exist (Decision 1). If FR-1 surfaces a blob-level 409 that legitimately needs suppression, extend the processor with one more matcher rather than introducing ambient state.

#### Decision 3: Alert wiring via existing infra script
**Options considered:**
- A. Configure the alert by hand in Azure Portal.
- B. Add the alert rule to `scripts/create-azure-infrastructure.sh` (same script that already provisions `aiHeblo` / `aiHeblo-test`).

**Chosen approach:** B.

**Rationale:** Per `docs/architecture/observability.md`, App Insights resources are provisioned by `create-azure-infrastructure.sh`. Drift between staging and production is the documented failure mode the script exists to prevent. A portal-only alert is invisible to code review and disappears on resource rebuild.

#### Decision 4: Helper placement (forward-looking — only if Decision 1 flips)
**Chosen approach:** `Anela.Heblo.Application/Features/FileStorage/Infrastructure/IdempotentBlobUploader.cs`, mirroring `BlobContainerEnsurance`.

**Rationale:** Matches the existing pattern. `FileStorage` already owns the blob abstraction (`IBlobStorageService`); a deeper home (e.g. `Anela.Heblo.Xcc`) would imply cross-module reuse that does not exist.

## Implementation Guidance

### Directory / Module Structure

No new files are required by the **default** execution path (Decision 1 = B). The work is:

- **Edit** `docs/features/azure-blob-409-diagnostic.md` — fill in the "Raw results" table and "Conclusion" sections from the KQL output.
- **Edit** `scripts/create-azure-infrastructure.sh` — add the Azure Monitor alert rule (FR-3).
- **Edit** `docs/architecture/observability.md` — record the alert under "Critical Alerts".

If — and only if — FR-1 reveals a non-container 409 source, then add:
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/IdempotentBlobUploader.cs`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/IdempotentBlobUploaderTests.cs`
- A new matcher branch (or a parallel processor) extending `BlobIdempotent409TelemetryProcessor`.
- One migrated call site.
- An architecture test (under `backend/test/Anela.Heblo.Tests/Architecture/`, following the `ModuleBoundariesTests` reflection pattern) banning direct `BlobClient.UploadAsync(..., overwrite: false, ...)` calls outside the helper.

### Interfaces and Contracts

If FR-4 is built (conditional on FR-1):

```csharp
namespace Anela.Heblo.Application.Features.FileStorage.Infrastructure;

public interface IIdempotentBlobUploader
{
    Task<BlobUploadOutcome> UploadIfAbsentAsync(
        BlobClient blob,
        Stream content,
        BlobHttpHeaders? headers = null,
        CancellationToken cancellationToken = default);
}

public enum BlobUploadOutcome { Uploaded, AlreadyExisted }
```

Implementation calls `blob.UploadAsync(content, new BlobUploadOptions { HttpHeaders = headers }, cancellationToken)` with no `Conditions` (a separate API call sets up `IfNoneMatch = ETag.All` only when needed). Catches `RequestFailedException` with `Status == 409` and `ErrorCode is "BlobAlreadyExists" or "ContainerAlreadyExists"`, returns `AlreadyExisted`, emits an `Information`-level log entry per NFR-3. Re-throws everything else.

The processor must continue to recognise PUT-container 409s by URI shape (existing behaviour). If a blob-level 409 must be suppressed, add a second URI-shape branch that matches the specific operation path identified by FR-1 — **do not** broaden the processor to suppress all blob-level 409s, because that would mask lease violations and conditional-PUT failures (the very signal we want to keep, per the spec and the processor's existing XML doc).

No public API changes. No DI lifetime changes (helper is stateless, register as singleton).

### Data Flow

**Write path (existing, unchanged):**

```
Caller → AzureBlobStorageService.UploadAsync (or AzureBlobPrintQueueSink.SendAsync)
       → BlobContainerEnsurance.EnsureExistsAsync
           → ExistsAsync (HEAD, never 409)
           → CreateAsync only if missing; 409 swallowed (cache marked ok)
       → blobClient.UploadAsync(stream, overwrite: true, ...)
       → returns URL
```

**Telemetry path (existing, unchanged):**

```
Azure SDK emits DependencyTelemetry (Type="Azure blob", ResultCode="409", Data=URI)
       → BlobIdempotent409TelemetryProcessor inspects Data URI shape
       → If PUT-container shape: Success=true, forward
       → Else: forward unchanged (genuine 409 surfaces as failure)
       → CostOptimizedTelemetryProcessor (must run after, registration order in
         AddOptimizedApplicationInsights is correct today — preserve it)
```

**Verification path (new):**

```
Run KQL from docs/features/azure-blob-409-diagnostic.md against aiHeblo-test, then aiHeblo
       → Record per-operation / per-container 409 counts
       → Cross-reference cloud_RoleName and operation_Name to the originating C# call site
       → If dominant cause = PUT container: existing fix sufficient; close FR-2 / FR-4 as
         "already covered by BlobContainerEnsurance, no migration needed"
       → If dominant cause = blob-level (e.g. CreateIfNotExists on blob, conditional PUT,
         lease op): build IIdempotentBlobUploader, migrate the offending call site, extend
         the telemetry processor's matcher, and add the architecture test.
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| FR-1 query returns zero 409s in the current window (because the existing fix already shipped to one or both environments) | Medium | Widen window to 30d and split by `cloud_RoleName` to separate pre-fix vs post-fix periods. Document the deployment timestamp in the diagnostic doc before relying on the window. |
| FR-4 helper is built without a real consumer because the spec is followed literally | High | Adopt Decision 1: FR-1 is a hard gate for FR-4. State the gate explicitly in the spec amendment so an implementer cannot skip it. |
| Telemetry processor over-suppresses genuine 409s (lease violations, conditional PUTs) | High | Preserve URI-shape narrowness: only one-segment paths are container PUTs. Existing test `Process_AzureBlob409OnPutBlob_DoesNotMarkSuccess` covers regression — keep it green for any matcher extension. |
| Processor registration order regresses (`CostOptimized` runs before `BlobIdempotent409`, marking failed-409 deps as expensive failures) | Medium | The comment in `AddOptimizedApplicationInsights` already documents the ordering constraint. Add an integration test that resolves the configured chain and asserts `BlobIdempotent409TelemetryProcessor` precedes `CostOptimizedTelemetryProcessor`. |
| Alert fires on benign 409s after a future Azure SDK change emits a new URI shape | Medium | Alert query filters on `success == false` (post-processor), so re-marking handles this. Re-run FR-1 quarterly (track in `memory/gotchas/` or as a recurring routine in `docs/routines/`). |
| Performance regression from `ExistsAsync` HEAD probe on cold containers | Low | `BlobContainerEnsurance` caches the result per container per process — first call is HEAD+PUT, subsequent are no-ops. Existing tests `EnsureExistsAsync_CalledTwice_ProbesExistenceOnlyOnce` and `_FourParallelFirstCalls_ProbesExistenceAtMostOnce` cover the caching guarantee. NFR-1 budget (≤50ms p95) is preserved. |
| Alert noise during the FR-1 measurement window itself (before the fix is verified) | Low | Set the alert threshold conservatively (default: 10 failures/hour) and tag as Warning, not Critical, until the post-deployment verification at FR-3 confirms ≤0.5%. Tighten thereafter. |

## Specification Amendments

1. **FR-1 must complete before FR-4.** Add to FR-4: "If FR-1 finds that ≥80% of 409s originate from PUT-container operations, the `IIdempotentBlobUploader` helper and its architecture test are deferred — the existing `BlobContainerEnsurance` helper satisfies the requirement. Record the FR-1 outcome and the decision in `docs/features/azure-blob-409-diagnostic.md` under the Conclusion section."

2. **Remove the Photobank hypothesis from FR-2.** Photobank does not write to Azure Blob (it uses Microsoft Graph). Replace with: "Affected modules will be determined by FR-1 outputs. The current candidates are `Anela.Heblo.Application.Features.FileStorage.Services.AzureBlobStorageService` and `Anela.Heblo.Adapters.Azure.Features.ExpeditionList.AzureBlobPrintQueueSink`."

3. **Reject the `AsyncLocal<bool>` design in the FR-3 telemetry processor section.** Replace with: "Suppression is decided by `DependencyTelemetry.Data` URI shape inside `BlobIdempotent409TelemetryProcessor`. Do not introduce ambient state across the call site and the processor."

4. **Add an explicit acceptance criterion for processor ordering** under FR-3: "An automated test resolves the configured telemetry processor chain and asserts that `BlobIdempotent409TelemetryProcessor` runs before `CostOptimizedTelemetryProcessor`."

5. **Move the alert wiring into infrastructure code** (FR-3 acceptance): "The hourly failure alert is provisioned by `scripts/create-azure-infrastructure.sh` alongside the `aiHeblo` / `aiHeblo-test` resources, not by a manual Azure Portal change."

6. **Acknowledge prior work in `Status`.** The spec currently reads `Status: COMPLETE` while the work is partially shipped. Update to `Status: VERIFICATION REQUIRED` until FR-1 is re-run and the diagnostic doc's "Raw results" / "Conclusion" / "FR-3 post-deployment verification" sections are filled in.

## Prerequisites

- **Read access to App Insights** resources `aiHeblo` (production) and `aiHeblo-test` (staging) in resource group `rgHeblo`, German West Central region. Verify the executing identity can run KQL against both.
- **Deployment timestamp of the existing `BlobIdempotent409TelemetryProcessor` + `BlobContainerEnsurance` work** to each environment — required to scope the FR-1 query window correctly (pre-fix vs post-fix). Search `git log --first-parent main -- backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/BlobContainerEnsurance.cs` and cross-reference to Azure deployment history.
- **Azure CLI authenticated** against the `rgHeblo` subscription for the alert-rule changes to `scripts/create-azure-infrastructure.sh`.
- **No infrastructure migrations or schema changes required.**
- **No new NuGet packages required** — confirmed against the existing `Azure.Storage.Blobs` and `Microsoft.ApplicationInsights.AspNetCore` references.
- **Notification channel for the alert** must already exist (`docs/architecture/observability.md` lists Teams/Email but flags alerting as "⏳ Not configured" — confirm the channel is provisioned before the alert rule references it, otherwise add channel creation as a prior step).