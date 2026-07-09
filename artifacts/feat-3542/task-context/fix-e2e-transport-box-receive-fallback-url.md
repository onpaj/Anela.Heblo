### task: fix-e2e-transport-box-receive-fallback-url

**Goal (FR-2):** `navigateToTransportBoxReceive`'s direct-navigation fallback currently goes to `/warehouse/transport-box-receive`, a URL with **no matching `<Route>`** anywhere in `App.tsx` (confirmed: the only registered route for this page is `/logistics/receive-boxes` at `frontend/src/App.tsx:437`, and there is no wildcard/catch-all `<Route path="*">` in the file). Navigating to an unmatched URL renders nothing at all — no `Layout`, no `<main>` — which is the distinct `locator('main, [role="main"]')` timeout unique to `box-receive.spec.ts` (6 of the 18 failures). Fix the one literal string.

**File to modify:** `frontend/test/e2e/helpers/e2e-auth-helper.ts`

Current code at line 314 (inside `navigateToTransportBoxReceive`, in the "UI navigation failed, fall back to direct URL" path):

```typescript
  // If UI navigation fails, go directly to the path
  console.log('🔄 Trying direct navigation to transport box receive...');
  const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
  await page.goto(`${baseUrl}/warehouse/transport-box-receive`);
  await page.waitForLoadState('domcontentloaded');
  await waitForPageLoad(page);

  console.log('✅ Direct navigation to transport box receive completed');
}
```

Use the Edit tool with this exact `old_string`/`new_string` pair:

old_string:
```typescript
  // If UI navigation fails, go directly to the path
  console.log('🔄 Trying direct navigation to transport box receive...');
  const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
  await page.goto(`${baseUrl}/warehouse/transport-box-receive`);
```

new_string:
```typescript
  // If UI navigation fails, go directly to the path
  console.log('🔄 Trying direct navigation to transport box receive...');
  const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
  await page.goto(`${baseUrl}/logistics/receive-boxes`);
```

This is the only line that changes. `/logistics/receive-boxes` matches the registered route (`frontend/src/App.tsx:437`: `<Route path="/logistics/receive-boxes" element={guard("/logistics/receive-boxes", <TransportBoxReceivePage />)} />`) and the sidebar's own `href` (`frontend/src/components/Layout/Sidebar.tsx:273`: `href: "/logistics/receive-boxes"`).

**Spot-check for the same class of bug in the rest of the file (per FR-2's acceptance criteria — do not skip this check):** Read through every other `navigateTo*` function in `frontend/test/e2e/helpers/e2e-auth-helper.ts` and confirm each fallback URL matches a real registered route in `frontend/src/App.tsx`:
- `navigateToTransportBoxes` (line 221): `/logistics/transport-boxes` — matches `App.tsx`. No change needed.
- `navigateToCatalog` (line 256): `/catalog` — matches. No change needed.
- `navigateToStockOperations` (line 270): `/stock-up-operations` — matches. No change needed.
- `navigateToInvoiceClassification` (line 358): `/purchase/invoice-classification` — matches. No change needed.
- `navigateToIssuedInvoices` (line 402): `/customer/issued-invoices` — matches. No change needed.
- `navigateToMarketingCalendar` (line 451): `/marketing/calendar` — matches. No change needed.

Confirm this by running:

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece
grep -n "page.goto(\`\${baseUrl}" frontend/test/e2e/helpers/e2e-auth-helper.ts
grep -n "<Route path=" frontend/src/App.tsx
```

Cross-check each `goto` path against the `Route path=` list; every one but the one just fixed should already have a match. If you find another mismatch, fix it the same way and note it in the commit message — but as of this investigation (verified against the current worktree) none was found besides the one fixed above.

**There is no dedicated unit test for this helper file** (it is a Playwright E2E helper, exercised only by the E2E specs themselves against a live staging deploy — see NFR-1). Verification for this task is:
1. TypeScript compiles cleanly (this file is included in the frontend's `tsc` pass via `npm run build`).
2. The full functional verification happens in NFR-1's staging E2E run, which is covered as part of the overall feature's final validation (after Tasks 1–3 land) — see the plan's Overview. Do not attempt to run `box-receive.spec.ts` against staging in isolation for this task alone unless Task 1 (the role grant) has already been deployed to staging, since the fallback URL fix alone still lands the E2E user on a route that will redirect to `/` without FR-1.

**Validation commands:**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/frontend
npx tsc --noEmit -p test/e2e/tsconfig.json 2>/dev/null || npx tsc --noEmit
npm run lint
```

(If `test/e2e` has its own `tsconfig.json`, use it; otherwise fall back to the root `tsc --noEmit`. Either way, this file must produce zero new TypeScript errors.)

**Commit:**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece
git add frontend/test/e2e/helpers/e2e-auth-helper.ts
git commit -m "$(cat <<'EOF'
Fix wrong fallback route in navigateToTransportBoxReceive E2E helper

The direct-navigation fallback pointed at /warehouse/transport-box-receive,
which has no matching <Route> in App.tsx and renders nothing (no Layout,
no <main>). Changed to /logistics/receive-boxes, the actual registered
route, matching Sidebar.tsx's own href and App.tsx:437.

Fixes the locator('main, [role="main"]') timeout unique to
box-receive.spec.ts (6 of 18 nightly transport E2E failures).
EOF
)"
```

---

