# 403 storm on `GET /api/StockUpOperations/summary` — 2026-06-13

## FR-1 — Route and gate verification

- Route `GET /api/StockUpOperations/summary` resolves to `StockUpOperationsController.GetSummary` only.
  Source: `backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs:113`.
- Class-level gate `[FeatureAuthorize(Feature.Warehouse_StockUp)]` (Read) is the authoritative gate; no overriding method-level attributes on `GetSummary`.
- Write actions (`RetryOperation` at line 76, `AcceptOperation` at line 95) remain at `Warehouse_StockUp` Write — method-level `[FeatureAuthorize(Feature.Warehouse_StockUp, AccessLevel.Write)]`.
- No duplicate route registrations in `Program.cs` or extension modules (`grep -r MapGet.*StockUp backend/src` returns no matches).
- No additional `[Authorize]` or `[FeatureAuthorize]` attributes on `GetSummary` beyond the class-level gate.

## FR-2 — Caller attribution

Window: 2026-06-05 → 2026-06-12.

**Attribution status: Unable to query** — Application Insights is not accessible from this automated session context.

Per arch-review specification amendment FR-2 fallback rule:
> "If App Insights cannot identify the caller(s) (anonymous, or all rows have null `user_AuthenticatedId`), default to **R-A**."

The observed telemetry pattern (209/210 requests returning 403, ~42/day, absent for last 2 days) is consistent with a single user without `Warehouse_StockUp` Read sitting on one of the two pages that poll every 15 seconds. This is a UX noise issue, not a data-access entitlement issue for authorized users.

The KQL query to run when App Insights is accessible:
```kusto
requests
| where timestamp between (datetime(2026-06-05) .. datetime(2026-06-12T23:59:59Z))
| where url has "/api/StockUpOperations/summary"
| where resultCode == 403
| extend principalId = tostring(user_AuthenticatedId)
| extend principalDisplay = coalesce(principalId, tostring(user_Id), "anonymous")
| summarize calls = count(), firstSeen = min(timestamp), lastSeen = max(timestamp), pages = make_set(tostring(customDimensions.["Referer"]), 5) by principalDisplay
| order by calls desc
```

## FR-3 — Remediation

Chosen path: **R-A (Frontend gate)**.

Justification: Attribution was impossible (no App Insights access); applying arch-review fallback — default to R-A.
R-A is correct under the worst case: if the caller legitimately holds the permission, the server gate is unchanged for them (200 continues as before). For any unauthorized caller, R-A eliminates the 15-second polling storm before it hits the server.

After-fix expected 403 rate on `GET /api/StockUpOperations/summary`: near-zero within 24h (NFR-3).
The server `[FeatureAuthorize(Feature.Warehouse_StockUp)]` gate remains in place as the authoritative security boundary — R-A is a UX/cost optimization only.

## FR-5 — Single 500

**Attribution status: Unattributable** — Application Insights is not accessible from this automated session context.

The single 500 response occurred within the same 7-day window as the 403 storm. It resolved to the same route (`GET /api/StockUpOperations/summary` → `StockUpOperationsController.GetSummary`) because there is only one handler for this path.

The KQL query to investigate when App Insights is accessible:
```kusto
requests
| where timestamp between (datetime(2026-06-05) .. datetime(2026-06-12T23:59:59Z))
| where url has "/api/StockUpOperations/summary"
| where resultCode == 500
| project timestamp, id, name, resultCode, duration, customDimensions, operation_Id
| join kind=leftouter (
    exceptions
    | project operation_Id, exceptionType=type, exceptionMessage=outerMessage, exceptionStack=outerType
) on operation_Id
```

Given the low frequency (1 out of 210 requests), this is likely a transient infrastructure event (e.g., DB timeout, cold-start) rather than a reproducible defect in `GetStockUpOperationsSummaryHandler`. No fix applied in this PR. If the query above reveals a structured exception, file a follow-up issue.

## NFR-3 — Post-deploy observability

After this change is deployed to production, run the following KQL daily for 3 days:

```kusto
requests
| where timestamp > ago(24h)
| where url has "/api/StockUpOperations/summary"
| summarize calls = count(), forbidden = countif(resultCode == 403), ok = countif(resultCode == 200)
```

Expected: `forbidden` drops to near-zero (single-digit at most, from edge cases like users mid-permission-revocation).
If `forbidden` stays high: caller attribution (FR-2) missed a principal; re-run with a broader window and reconsider the chosen path.
