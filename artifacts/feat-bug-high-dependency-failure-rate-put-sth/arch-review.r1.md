# Architecture Review: Eliminate 409 Conflict Noise from `PUT stheblo` Blob Container Creation

## Skip Design: true

## Architectural Fit Assessment

The feature is a localized correctness fix in the Azure adapter, fully consistent with the codebase. A near-identical fix already exists in `Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs:232-241`, registered as Singleton in `FileStorageModule.cs:56`. This proposal carries that same pattern across to `AzureBlobPrintQueueSink`, with two refinements that the spec explicitly demands (NFR-2): the cache flag is set *only after* the SDK call succeeds, and concurrent first-time callers must converge on a single SDK invocation. No new module boundaries, no new abstractions, no contract changes. Integration points are limited to one class file and one DI registration line in `AzureAdapterModule`. Existing test scaffolding in `AzureBlobPrintQueueSinkTests.cs` already mocks `CreateIfNotExistsAsync` and can be extended in place.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────┐
│ Application code (PrintExpeditionListHandler etc.)   │
└──────────────────────────┬───────────────────────────┘
                           │ resolves
                           ▼
                ┌─────────────────────┐
                │ IPrintQueueSink     │   Singleton (changed)
                │ AzureBlobPrintQueueSink
                │  ─ bool _containerEnsured
                │  ─ SemaphoreSlim _ensureGate
                └──────────┬──────────┘
                           │ uses
                           ▼
                ┌─────────────────────┐
                │ BlobContainerClient │   Singleton (unchanged)
                └─────────────────────┘
```

### Key Design Decisions

#### Decision 1: Sink lifetime — Singleton vs static latch vs helper service

**Options considered:**
- (A) Promote `AzureBlobPrintQueueSink` to Singleton; cache lives as an instance field.
- (B) Keep it Scoped; store the latch in `static` state on the class.
- (C) Keep it Scoped; introduce a new singleton `IBlobContainerProvisioner` injected into the sink.

**Chosen approach:** (A) Change `IPrintQueueSink` registration from Scoped to Singleton.

**Rationale:** All current constructor dependencies are already singleton-safe (`BlobContainerClient` — Singleton; `TimeProvider` — registered to `TimeProvider.System` which is a singleton; `ILogger<T>` — always safe in singletons). The `AzureBlobStorageService` precedent uses the same approach (Singleton service with instance cache). Static state (B) leaks across xUnit test cases and complicates parallel test execution. A dedicated provisioner (C) adds a new abstraction for a single container — pure overhead under YAGNI.

#### Decision 2: Concurrency primitive — SemaphoreSlim + bool vs `ConcurrentDictionary.TryAdd`

**Options considered:**
- (A) `SemaphoreSlim(1,1)` + `bool _containerEnsured`, double-checked locking, set flag only after SDK call returns.
- (B) `ConcurrentDictionary<string,bool>` with `TryAdd` (mirrors `AzureBlobStorageService`).
- (C) `Interlocked.CompareExchange` on an `int` latch.

**Chosen approach:** (A) `SemaphoreSlim` with double-checked `bool` flag, flag set inside `try { ... }` only on success.

**Rationale:** NFR-2 explicitly forbids permanently disabling container creation after a transient failure. The existing `TryAdd` pattern in `AzureBlobStorageService` sets the dictionary entry *before* awaiting the SDK call (lines 235-238) — if that call throws, the entry stays, blocking retries. The spec calls out this exact requirement for the new code: "The caching mechanism must record success only after the call completes without throwing." `Interlocked.CompareExchange` cannot guarantee FR-3's "at most once" under concurrent first calls without losing the success-only-record property. `SemaphoreSlim` is the only primitive that satisfies all three of FR-1, FR-3, and NFR-2 cleanly. Note: the dictionary shape (B) is unnecessary because one `BlobContainerClient` instance maps to exactly one container — single `bool` suffices.

#### Decision 3: Cache shape — single `bool` vs dictionary

**Options considered:**
- (A) Single `bool _containerEnsured`.
- (B) `ConcurrentDictionary<string,bool> _containerExists` keyed on container name.

**Chosen approach:** (A) Single `bool`.

**Rationale:** `AzureBlobStorageService` operates on `BlobServiceClient` (account-level) and routes uploads to N containers chosen at call time — a dictionary is necessary. `AzureBlobPrintQueueSink` operates on a pre-bound `BlobContainerClient` (one container, set at DI time in `AzureAdapterModule.cs:18-22`). YAGNI rules out the dictionary.

## Implementation Guidance

### Directory / Module Structure

No new files. Touch only:

- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs` — add fields, refactor `SendAsync`.
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs:24` — change `AddScoped` to `AddSingleton`.
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs` — extend with new test cases (FR-1, FR-3, NFR-2).

### Interfaces and Contracts

- `IPrintQueueSink.SendAsync(IEnumerable<string>, CancellationToken)` — **signature unchanged**, behavior preserved.
- DI contract change: `IPrintQueueSink` lifetime changes from Scoped to Singleton. Any consumer that depends on per-request state in the sink would break — verified none exist (the sink has no per-request fields today and the new fields are intentionally process-wide).

Proposed internal shape (illustrative — not code in this review):
```
private readonly SemaphoreSlim _ensureGate = new(1, 1);
private bool _containerEnsured;

private async Task EnsureContainerAsync(CancellationToken ct)
{
    if (_containerEnsured) return;                    // fast path, lock-free
    await _ensureGate.WaitAsync(ct);
    try
    {
        if (_containerEnsured) return;                // double-check
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: ct);
        _containerEnsured = true;                     // only after success
    }
    finally { _ensureGate.Release(); }
}
```

### Data Flow

Steady state (cached):
1. `SendAsync(files)` called.
2. `files.Count == 0` → early return (unchanged).
3. `EnsureContainerAsync` reads `_containerEnsured == true` → returns without awaiting or acquiring the semaphore.
4. Date prefix computed; per-file `UploadAsync` loop runs (unchanged).

First call (cold):
1. `SendAsync(files)`.
2. `EnsureContainerAsync` sees `_containerEnsured == false`, acquires semaphore.
3. Re-checks flag (still false), awaits `CreateIfNotExistsAsync`.
4. On success: sets `_containerEnsured = true`, releases semaphore. Subsequent callers see fast path.
5. On failure: releases semaphore, flag remains `false`, exception propagates. Next `SendAsync` will retry the ensure step.

Concurrent first calls:
- N callers race to the semaphore. Winner runs `CreateIfNotExistsAsync` once. Losers wake up, re-check flag (now `true`), return. Exactly one SDK call.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Singleton sink later acquires a Scoped dependency (e.g. `DbContext`, `IUnitOfWork`) and triggers a captive-dependency bug. | Medium | Add an xUnit DI test that resolves `IPrintQueueSink` from a Singleton scope and asserts no scoped/transient captive. If a future scoped dependency is genuinely required, fall back to Decision 1 option (C) — introduce a singleton provisioner. |
| `TimeProvider` registered as Scoped or Transient by some future change would break the singleton sink. | Low | The codebase resolves `TimeProvider` to `TimeProvider.System` (a static singleton); no change planned. Existing test uses `TimeProvider.System` directly. |
| `SemaphoreSlim` is `IDisposable`; if the sink ever becomes disposable, the gate must be disposed. | Low | The sink is process-singleton — the semaphore lives for the process lifetime. No `IDisposable` implementation needed. |
| Transient cancellation thrown from `CreateIfNotExistsAsync` could leave `_containerEnsured == false` and cause every subsequent send to re-attempt. | Low | This is the desired behavior per NFR-2. Document the retry contract in a brief code comment on `EnsureContainerAsync`. |
| Mock-based test on the singleton sink may not observe the concurrency code path; future regressions could silently re-introduce per-call `CreateIfNotExistsAsync`. | Medium | Add explicit tests: (a) sequential N≥2 calls produce one `CreateIfNotExistsAsync` invocation; (b) parallel `Task.WhenAll` of N first-time calls produces at most one invocation; (c) first call throws → flag not set, retry on next call succeeds. |
| Staging validation step (FR-4) requires App Insights inspection that cannot be automated in CI. | Low | Spec already lists this as a manual PR checklist item; surface it explicitly in the PR template. |

## Specification Amendments

- **API/Interface section, "DI registrations" sentence.** Spec currently says "DI registrations in `AzureAdapterModule` remain unchanged in lifetimes (Singleton `BlobContainerClient`, Scoped `IPrintQueueSink`), unless the chosen design requires a new singleton helper." Amend to: "DI registration of `IPrintQueueSink` changes from Scoped to Singleton (`AzureAdapterModule.cs:24`). `BlobContainerClient` remains Singleton." This is a deliberate change driven by Decision 1 and must be called out so reviewers do not flag it as drift.
- **Acceptance criteria, FR-3.** Tighten "acceptable: exactly once; not acceptable: N times" to require a unit test using `Task.WhenAll` of ≥ 4 concurrent first-time `SendAsync` invocations against a fresh sink instance, asserting `_containerClient.CreateIfNotExistsAsync` was invoked exactly once. Without an enforced parallel-call test, FR-3 is unverifiable.
- **Add NFR-2 regression test requirement.** Spec asserts the retry-after-failure property but the test list does not name it. Add: "Test: first `CreateIfNotExistsAsync` throws → assert second `SendAsync` re-invokes `CreateIfNotExistsAsync` once (and only once thereafter once successful)."
- **Out of Scope clarification.** Add: "The equivalent NFR-2 hardening for `AzureBlobStorageService.GetOrCreateContainerAsync` (where the flag is set *before* awaiting the SDK call) is a known precedent gap and is out of scope for this fix."

## Prerequisites

None. No migrations, no configuration changes, no infrastructure provisioning. The fix is pure code:

- `Azure.Storage.Blobs` already referenced.
- `Microsoft.Extensions.DependencyInjection` already referenced.
- `SemaphoreSlim` is in BCL — no NuGet additions.
- Existing `ExpeditionList:BlobConnectionString` configuration key and `PrintPickingListOptions` remain unchanged.
- Existing test project `Anela.Heblo.Tests` already references the adapter and Moq — no new test infrastructure.