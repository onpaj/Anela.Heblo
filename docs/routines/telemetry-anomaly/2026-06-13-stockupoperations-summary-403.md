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

_TBD by Task 10._

## NFR-3 — Post-deploy observability

_TBD by Task 11._
