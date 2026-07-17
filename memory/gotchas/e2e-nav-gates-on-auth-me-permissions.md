# E2E frontend nav gates on /api/auth/me permissions, not client-side roles

## Symptom
Nightly E2E renders the app "unauthenticated": sidebar shows only Dashboard, "Click to authenticate" everywhere, ~all role-gated module tests time out. Easy to misdiagnose as a workflow/secret regression (issue #3680 blamed the per-module matrix split). It is NOT: the workflow env/secrets were unchanged, and the backend E2E auth actually *succeeds* (`✅ E2E authentication successful`, SP token valid, `/api/e2etest/auth` issues the cookie, `env-info` → Staging).

## Root cause
The frontend sidebar/nav is gated by the **permission list from `GET /api/auth/me`** via `usePermissionsContext().hasPermission(...)` (`frontend/src/auth/PermissionsContext.tsx` → `usePermissions` → `client.auth_Me()`), **not** by the client-side `E2E_USER` roles in `frontend/src/auth/e2eAuth.ts`.

`GetMeHandler` returns the full permission set **only for `super_user`**; otherwise it calls `IPermissionResolver.ResolveAsync(...)`, which "returns empty for inactive/unknown users." The E2E synthetic user (`E2ESessionService.CreateSyntheticUserClaims`, `oid=e2e-test-object-id`) is not a DB user and had `Base` + per-module role claims but **not `super_user`** → `/api/auth/me` returned empty permissions → nav collapsed.

Per-module `ClaimTypes.Role` claims only satisfy API `[FeatureAuthorize]` endpoint gates (that's why Dashboard tiles loaded data) — they never populate the `/api/auth/me` permission list. Adding one role per module (commits #3540, #3542, #3670) was whack-a-mole at the wrong layer.

## Fix
Add `new Claim(ClaimTypes.Role, AccessRoles.SuperUser)` to `CreateSyntheticUserClaims` — same wildcard path `MockAuthenticationHandler` already uses for mock users. `PermissionClaimsTransformation` + `GetMeHandler` then grant the wildcard regardless of DB. (PR #3684)

## Fast repro (no secrets needed)
Browser to `https://heblo.stg.anela.cz/?e2e=true`: `isE2ETestMode()` is true and `useE2EAuth` authenticates client-side, but `GET /api/auth/me` → 401/empty and the sidebar is Dashboard-only. Pre-fix vs post-fix is visible here without the E2E cookie flow.

Related: [[e2e-auth-navigatetoapp-vs-createe2eauthsession]].
