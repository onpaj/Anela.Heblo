# Design: Fix Catalog Page Navigation E2E Regression (navigateToCatalog)

## Component Design

**`navigateToCatalog(page)`** — `frontend/test/e2e/helpers/e2e-auth-helper.ts` (lines 234-260)

Sole component in scope. No new files, no product-code changes. The fix restructures this one function to match the existing, working pattern used by its siblings `navigateToTransportBoxes` and `navigateToTransportBoxReceive` in the same file:

- `navigateToApp(page)` — called first, unchanged; mounts the app shell but does not wait for the permissions fetch.
- **UI path** (inside `try`): locate "Produkty" with `isVisible({ timeout: 5000 })` (was 2000ms), click it, wait for load, locate "Katalog" with the same 5000ms timeout, click it, wait for load. Only if `page.url()` is then confirmed to contain `/catalog` does the function `return` early. A click that lands elsewhere (e.g. a `RequireMenuPath` redirect to `/`) is treated as a miss, not a success.
- **`catch` block**: logs the exception (matching sibling style) and falls through — it no longer performs navigation itself.
- **Fallback** (unconditional, after `try/catch`): reached whenever the UI path didn't already return — i.e. on an `isVisible` timeout-miss, a UI click that didn't route to `/catalog`, or a thrown exception alike. Navigates directly via `page.goto(\`${baseUrl}/catalog\`)` using the same `baseUrl` resolution already in the current code (`PLAYWRIGHT_FRONTEND_URL` → `PLAYWRIGHT_BASE_URL` → staging default), then waits for load.
- **Self-verification** (new): after the fallback, throws a descriptive `Error` (naming that both the UI path and the goto fallback were attempted, plus the final `page.url()`) if the URL still does not contain `/catalog`. This replaces the current silent-return behavior with a fast, diagnosable failure at the helper call site.

No other helper in `e2e-auth-helper.ts` is touched. No changes to `Sidebar.tsx`, `App.tsx`, `RequireMenuPath.tsx`, or `PermissionsContext.tsx` — all were inspected and confirmed correctly wired; this is scoped entirely to the test helper.

## Data Schemas

Not applicable — no persistence, API, or event payloads involved. The relevant contract is the helper function's signature and behavior:

```ts
export async function navigateToCatalog(page: any): Promise<void>
```

**Pre-conditions:** `page` is an authenticated Playwright page (post-`navigateToApp`/E2E session setup); the E2E principal is assumed to hold `products.catalog.read` (verified out-of-band per spec FR-2, not by this function).

**Post-conditions:**
- On successful return: `page.url()` contains `/catalog`, reached via either the UI click path or the direct `goto` fallback.
- On failure to reach `/catalog` by either means: the function throws an `Error` describing what was attempted (UI navigation vs. fallback goto) and the final URL, instead of returning silently.

**Unchanged for callers:** the 9 spec files in `frontend/test/e2e/catalog/*.spec.ts` continue to call `await navigateToCatalog(page);` followed by their own `expect(page.url()).toContain('/catalog')`, which becomes redundant but harmless once the helper self-verifies.
