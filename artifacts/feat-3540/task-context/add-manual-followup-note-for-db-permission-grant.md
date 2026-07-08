### task: add-manual-followup-note-for-db-permission-grant

**Files:**
- Modify: `artifacts/feat-3540/task-plan.r1.md` (this file — no changes needed, informational only)
- Add: `artifacts/feat-3540/MANUAL-FOLLOWUP.md` (new file)

**Goal:** Surface the one change this pipeline cannot make itself — granting
`warehouse.stock_up.read` to the E2E test account's permission group in the staging database (spec
FR-2) — as an explicit, actionable manual follow-up so it isn't silently dropped once the PR merges.

- [ ] Step 1: Create `artifacts/feat-3540/MANUAL-FOLLOWUP.md` with this exact content:

  ```markdown
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
  ```

- [ ] Step 2: Verify the file was created and is valid markdown (no unclosed code fences):
  ```
  cat artifacts/feat-3540/MANUAL-FOLLOWUP.md
  ```

- [ ] Step 3: Commit with message `docs(feat-3540): add manual staging DB follow-up note for warehouse.stock_up.read grant`.

  Note for whoever finalizes the PR: surface the contents of
  `artifacts/feat-3540/MANUAL-FOLLOWUP.md` prominently in the PR description (e.g. under a "Manual
  follow-up required" heading) so it is not missed — this file living only in `artifacts/` is not
  itself visible on the PR unless quoted into the PR body.

---

## Scope notes / deliberately omitted work

- **Spec FR-4** (a reflection-based test scanning every `[FeatureAuthorize]`-gated controller
  action and cross-checking it against `CreateSyntheticUserClaims()`, e.g.
  `E2ESyntheticClaimsCoverageTests.cs`) is explicitly called out by `arch-review.r1.md` as
  "recommended, not strictly required by the spec but cheap insurance," and describes a simpler,
  equivalent alternative: a direct unit test asserting the synthetic claim set contains the two
  `Warehouse_StockUp` roles. That simpler version is implemented in Task 1
  (`E2ESessionServiceTests.cs`) as part of the TDD flow for the claims fix itself — it directly
  prevents a recurrence of this exact bug (a claim silently missing from the hardcoded list) with
  no additional task needed. The broader reflection-based sweep across *all* controllers is a
  larger, separate effort (auditing every other feature's read endpoints and building an allowlist
  for intentionally-unreachable ones) and is left out of this bite-sized plan; if desired, file it
  as a separate follow-up issue rather than folding it into this fix.
- **No task modifies `RequireMenuPath.tsx`** (e.g. adding a `console.warn` on permission-denied
  redirects). Neither `spec.r1.md` nor `arch-review.r1.md` currently define this as a requirement
  for this ticket — `RequireMenuPath.tsx` was read directly as part of planning this task and
  confirmed to already exist as a small, side-effect-free component; adding logging there would be
  a reasonable, low-risk future diagnostic improvement, but it is not needed to satisfy FR-1 or
  FR-3's acceptance criteria and is left out to keep this change surgical, per this project's
  "touch only what the task requires" rule.
- **No task touches the staging database or Azure Key Vault.** Spec FR-2 (confirm/grant
  `warehouse.stock_up.read` in the DB-backed permission resolver) is a data/configuration change in
  a live environment that a sandboxed worktree cannot perform; Task 3 above produces the follow-up
  documentation instead, per this plan's explicit constraint.

## Self-review against spec acceptance criteria

- FR-1 "`CreateSyntheticUserClaims()` returns a claim set including `WarehouseStockUpRead` and
  `WarehouseStockUpWrite`" → Task 1, Step 3, verified by Task 1 Step 4's passing test.
- FR-1 "an authenticated call ... to `GET /api/StockUpOperations` returns 200, not 403" → follows
  directly from the claim grant (ASP.NET Core `[Authorize(Roles=...)]` behavior is not something
  this repo's unit tests re-verify per-endpoint; `StockUpOperationsControllerAuthorizationTests.cs`
  already covers the controller side of this contract and is unaffected by this change) —
  confirmed at the "all 56 tests pass" level once this PR is deployed and Task 3's DB follow-up is
  done, per FR-2's acceptance criteria.
- FR-3 "route-interception pattern ... matches the real endpoint casing" → Task 2, Step 1.
- FR-3 "hard `await expect(errorMessage).toBeVisible()` assertion, ... retry-button assertion
  unconditional" → Task 2, Step 1 (both are now unconditional `expect(...).toBeVisible()` calls).
- FR-3 "test fails (not silently passes) if the error UI ... does not appear" → satisfied by the
  hard assertions in Task 2, Step 1; no `if`/`console.log`-only branch remains.
- FR-3 "test continues to pass once genuinely exercising the intercepted/aborted request path" →
  the fixed glob (`**/api/StockUpOperations**`) now matches the real request path regardless of
  FR-1/FR-2's outcome, so the abort — and therefore the error UI — is deterministic.
