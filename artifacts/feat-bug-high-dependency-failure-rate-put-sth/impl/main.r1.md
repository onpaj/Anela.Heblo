---

# Implementation: Eliminate 409 Conflict Noise from `PUT stheblo` Blob Container Creation

## What was implemented

`AzureBlobPrintQueueSink` was fixed to call `CreateIfNotExistsAsync` at most once per process lifetime instead of on every `SendAsync` invocation. The implementation uses a `SemaphoreSlim(1,1)` + `bool _containerEnsured` double-checked locking pattern. The flag is set only after the SDK call returns successfully, so transient failures still allow retry on the next call. `IPrintQueueSink` was promoted from Scoped to Singleton so the cache survives across requests.

## Files created/modified

- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs` — added `_ensureGate` SemaphoreSlim + `_containerEnsured` bool; extracted `EnsureContainerAsync` private method with double-checked locking; `SendAsync` behavior (naming, overwrite, empty-list skip, warning) unchanged
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs` — line 24: `AddScoped` → `AddSingleton` for `IPrintQueueSink`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs` — added 3 tests: `SendAsync_CalledTwice_InvokesCreateIfNotExistsOnce` (FR-1), `SendAsync_FourParallelFirstCalls_InvokesCreateIfNotExistsExactlyOnce` (FR-3), `SendAsync_FirstCreateIfNotExistsThrows_RetriesOnNextCall` (NFR-2)

## Tests

`backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs` — 6 total (3 pre-existing + 3 new):
- Sequential dedup: 2 calls → exactly 1 `CreateIfNotExistsAsync`
- Concurrent dedup: 4 parallel first calls (gated via `TaskCompletionSource`) → exactly 1 `CreateIfNotExistsAsync`
- Retry after failure: first throws → flag stays false → second call retries → third uses cache (2 total invocations)

Full suite: 3312 tests, 0 failed.

## How to verify

```bash
cd backend
dotnet build
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AzureBlobPrintQueueSinkTests"
# Expected: 6 passed, 0 failed
```

For staging validation (FR-4, manual): after deployment, trigger ≥ 3 expedition list print operations and confirm in App Insights → Failures → Dependencies that only the **first** operation per app restart generates a `PUT {container}?restype=container` dependency entry.

## Notes

No deviations. Spec amendment (DI lifetime change from Scoped to Singleton) is intentional per architecture review Decision 1 and is documented in the commit message. The 316 build warnings are all pre-existing and unrelated to this fix.

## PR Summary

Eliminates ~27 false-positive `PUT stheblo` dependency failures per day in App Insights by ensuring `AzureBlobPrintQueueSink` calls `CreateIfNotExistsAsync` at most once per process lifetime. The fix mirrors the existing pattern in `AzureBlobStorageService` but hardens it: the cache flag is set only after the SDK call succeeds (so transient failures still allow retry), and a `SemaphoreSlim` gates concurrent first-time callers to exactly one invocation. `IPrintQueueSink` is promoted from Scoped to Singleton — all three dependencies (`BlobContainerClient`, `TimeProvider`, `ILogger<T>`) are already singleton-safe.

### Changes
- `AzureBlobPrintQueueSink.cs` — `SemaphoreSlim` + `_containerEnsured` bool with double-checked locking; `EnsureContainerAsync` private method; `SendAsync` behavior unchanged
- `AzureAdapterModule.cs:24` — `AddScoped` → `AddSingleton` for `IPrintQueueSink`
- `AzureBlobPrintQueueSinkTests.cs` — 3 new tests covering FR-1 (sequential dedup), FR-3 (concurrent dedup), NFR-2 (retry after transient failure)

**Manual staging check (FR-4):** Trigger ≥ 3 expedition list print operations against Staging. Confirm only the first generates a `PUT {container}?restype=container` entry in App Insights → Failures → Dependencies (filtered to `stheblo.blob.core.windows.net`).

## Status
DONE