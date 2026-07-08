# Specification: Fix Catalog Page Navigation E2E Regression (navigateToCatalog)

## Summary

The nightly E2E regression suite reports 84 failing tests across every spec in `frontend/test/e2e/catalog/`, all failing at the same `beforeEach`/setup assertion: after calling `navigateToCatalog(page)`, `page.url()` still shows the app root (`https://heblo.stg.anela.cz/?e2e=true`) instead of `/catalog`. Code inspection confirms the root cause is a structural bug in the `navigateToCatalog` test helper (`frontend/test/e2e/helpers/e2e-auth-helper.ts`), not in the Catalog page/route/sidebar itself: the helper's direct-navigation fallback is unreachable under the exact conditions staging produces, so when UI-based navigation doesn't complete in time, the test is silently left on the root URL. This spec defines the fix to `navigateToCatalog` (and verification of the surrounding sidebar/route code it depends on) so that the Catalog page reliably loads before the 84 downstream assertions run.

## Background

`frontend/test/e2e/catalog/*.spec.ts` (9 spec files, 84 tests) all import and call `navigateToCatalog` from `frontend/test/e2e/helpers/e2e-auth-helper.ts` in their `beforeEach`/setup, then immediately assert `expect(page.url()).toContain('/catalog')`. In nightly run #191 (branch `main`, commit `738a99c`) every one of these assertions failed with the browser still on `/?e2e=true`.

Reading the current implementation of `navigateToCatalog` (lines 234-260 of `e2e-auth-helper.ts`):

```ts
export async function navigateToCatalog(page: any): Promise<void> {
  await navigateToApp(page);

  const produktySelector = page.locator('text="Produkty"').first();
  try {
    if (await produktySelector.isVisible({ timeout: 2000 })) {
      await produktySelector.click();
      await waitForLoadingComplete(page);

      const katalog = page.locator('text="Katalog"').first();
      if (await katalog.isVisible({ timeout: 2000 })) {
        await katalog.click();
        await page.waitForLoadState('domcontentloaded');
        await waitForLoadingComplete(page);
      }
    }
  } catch (e) {
    // If UI navigation fails, go directly to the path
    const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
    await page.goto(`${baseUrl}/catalog`);
    await page.waitForLoadState('domcontentloaded');
    await waitForLoadingComplete(page);
  }
}
```

Two defects combine to produce the observed symptom:

1. **The fallback is dead code under the failure mode that actually occurs.** Playwright's `locator.isVisible({ timeout })` resolves to `false` when the element isn't found within the timeout — it does **not** throw. The only code path that reaches direct navigation to `/catalog` is the `catch` block, which only runs on a genuine thrown exception (e.g. a strict-mode locator violation). If `produktySelector` or `katalog` simply isn't visible in time, `isVisible()` returns `false`, the `if` bodies are skipped, the `try` block completes normally, `catch` never runs, and the function returns having done nothing. The test is left exactly where `navigateToApp()` left it: the app root. This matches the reported symptom exactly (`https://heblo.stg.anela.cz/?e2e=true`).

   Contrast with the two working helpers in the same file, `navigateToTransportBoxes` and `navigateToTransportBoxReceive`: both place their direct-navigation fallback **unconditionally after** the `try/catch` block (only skipped via an explicit early `return` on confirmed UI-navigation success). That structure guarantees a fallback runs whenever UI navigation didn't succeed, regardless of whether a timeout or an exception caused the miss. `navigateToCatalog` is the odd one out — its fallback is nested inside `catch` only.

2. **The 2000ms `isVisible` timeouts race against permission-gated, asynchronously-loaded sidebar content.** `frontend/src/components/Layout/Sidebar.tsx` builds `navigationSections` by filtering `allSections` through `canSeeItem`, which calls `hasPermission()` from `frontend/src/auth/PermissionsContext.tsx`. In `PermissionsContext.tsx`, `hasPermission` is defined as `(data?.isSuperUser ?? false) || (data?.permissions ?? []).includes(perm)` — while the `/me`-style permissions fetch (`usePermissions`) is still in flight, `data` is `undefined`, so `hasPermission` returns `false` for every permission and every gated section (including "Produkty", which gates the "Katalog" sub-item at `href: "/catalog"`, `key: "/catalog"`) is filtered out of `navigationSections` entirely (`if (visibleItems.length === 0) continue;` in `Sidebar.tsx`). `navigateToApp()` (called at the top of `navigateToCatalog`) only waits for the React shell to mount (`.App`, `#root > div`, or `nav`) — it does **not** wait for the permissions fetch to resolve. So on any run where the permissions call is slow (staging network/cold-start latency is called out repeatedly elsewhere in this same file, e.g. the 120s auth timeout and exponential backoff on `createE2EAuthSession`), "Produkty" — and therefore "Katalog" — can legitimately not exist in the DOM yet when `navigateToCatalog` checks for it with only a 2000ms budget. Every other UI-driving helper in this file (`navigateToTransportBoxes`, `navigateToTransportBoxReceive`) uses a 5000ms timeout for the equivalent check.

Given defect 1 makes the fallback unreachable on a timeout miss, and defect 2 makes a timeout miss plausible/likely under staging conditions, the two together fully explain why all 84 catalog tests fail identically at the same assertion, and why the failure is intermittent-looking at the level of "the app worked, but only sometimes" (a timing race, not a permanent route break). The `/catalog` route itself (`frontend/src/App.tsx`, `<Route path="/catalog" element={guard("/catalog", <CatalogList />)} />` guarded by `RequireMenuPath`) and the sidebar's "Katalog" entry (`frontend/src/components/Layout/Sidebar.tsx`, `{ id: "catalog", name: "Katalog", href: "/catalog", key: "/catalog" }`) are present, correctly wired, and not implicated by this investigation — this is a test-infrastructure bug, not a product bug.

`RequireMenuPath.tsx` was also inspected to rule out an alternative theory (redirect-on-permission-denied racing with page load): it returns `null` while `isLoading` is true (no premature redirect) and only issues `<Navigate to="/" replace />` once permissions have resolved and are confirmed insufficient — this does not explain the failure by itself, but is noted because it means the fix's direct-navigation fallback is safe to rely on: it will not be redirected away as long as the E2E principal genuinely holds `products.catalog.read` (see Open Questions/assumption below).

## Functional Requirements

### FR-1: Fix Catalog page navigation E2E regression

`navigateToCatalog` in `frontend/test/e2e/helpers/e2e-auth-helper.ts` must reliably land the browser on a URL containing `/catalog` before returning, regardless of whether the sidebar UI path or a direct `page.goto` fallback was used, and regardless of transient permission-fetch latency on staging.

Required changes to `navigateToCatalog`:

1. **Make the fallback reachable on a timeout miss, not just on a thrown exception.** Restructure so that direct navigation to `${baseUrl}/catalog` runs whenever UI navigation did not already confirm success — mirroring the pattern already used by `navigateToTransportBoxes`/`navigateToTransportBoxReceive` in the same file (fallback placed after the try/catch, success path returns early). A caught exception must not be the only trigger for the fallback.
2. **Give the permission-gated sidebar items enough time to appear.** Align the `isVisible` timeout(s) for "Produkty" and "Katalog" with the rest of the file's UI-driven helpers (5000ms), rather than the current 2000ms, since these are gated behind an async permissions fetch that `navigateToApp()` does not wait for.
3. **Verify the end state before returning.** After UI navigation and/or the fallback, the function must confirm `page.url()` contains `/catalog` (or equivalent) before returning; if neither UI navigation nor the fallback lands on `/catalog`, the helper should throw a clear, descriptive error rather than returning silently. This turns a silent no-op into a fast, diagnosable failure at the helper call site instead of 84 generic downstream assertion failures.
4. **No changes to product code required.** `frontend/src/components/Layout/Sidebar.tsx`, `frontend/src/App.tsx` (route + `guard`), and `frontend/src/components/auth/RequireMenuPath.tsx` were inspected and are correctly wired; this is scoped to the E2E helper only unless implementation turns up evidence to the contrary (see FR-2).

**Acceptance criteria:**
- Given the E2E service principal is authenticated and holds `products.catalog.read`, calling `navigateToCatalog(page)` against staging results in `page.url()` containing `/catalog` on completion, in both the UI-click path and the direct-`goto` fallback path (verify by temporarily forcing each path, e.g. via a short-circuited "Produkty" locator in a local test, or by asserting behavior via code review + a staging smoke run).
- The specific failing assertion from the brief no longer fails:
  ```
  await navigateToCatalog(page);
  expect(page.url()).toContain('/catalog');
  ```
  This assertion (or its equivalent) passes for all 9 affected spec files: `catalog/filter-edge-cases.spec.ts` (17 tests), `catalog/text-search-filters.spec.ts` (16), `catalog/combined-filters.spec.ts` (13), `catalog/pagination-with-filters.spec.ts` (13), `catalog/clear-filters.spec.ts` (10), `catalog/sorting-with-filters.spec.ts` (10), `catalog/product-type-filter.spec.ts` (3), `catalog/margins-chart.spec.ts` (1), `catalog/ui.spec.ts` (1) — 84 tests total.
  - Note: passing this initial navigation assertion unblocks each test to proceed into its own filter/sort/pagination logic; this spec's acceptance criteria cover only the navigation step. Any *further* failures inside a given test, once navigation succeeds, belong to that test/feature and are out of scope here (see Out of Scope).
- If UI navigation fails and the fallback also fails to reach `/catalog` (e.g., genuine permission denial), `navigateToCatalog` throws a descriptive error (naming what was attempted and the resulting URL) instead of returning silently — verifiable by unit-level/manual test of the helper with a deliberately-revoked permission or an unreachable route.
- No regression to the other navigation helpers in `e2e-auth-helper.ts` (`navigateToTransportBoxes`, `navigateToStockOperations`, `navigateToTransportBoxReceive`, `navigateToApp`) — their behavior and timeouts are unchanged by this fix unless FR-2 determines a shared fix is warranted.
- `frontend/src/components/Layout/Sidebar.tsx`, `frontend/src/App.tsx`, `frontend/src/components/auth/RequireMenuPath.tsx`, and `frontend/src/auth/PermissionsContext.tsx` remain unmodified by this fix (product code is not implicated) — unless investigation during implementation finds a genuine product-side defect, in which case FR-2 governs.

### FR-2: Confirm no product-side contribution and no impact to E2E-mode permission loading

Before closing this issue, confirm (via a staging run capturing timing, or via targeted logging/tracing during a debug run) that:
- The permissions fetch (`usePermissions` → `/me`-equivalent endpoint) that gates sidebar visibility is not abnormally slow or failing in the E2E-authenticated (`?e2e=true`) mode specifically (as opposed to normal user auth), since E2E tests use a service-principal-issued session distinct from interactive login.
- The E2E service principal used by the nightly suite genuinely holds `products.catalog.read` (per `frontend/src/auth/accessMatrix.generated.ts`: `"/catalog": { permissions: ["products.catalog.read"] }`), so that the FR-1 fallback is guaranteed to succeed rather than merely relocating the same failure one level down (UI nav timeout → fallback redirect-to-"/" via `RequireMenuPath`).

**Acceptance criteria:**
- A written note (PR description or code comment) confirms the E2E principal's permission grant was checked and is sufficient, OR a follow-up ticket is filed if it is found to be insufficient/misconfigured.
- If the permissions fetch is found to be abnormally slow in E2E mode, that finding is captured (either fixed here if trivial and in-scope, or filed as a separate ticket if it requires backend/infra changes) — do not silently paper over a slow-permissions-fetch product bug by only raising the E2E helper's timeout.

## Non-Functional Requirements

### NFR-1: Test reliability / flakiness budget

`navigateToCatalog` must not introduce new flakiness. The increased timeout (2000ms → 5000ms) must not materially slow the overall catalog suite (84 tests) beyond what's already budgeted for nightly runs (suite currently runs unattended overnight per `CLAUDE.md`: "E2E suite runs nightly, not in PR CI" — a few extra seconds per test setup is acceptable; multi-minute regressions are not).

### NFR-2: Fail-fast diagnostics

When `navigateToCatalog` cannot reach `/catalog` by any means, it must fail loudly and specifically (thrown error naming the attempted paths and final URL) at the helper call site, rather than allowing execution to continue into a test body that will fail later with a less informative, generically-worded assertion. This directly serves future debuggability of this exact class of regression.

## Data Model

Not applicable — this is a test-infrastructure fix with no change to domain entities, persistence, or API contracts.

## API / Interface Design

No public API changes. The only interface affected is the internal contract of the exported helper function:

```ts
export async function navigateToCatalog(page: any): Promise<void>
```

Contract (updated): on successful return, `page.url()` is guaranteed to contain `/catalog`. On failure to reach `/catalog` by any means, the function throws (it must not return silently while still on a non-`/catalog` URL). Call sites (`frontend/test/e2e/catalog/*.spec.ts`, 9 files) are unchanged — they continue to call `await navigateToCatalog(page);` followed by their own `expect(page.url()).toContain('/catalog')` (which becomes redundant but harmless once the helper self-verifies; removing that duplicate assertion from the 9 spec files is optional cleanup, not required).

## Dependencies

- Playwright (`@playwright/test`) — existing E2E framework, no version change implied.
- Staging environment `https://heblo.stg.anela.cz` — the fix must be validated there per `docs/testing/playwright-e2e-testing.md`, since there is no sandbox equivalent for this class of timing issue.
- E2E service principal credentials/permissions (`E2E_CLIENT_ID`/`AZURE_CLIENT_ID`, `E2E_CLIENT_SECRET`/`AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID`) and its grant of `products.catalog.read` — see FR-2.
- `frontend/test/e2e/helpers/wait-helpers.ts` (`waitForLoadingComplete`, `waitForPageLoad`) — used as-is; note the existing documented caveat in that file (lines ~98-129) that `waitForLoadingComplete`'s loading-indicator selectors (`[data-loading="true"], .loading, .spinner, [aria-busy="true"]`) don't match anything rendered by `CatalogList.tsx`, so it currently returns immediately without actually waiting for catalog data to load. This is a separate, pre-existing gap (not part of the reported 84-test failure, since those fail before ever reaching the catalog page) but is worth flagging as a related risk: once navigation is fixed, tests may newly expose catalog-data-loading race conditions that this helper doesn't currently guard against. Out of scope for this fix unless it causes new failures during validation.

## Out of Scope

- Fixing any filter/sort/pagination/margins-chart logic *within* the Catalog page itself. This spec only restores the ability to reach `/catalog`; once there, each test's own assertions govern its own pass/fail, and any such failures are separate issues.
- Rewriting `waitForLoadingComplete` to correctly detect `CatalogList.tsx`'s React-Query-driven loading state (documented as a known gap in `wait-helpers.ts`). Only revisit if FR-1 validation surfaces new flakiness traceable to this gap.
- Auditing or fixing the equivalent try/catch-fallback pattern bug in *other* untouched navigation helpers beyond `navigateToCatalog`, unless FR-2 investigation shows they share the exact same defect and are also causing nightly failures. `navigateToTransportBoxes` and `navigateToTransportBoxReceive` already use the correct (fallback-outside-try/catch) pattern and are not known to be broken.
- Backend/API changes to the permissions endpoint (`/me`-equivalent) or to `accessMatrix.generated.ts`/RBAC configuration, unless FR-2 investigation finds the E2E principal is missing `products.catalog.read` (in which case granting it is a config/ops change, not a code change, and should be tracked separately if discovered).
- Any UI/UX changes to `Sidebar.tsx`, the `/catalog` route, or `RequireMenuPath.tsx` — all three were inspected and found correctly implemented.

## Open Questions

None.

## Status: COMPLETE
