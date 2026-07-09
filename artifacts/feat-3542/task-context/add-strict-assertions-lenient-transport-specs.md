### task: add-strict-assertions-lenient-transport-specs

**Priority note: this task is recommended, not blocking, per the spec's FR-4 designation ("recommended, not blocking") and the architecture review's risk mitigation.** Land and verify Tasks 1–3 against staging first (per NFR-1). Only after that verification succeeds should this task's changes be added, so that if one of these three specs has its own latent, unrelated issue, it does not block or confuse the primary fix's rollout.

**Goal (FR-4):** `box-items.spec.ts`, `box-workflow.spec.ts`, and `ean-integration.spec.ts` wrap every meaningful assertion in `if (await x.count() > 0) { ... }` guards, so they report "pass" even when the Transport Box list never rendered at all (exactly what happened in nightly run #191 — these three specs "passed" despite hitting the identical root-cause failure as the four specs that did fail, purely because they never assert anything unconditionally). Add one unconditional assertion to each file's `beforeEach` so a regression like this one fails loudly in all specs that exercise this page, not just the ones that happen to assert strictly.

All three files share the identical `beforeEach` shape:

```typescript
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);
  });
```

**File 1: `frontend/test/e2e/transport/box-items.spec.ts`**

old_string:
```typescript
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);
  });
```

new_string:
```typescript
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);

    // Strict assertion (feature-3542 FR-4): the rest of this file's assertions are
    // all guarded by `if (await x.count() > 0)`, so a broken navigation/permission
    // gap would previously make every test in this file "pass" without exercising
    // anything. Fail loudly here if the Transport Box list never rendered.
    await expect(page.locator('h1')).toContainText('Transportní boxy');
  });
```

**File 2: `frontend/test/e2e/transport/box-workflow.spec.ts`**

old_string:
```typescript
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);
  });
```

new_string:
```typescript
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);

    // Strict assertion (feature-3542 FR-4): the rest of this file's assertions are
    // all guarded by `if (await x.count() > 0)`, so a broken navigation/permission
    // gap would previously make every test in this file "pass" without exercising
    // anything. Fail loudly here if the Transport Box list never rendered.
    await expect(page.locator('h1')).toContainText('Transportní boxy');
  });
```

**File 3: `frontend/test/e2e/transport/ean-integration.spec.ts`**

old_string:
```typescript
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);
  });
```

new_string:
```typescript
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);

    // Strict assertion (feature-3542 FR-4): the rest of this file's assertions are
    // all guarded by `if (await x.count() > 0)`, so a broken navigation/permission
    // gap would previously make every test in this file "pass" without exercising
    // anything. Fail loudly here if the Transport Box list never rendered.
    await expect(page.locator('h1')).toContainText('Transportní boxy');
  });
```

All three files already `import { test, expect } from '@playwright/test';` at the top (verified in each file), so `expect` is already in scope — no new imports needed. Do not change anything else in these files: every subsequent `if (await x.count() > 0)` conditional block in the test bodies is explicitly out of scope per the spec ("No behavioral change to the rest of each spec's conditional logic").

**How to test this change:** These are Playwright specs that only run meaningfully against a live staging deploy with a real E2E session (per NFR-1 — there is no local/mocked equivalent, since the E2E service-principal identity only exists in Staging/Development, `E2ETestController.cs` lines 68–73). After Tasks 1–3 have been deployed and staging is confirmed healthy, run:

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece
./scripts/run-playwright-tests.sh --grep "box-items|box-workflow|ean-integration"
```

Expected output: all three spec files pass, and specifically the new `beforeEach` assertion (`h1` contains "Transportní boxy") succeeds for every test in these files — confirming the Transport Box list actually rendered before each test's conditional logic runs. If any of these three specs fails at the new assertion, that indicates a real, previously-hidden issue with this page for that spec's flow — investigate separately; do not revert the assertion to "fix" the failure (that would recreate the exact silent-pass problem this task exists to close).

**Validation commands (static check only, before the staging run):**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/frontend
npx tsc --noEmit -p test/e2e/tsconfig.json 2>/dev/null || npx tsc --noEmit
npm run lint
```

**Commit:**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece
git add frontend/test/e2e/transport/box-items.spec.ts frontend/test/e2e/transport/box-workflow.spec.ts frontend/test/e2e/transport/ean-integration.spec.ts
git commit -m "$(cat <<'EOF'
Add strict h1 assertion to lenient transport E2E specs (FR-4)

box-items.spec.ts, box-workflow.spec.ts, and ean-integration.spec.ts wrap
every assertion in `if (await x.count() > 0)`, so they silently "pass"
even when the Transport Box list never renders (as happened in nightly
run #191, hitting the same root cause as the 4 specs that did fail
loudly). Adds one unconditional h1 check per file's beforeEach so a
regression like this one surfaces in every spec that exercises this
page, not just the ones that happened to assert strictly.

Recommended hardening per feature-3542 spec FR-4 (not blocking); land
after the primary fix (Warehouse_Logistics role grant + nav fallback +
error-state hardening) is verified against staging.
EOF
)"
```
