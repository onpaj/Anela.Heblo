# Azure Blob Storage 409 Conflict Diagnostic (FR-1)

## Background

Application Insights telemetry for `stheblo.blob.core.windows.net` showed 16 of 231 Azure Blob dependency calls returning HTTP 409 in the 7-day window 2026-06-05 → 2026-06-12 (6.9% failure rate). The `InProc | Microsoft.Storage | BlobClient.Upload` metric showed 0 failures across 160 calls, so the 409s originate from a different operation path.

## KQL query

Run against the `aiHeblo` (production) and `aiHeblo-test` (staging) App Insights resources:

```kusto
dependencies
| where timestamp > ago(14d)
| where type == "Azure blob"
| where resultCode == "409"
| extend container = tostring(split(data, "/")[3])
| summarize
    count = count(),
    sample_data = any(data),
    sample_operation = any(operation_Name),
    sample_role = any(cloud_RoleName)
    by name, container
| order by count desc
```

Re-run with a wider window if no rows return for `ago(14d)`.

## Raw results

### Staging (aiHeblo-test)

No 409 errors detected in the past 14 days.

### Production (aiHeblo)

| name | container | count | sample_data | sample_operation | sample_role |
|------|-----------|-------|-------------|------------------|-------------|
| PUT stheblo | expedition-lists?restype=container | 25 | https://stheblo.blob.core.windows.net/expedition-lists?restype=container | (empty) | Heblo-API-Production |
| PUT stheblo | shoptetexport?restype=container | 8 | https://stheblo.blob.core.windows.net/shoptetexport?restype=container | (empty) | Heblo-API-Production |

## Conclusion

All 33 production 409s (100%) originate from `BlobContainerClient.CreateIfNotExistsAsync` operations on two containers: `expedition-lists` (25 failures) and `shoptetexport` (8 failures). These calls come from `AzureBlobStorageService.EnsureContainerAsync()`, which is invoked during telemetry processor initialization and sink creation. The existing `BlobContainerEnsurance` class wraps the `CreateIfNotExistsAsync` call with idempotent 409 handling, converting HTTP 409 (container already exists) into a success outcome. The `BlobIdempotent409TelemetryProcessor` also marks any remaining 409s as success for telemetry purposes. Because 100% of detected 409s are PUT-container shape and are already handled by `BlobContainerEnsurance`, the existing mitigation covers FR-2 requirement scope. No additional blob-level 409 operation is observed. Per Architecture Review Decision 1 and Amendment 1, Task 6 (build `IIdempotentBlobUploader`) is **SKIPPED**. FR-3 post-deployment verification will confirm that production failure rate falls to ≤0.5%.

## Re-run schedule

FR-3 acceptance requires re-running the query 7 days after production deployment. Record the deployment timestamp and the post-deployment counts in the PR comment thread.

## FR-3 post-deployment verification

Run **7 days after production deployment** (record deployment timestamp here: `__pending — fill in after Production deploy__`).

### Failure-rate query

```kusto
let deploymentTs = datetime("__pending — fill in after Production deploy__");
dependencies
| where timestamp between (deploymentTs .. (deploymentTs + 7d))
| where type == "Azure blob"
| where target == "stheblo.blob.core.windows.net"
| summarize
    total = count(),
    failures409 = countif(resultCode == "409"),
    failure_rate_pct = round(100.0 * countif(resultCode == "409") / count(), 2)
```

**Acceptance:** `failure_rate_pct` ≤ 0.5%.

### Latency-regression query

```kusto
let deploymentTs = datetime("__pending — fill in after Production deploy__");
dependencies
| where timestamp between (deploymentTs .. (deploymentTs + 7d))
| where type == "Azure blob"
| where target == "stheblo.blob.core.windows.net"
| where success == true
| summarize
    p50 = percentile(duration, 50),
    p95 = percentile(duration, 95),
    p99 = percentile(duration, 99)
```

**Acceptance:** p50 ≤ 50ms, p95 ≤ 300ms, p99 ≤ 600ms (matching or improving the pre-fix baseline of p50 26ms, p95 205ms, p99 446ms).

### Recording results

Paste the query outputs and pass/fail verdict into the merged PR comment thread (or a follow-up issue if the PR is too old to comment on).
