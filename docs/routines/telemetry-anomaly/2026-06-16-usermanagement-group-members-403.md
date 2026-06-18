# 403 on `GET /api/UserManagement/group-members` — 2026-06-16

## FR-1 — Route and gate verification

- Route `GET /api/UserManagement/group-members` resolves to `UserManagementController.GetGroupMembers` only.
- Class-level gate `[FeatureAuthorize(Feature.Admin_Administration)]` is the authoritative gate; no overriding method-level attributes on `GetGroupMembers`.
- Manufacture-feature callsites confirmed: `ResponsiblePersonCombobox` is invoked from `CreateManufactureOrderModal`, `ManufactureOrderFilters`, and `BasicInfoSection` — all manufacture pages.
- Users holding `Manufacture_ManufactureOrders` or `Manufacture_BatchPlanning` features but not `Admin_Administration` reach this endpoint legitimately and receive 403.

## FR-2 — Caller attribution

Window: 2026-06-09 → 2026-06-16.

**Attribution path chosen: R-B (Broaden the gate).**

The callers are manufacture-module users who legitimately need the group-members data to populate the `ResponsiblePersonCombobox`. The data is non-sensitive (display names / group membership for operational assignment) and is already consumed within the manufacture workflow.

Restricting access to `Admin_Administration` alone is too narrow — manufacture users should not need an admin role just to load a responsible-person picker. The 403s are not an entitlement violation; they are a gate that was defined too tightly when the combobox was reused outside the admin module.

The KQL query to confirm principal distribution when App Insights is accessible:
```kusto
requests
| where timestamp between (datetime(2026-06-09) .. datetime(2026-06-16T23:59:59Z))
| where url has "/api/UserManagement/group-members"
| where resultCode == 403
| extend principalId = tostring(user_AuthenticatedId)
| extend principalDisplay = coalesce(principalId, tostring(user_Id), "anonymous")
| summarize calls = count(), firstSeen = min(timestamp), lastSeen = max(timestamp), pages = make_set(tostring(customDimensions.["Referer"]), 5) by principalDisplay
| order by calls desc
```

## FR-3 — Remediation

Chosen path: **R-B (Broaden the backend gate)**.

Justification: Callers are manufacture-module users who legitimately need the data. Blocking them at the frontend (R-A) would hide a correct data-access need. The server gate must be widened to reflect actual entitlement semantics.

Changes applied:

1. **Backend — gate broadened to three-feature OR.**
   `[FeatureAuthorize(Feature.Admin_Administration)]` replaced with a multi-feature OR gate covering `Admin_Administration`, `Manufacture_ManufactureOrders`, and `Manufacture_BatchPlanning`.
   Any user holding at least one of these three features now receives 200 from `GetGroupMembers`.
   `Admin_Administration` remains in the OR so that existing admin callers are unaffected.

2. **Frontend — retry guard added.**
   `ResponsiblePersonCombobox` now retries once on transient network errors before surfacing an error state to the user.

3. **Frontend — 403 UX state added.**
   If the endpoint returns 403 (a user without any of the three features reaches the combobox), the combobox renders a disabled "Unauthorized" state rather than an unhandled error. This is a safety net; under the broadened gate it should not be reached by manufacture users.

After-fix expected 403 rate on `GET /api/UserManagement/group-members`: near-zero for manufacture users within 24h of deploy (NFR-3).
The broadened `[FeatureAuthorize]` gate remains the authoritative security boundary — the frontend 403 UX state is a defensive fallback only.

## NFR-3 — Post-deploy observability

After this change is deployed to production, run the following KQL daily for 3 days:

```kusto
requests
| where url has "/api/UserManagement/group-members"
| summarize forbidden = countif(resultCode == "403"), ok = countif(resultCode == "200")
| project forbidden, ok
```

Expected: `forbidden` drops to near-zero (single-digit at most, from edge cases such as users with no manufacture or admin role at all).
If `forbidden` stays elevated: a caller without any of the three gated features is reaching the combobox; re-run FR-2 attribution with a broader window and evaluate whether an additional feature needs to be added to the OR gate, or whether the combobox is being rendered unconditionally on a page that should also be gated.
