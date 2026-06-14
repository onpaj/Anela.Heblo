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

| name | container | count | sample_data | sample_operation | sample_role |
|------|-----------|-------|-------------|------------------|-------------|
| _(filled in from query output)_ | | | | | |

## Conclusion

_(One paragraph naming the offending Azure SDK method — e.g. `BlobContainerClient.CreateIfNotExistsAsync` — and the originating code paths. If `PUT container` accounts for ≥80% of 409s, proceed with Phase 2 as planned. If a different operation dominates, document the alternative remediation strategy here before continuing.)_

## Re-run schedule

FR-3 acceptance requires re-running the query 7 days after production deployment. Record the deployment timestamp and the post-deployment counts in the PR comment thread.

## FR-3 post-deployment verification

Run **7 days after production deployment** (record deployment timestamp here: `__YYYY-MM-DDTHH:MM:SSZ__`).

### Failure-rate query

```kusto
let deploymentTs = datetime("__YYYY-MM-DDTHH:MM:SSZ__");
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
let deploymentTs = datetime("__YYYY-MM-DDTHH:MM:SSZ__");
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
