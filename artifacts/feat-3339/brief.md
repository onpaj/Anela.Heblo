## Problem

`GET /api/UserManagement/group-members` returns **403 Forbidden** to non-admin users — 113 occurrences in the past 7 days (2026-06-17 – 2026-06-24), all during business hours, all from authenticated real users. The endpoint is correctly gated by `[FeatureAuthorize(Feature.Admin_Administration)]` (`backend/src/Anela.Heblo.API/Controllers/UserManagementController.cs:10`), so the 403 is the policy working as intended. The bug is on the frontend: a non-admin component fires the request unconditionally.

This is the narrowly-scoped, agent-ready remediation split off from #3336 (see grooming verdict there). The backend policy is correct and **must not be changed**.

## Root cause (confirmed in code)

The endpoint is consumed via:

- `frontend/src/api/hooks/useUserManagement.ts` → `useResponsiblePersonsQuery(groupId)` (`enabled: Boolean(groupId)`, `retry: 2`)
- `frontend/src/components/common/ResponsiblePersonCombobox.tsx` → calls the hook with no permission check

`ResponsiblePersonCombobox` is rendered in **non-admin Manufacture UI**:

- `frontend/src/components/modals/CreateManufactureOrderModal.tsx`
- `frontend/src/components/manufacture/detail/BasicInfoSection.tsx`
- `frontend/src/components/manufacture/list/ManufactureOrderFilters.tsx`

When a non-admin manufacture user opens any of these, the combobox fires `GET /api/UserManagement/group-members` (plus 2 retries), each returning 403. The combobox already supports a manual-entry fallback (`allowManualEntry`), so degrading gracefully is straightforward.

## Fix

Gate the request behind the admin permission so it never fires for non-admins:

1. In `ResponsiblePersonCombobox` (or the hook), read `usePermissionsContext().hasPermission('admin.administration.read')`.
2. Only enable the query when the user holds that permission — e.g. pass an `enabled` flag through `useResponsiblePersonsQuery` so the query stays disabled for non-admins (`enabled: Boolean(groupId) && canReadAdministration`).
3. For non-admin users, fall back to the existing manual-entry path (`allowManualEntry`) — they keep a working responsible-person field, just without the auto-loaded member dropdown.

Permission string: `admin.administration.read` (maps to `Feature.Admin_Administration`, see `frontend/src/auth/accessMatrix.generated.ts:103`). Use the existing `usePermissionsContext` pattern already used in `Sidebar`, `RequireMenuPath`, `ArticleGenerationForm`, etc.

**Do not relax the backend `[FeatureAuthorize(Feature.Admin_Administration)]` policy.**

## Open question (note for reviewer, not a blocker)

The grooming comment on #3336 assumed an admin UI caller, but the actual consumers are Manufacture order screens used by non-admins. If product intent is that manufacture users *should* be able to load the responsible-person list, the correct fix would instead be to expose a non-admin endpoint or relax the policy for this specific read — that is a larger product decision and out of scope here. This issue implements the safe, telemetry-clearing frontend guard; flag in the PR if you believe the manufacture feature genuinely needs the member list for non-admins.

## Success criteria

- No 403s from `GET /api/UserManagement/group-members` caused by non-admin users (the hook does not fire without `admin.administration.read`).
- Non-admin users can still set a responsible person via manual entry in all three Manufacture screens.
- Admin users see the auto-loaded member dropdown exactly as before.
- `npm run build` + `npm run lint` pass; relevant component tests pass.

## References

- Telemetry source: #3336 (113 × 403 over P7D), prior #3184 (54 × 403)
- Correct gate: `backend/src/Anela.Heblo.API/Controllers/UserManagementController.cs:10`
