telemetry-signal: dep-fail:Azure blob:stheblo.blob.core.windows.net

**Window:** P7D (2026-06-05 – 2026-06-12)
**Failures:** 16 / 231 total blob calls (6.9% failure rate, resultCode 409 Conflict)

## Signal

Azure Blob Storage dependency (`stheblo.blob.core.windows.net`) returning HTTP 409 Conflict on 6.9% of operations — indicating concurrent-write conflicts or lease collisions on blob resources.

| Metric | Value |
|---|---|
| Dependency type | `Azure blob` |
| Target | `stheblo.blob.core.windows.net` |
| Result code | 409 Conflict |
| Failures in window | 16 |
| Total calls in window | 231 |
| Failure rate | 6.9% |
| Successful call latency | p50 26ms, p95 205ms, p99 446ms |

## Analysis

HTTP 409 from Azure Blob Storage means one of:
- A blob was created while another writer was also trying to create it (no `IfNoneMatch: *` guard)
- A leased blob was written/deleted without supplying the active lease token
- A container-level conflict (attempting to delete a container that still has active operations)

Notably, the `InProc | Microsoft.Storage | BlobClient.Upload` metric shows 160 upload operations with **0 failures**, meaning direct upload InProc calls succeed — the 409s are coming from a different blob operation path (likely conditional create, metadata update, or a finalise step).

## Correlation hypothesis

No direct merge correlation. The blob storage is used by the Photobank module (thumbnail uploads) and any file-storage workflow. The 6.9% conflict rate could be from a scenario where the same thumbnail is generated/written by concurrent requests for the same resource without a distributed lock or idempotency guard.

## Next step

1. Add an App Insights query scoped to `dependencies | where type == "Azure blob" and resultCode == "409"` to retrieve the `name` field — this will identify the exact blob operation (e.g. `BlobClient.UploadAsync`, `BlobContainerClient.CreateAsync`).
2. Cross-reference the `operation_Name` to find which controller or handler triggers the conflicting writes.
3. If it is thumbnail generation: add an existence check before upload, or switch to `UploadAsync` with `overwrite: false` and handle the `RequestFailedException(409)` as an expected no-op when another writer already wrote the same content.