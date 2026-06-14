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
