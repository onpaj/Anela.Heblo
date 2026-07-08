# Manual follow-up required after merge (feat-3540)

This PR fixes the backend API authorization gap (FR-1: E2E synthetic user now holds
`warehouse.stock_up.read`/`write` role claims) and hardens a broken E2E test (FR-3). It does
**not** and cannot make one additional required change:

## Action required: grant `warehouse.stock_up.read` to the E2E test account in the staging DB

The frontend route guard (`RequireMenuPath` on `/stock-up-operations`) does not consume the
ASP.NET Core role claims this PR adds. It gates on `GET /api/auth/me`'s resolved permission
list, which comes from a separate, DB-backed permission resolver
(`IPermissionResolver.ResolveAsync`, `backend/src/Anela.Heblo.Persistence/Features/Authorization/PermissionResolver.cs`).
That resolver looks up the E2E test `AppUser`'s **DB group memberships** — a mechanism this PR's
code change does not touch and a sandboxed development environment cannot reach or modify
(per this repo's CLAUDE.md: database migrations/data changes are manual, and secrets/config
live in Azure Key Vault, never in Web App environment variables edited directly).

**Steps for the repo owner to perform manually on staging, after this PR is merged and deployed:**

1. Sign in to `https://heblo.stg.anela.cz/admin/access` as an administrator.
2. Find the E2E test account (`oid` / `entraObjectId` = `e2e-test-object-id`, email
   `e2e-test@anela-heblo.com`).
3. Confirm whether its resolved permissions already include `warehouse.stock_up.read` — either
   via the `/admin/access` UI's effective-permissions view, or by calling `GET /api/auth/me`
   while authenticated as that account and inspecting the `permissions` array in the response.
4. If `warehouse.stock_up.read` is **not** present, add the E2E test account to an existing
   access group that grants it, or grant it directly through the `/admin/access` UI — scoped
   **only** to the E2E/staging test account, not to any production group or user (per spec
   NFR-2).
5. Re-run the nightly E2E suite (or manually run `./scripts/run-playwright-tests.sh
   stock-operations` against staging) and confirm all 56 previously-failing tests in
   `frontend/test/e2e/stock-operations/*.spec.ts` now pass.

This step is independent of and not blocked by the code changes in this PR — it can be done in
parallel with code review, but the nightly suite will not go fully green until both this PR's
code changes are deployed **and** this DB grant is made.
