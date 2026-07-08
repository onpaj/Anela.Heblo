# Task Plan: Fix Stock Operations E2E suite — 56 nightly failures (feat-3540)

## Context for the implementer

Root cause (confirmed by both `spec.r1.md` and `arch-review.r1.md`): the E2E synthetic test
identity's hardcoded role claims (`E2ESessionService.CreateSyntheticUserClaims()`) never grant
`AccessRoles.WarehouseStockUpRead` / `AccessRoles.WarehouseStockUpWrite`, so every
`/api/StockUpOperations*` call the E2E principal makes is rejected (403) and/or the frontend's
`RequireMenuPath` route guard silently redirects away from `/stock-up-operations` before the page
ever mounts. Separately, one E2E test in `navigation.spec.ts` has a case-mismatched route-intercept
glob and a soft (no-op-on-failure) assertion, so it "passes" without testing anything.

This plan covers the two fully-automatable fixes (backend claims grant, broken test hardening) plus
one small regression-test addition. It deliberately does **not** include a task that modifies the
staging database or Azure Key Vault — per this project's CLAUDE.md, database changes are manual and
secrets never go through Web App environment variables, and a sandboxed worktree has no access to
either. Task 4 produces a documentation artifact that surfaces the required manual DB grant as an
explicit follow-up in the PR body.

Tasks are ordered so the branch is shippable after each one. Each task is self-contained — you do
not need to have read this context section or any other task's reasoning, only the file paths and
code below.

---

### task: grant-e2e-warehouse-stockup-claims

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs:85-91`
- Test: `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ESessionServiceTests.cs` (new file)

**Goal:** Grant the E2E synthetic test principal `warehouse.stock_up.read`/`write` role claims so
`[FeatureAuthorize(Feature.Warehouse_StockUp)]` stops rejecting its calls to
`/api/StockUpOperations*` with 403.

- [ ] Step 1: Create the test file `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ESessionServiceTests.cs` with this exact content:

  ```csharp
  using System.Linq;
  using System.Security.Claims;
  using Anela.Heblo.API.Infrastructure.Authentication;
  using Anela.Heblo.Domain.Features.Authorization;
  using FluentAssertions;
  using Microsoft.Extensions.Logging;
  using Microsoft.Extensions.Logging.Abstractions;
  using Xunit;

  namespace Anela.Heblo.Tests.Infrastructure.Authentication;

  /// <summary>
  /// Regression coverage for the E2E synthetic user's role claims. A new
  /// [FeatureAuthorize]-gated feature shipping without a matching role claim added to
  /// CreateSyntheticUserClaims() has already caused two incidents: FinancialOverview
  /// (fixed previously) and Warehouse_StockUp (feat-3540, 56 nightly E2E failures).
  /// </summary>
  public class E2ESessionServiceTests
  {
      private readonly ILogger<E2ESessionService> _logger = NullLogger<E2ESessionService>.Instance;

      [Fact]
      public void CreateSyntheticUserClaims_IncludesWarehouseStockUpReadAndWriteRoles()
      {
          // Arrange
          var sut = new E2ESessionService(_logger);

          // Act
          var claims = sut.CreateSyntheticUserClaims("Staging");

          // Assert
          var roleClaims = claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value);
          roleClaims.Should().Contain(AccessRoles.WarehouseStockUpRead,
              "the E2E principal must be able to call GET /api/StockUpOperations, which is " +
              "gated by [FeatureAuthorize(Feature.Warehouse_StockUp)] (Read)");
          roleClaims.Should().Contain(AccessRoles.WarehouseStockUpWrite,
              "the E2E principal must be able to call POST /api/StockUpOperations/{id}/retry " +
              "and /accept, which are gated at AccessLevel.Write");
      }
  }
  ```

- [ ] Step 2: Run the new test and confirm it fails (red) against the current, unfixed
  `E2ESessionService`:
  ```
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~E2ESessionServiceTests
  ```
  Expect `CreateSyntheticUserClaims_IncludesWarehouseStockUpReadAndWriteRoles` to fail because
  neither role claim is present yet.

- [ ] Step 3: In `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs`,
  replace:
  ```csharp
              new Claim(ClaimTypes.Role, AccessRoles.Base), // Base role for application access
              new Claim("scp", "access_as_user"),
              // Grant the finance overview read role so E2E tests can reach /api/FinancialOverview.
              // FeatureAuthorize checks the role claim (permission strings were renamed away from the
              // old "FinancialOverview.View" form), so a stale "permission" claim no longer matches.
              new Claim(ClaimTypes.Role, AccessRoles.FinanceFinancialOverviewRead)
          };
  ```
  with:
  ```csharp
              new Claim(ClaimTypes.Role, AccessRoles.Base), // Base role for application access
              new Claim("scp", "access_as_user"),
              // Grant the finance overview read role so E2E tests can reach /api/FinancialOverview.
              // FeatureAuthorize checks the role claim (permission strings were renamed away from the
              // old "FinancialOverview.View" form), so a stale "permission" claim no longer matches.
              new Claim(ClaimTypes.Role, AccessRoles.FinanceFinancialOverviewRead),
              // Grant the Warehouse_StockUp read/write roles so E2E tests can reach
              // /api/StockUpOperations* (list, retry, accept). Without these, FeatureAuthorize
              // rejects every request with 403 before the controller action runs (feat-3540).
              new Claim(ClaimTypes.Role, AccessRoles.WarehouseStockUpRead),
              new Claim(ClaimTypes.Role, AccessRoles.WarehouseStockUpWrite)
          };
  ```

- [ ] Step 4: Re-run the same filtered test command from Step 2 and confirm it now passes (green).

- [ ] Step 5: Run the full backend test suite to check for regressions, then build:
  ```
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj && dotnet build && dotnet format --verify-no-changes
  ```
  If `dotnet format` reports changes, run `dotnet format` (no `--verify-no-changes`) and re-check
  the diff only touches the two files above.

- [ ] Step 6: Commit with message `fix(e2e): grant Warehouse_StockUp read/write claims to E2E synthetic user (feat-3540)`.

---

### task: harden-stock-operations-error-state-e2e-test

**Files:**
- Modify: `frontend/test/e2e/stock-operations/navigation.spec.ts:83-115`
- Modify: `frontend/test/e2e/helpers/stock-operations-test-helpers.ts:22-27`

**Goal:** Fix the case-mismatched route-intercept glob and replace the soft, no-op-on-failure
assertion in the `'should display error state on API failure'` test so it actually exercises and
verifies the error path, independent of Task 1's fix.

- [ ] Step 1: Open `frontend/test/e2e/stock-operations/navigation.spec.ts` and replace the
  `'should display error state on API failure'` test body (currently lines 83-115) — replace this
  exact block:
  ```typescript
    // Intercept API calls and force failure
    await page.route('**/api/stock-up-operations**', route => {
      route.abort('failed');
    });

    // Navigate to stock operations page (will trigger failed API call)
    const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
    await page.goto(`${baseUrl}/stock-up-operations`);
    await page.waitForTimeout(3000);

    // Check for error message
    const errorMessage = page.locator('text="Chyba při načítání operací"');
    const isErrorVisible = await errorMessage.isVisible();

    if (isErrorVisible) {
      console.log('   ✅ Error state displayed');

      // Verify retry button exists
      const retryButton = page.getByRole('button', { name: /Zkusit znovu/i });
      await expect(retryButton).toBeVisible();
      console.log('   ✅ Retry button present');
    } else {
      console.log('   ℹ️ Error state not triggered (possible caching)');
    }
  ```
  with:
  ```typescript
    // Intercept API calls and force failure.
    // The generated client builds this.baseUrl + "/api/StockUpOperations?" (PascalCase, no
    // dashes — see frontend/src/api/generated/api-client.ts:12051), so the glob must match
    // that literal casing or the route never intercepts and the real request goes through.
    await page.route('**/api/StockUpOperations**', route => {
      route.abort('failed');
    });

    // Navigate to stock operations page (will trigger failed API call)
    const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
    await page.goto(`${baseUrl}/stock-up-operations`);

    // Check for error message — hard assertion, so the test fails (not silently passes)
    // if the intercepted/aborted request doesn't produce the error UI.
    const errorMessage = page.locator('text="Chyba při načítání operací"');
    await expect(errorMessage).toBeVisible({ timeout: 15000 });
    console.log('   ✅ Error state displayed');

    // Verify retry button exists
    const retryButton = page.getByRole('button', { name: /Zkusit znovu/i });
    await expect(retryButton).toBeVisible();
    console.log('   ✅ Retry button present');
  ```
  Note this removes the `await page.waitForTimeout(3000);` line — the hard `expect(...).toBeVisible({ timeout: 15000 })` on the next line already waits, making the fixed timeout redundant and flake-prone.

- [ ] Step 2: Confirm the edited test file has no leftover references to the old
  `isErrorVisible` variable or the old glob pattern:
  ```
  grep -n "stock-up-operations\|isErrorVisible" frontend/test/e2e/stock-operations/navigation.spec.ts
  ```
  Expect no matches (the only `stock-up-operations` occurrences left should be the URL path
  `/stock-up-operations`, not the API glob — re-check the grep output line by line, since that URL
  string will legitimately still match `stock-up-operations` as a substring; confirm no
  `**/api/stock-up-operations**` glob remains).

- [ ] Step 3: Type-check the edited spec file so the change is syntactically valid (this repo has
  no dedicated `tsconfig.json` under `frontend/test/e2e/`, so type-check directly against the root
  config):
  ```
  cd frontend && npx tsc --noEmit -p tsconfig.json --skipLibCheck test/e2e/stock-operations/navigation.spec.ts
  ```
  If this reports unrelated pre-existing errors elsewhere in the E2E suite (common, since E2E specs
  aren't part of the production build/tsconfig include list), confirm there are zero errors
  reported specifically for `navigation.spec.ts` and move on — E2E specs are not covered by
  `npm run build`, so this step is a best-effort syntax check, not a hard gate.

- [ ] Step 4: In `frontend/test/e2e/helpers/stock-operations-test-helpers.ts`, harden the shared
  `waitForTableUpdate` wait so it fails fast with a clear message instead of the generic 15s timeout
  whenever the page lands on the error card instead of rows/empty-state — this directly prevents a
  repeat of the exact failure mode this ticket investigated (56 tests all timing out with the same
  unhelpful "element not found" error). Replace this exact block:
  ```typescript
  export async function waitForTableUpdate(page: Page): Promise<void> {
    // Wait for either at least one data row or the empty-state message to appear
    await expect(
      page.locator('tbody tr').first().or(page.locator('h3').filter({ hasText: 'Žádné výsledky' }))
    ).toBeVisible({ timeout: 15000 });
  }
  ```
  with:
  ```typescript
  export async function waitForTableUpdate(page: Page): Promise<void> {
    // Wait for a data row, the empty-state message, or the error card to appear. Failing fast
    // on the error card (instead of only on the generic 15s timeout) gives a clear diagnostic
    // — "Chyba při načítání operací" almost always means the E2E principal lacks a required
    // permission (see feat-3540) rather than a genuine UI bug.
    const success = page.locator('tbody tr').first().or(page.locator('h3').filter({ hasText: 'Žádné výsledky' }));
    const errorHeading = page.locator('h3').filter({ hasText: 'Chyba při načítání operací' });
    const result = success.or(errorHeading);
    await expect(result).toBeVisible({ timeout: 15000 });

    if (await errorHeading.isVisible().catch(() => false)) {
      throw new Error(
        'Stock Operations page rendered the error card ("Chyba při načítání operací") instead ' +
        'of data rows or the empty state. This usually means the caller lacks a required ' +
        'permission (e.g. warehouse.stock_up.read) rather than a genuine data/UI bug.'
      );
    }
  }
  ```

- [ ] Step 5: Confirm no other spec file relies on `waitForTableUpdate` resolving successfully when
  the page is in the error state (it shouldn't — the dedicated error-state test in Step 1 asserts
  on `errorMessage` directly, not via this helper):
  ```
  grep -rln "waitForTableUpdate" frontend/test/e2e/stock-operations/
  ```
  Read each matching file's usage and confirm none of them intentionally expect an error state at
  the point they call `waitForTableUpdate` (the error-state test itself, from Step 1, does not call
  `waitForTableUpdate` at all, so it is unaffected by this change).

- [ ] Step 6: This repo's `npm run lint` script (`eslint src --ext .ts,.tsx`) only covers
  `frontend/src`, not `frontend/test/e2e`, so it will not pick up these two files — skip it for
  this task and rely on Step 3's type-check plus manual review of the diff instead.

- [ ] Step 7: Commit with message `fix(e2e): correct StockUpOperations route-intercept casing and use hard assertions in error-state test (feat-3540)`.

---

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
