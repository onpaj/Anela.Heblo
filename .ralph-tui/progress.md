# Ralph Progress Log

This file tracks progress across iterations. Agents update this file
after each iteration and it's included in prompts for context.

## Codebase Patterns (Study These First)

### CatalogList loading state
- React Query's `isLoading` flag renders a plain `<div>` with `Loader2` icon (no `data-loading`, `.loading`, `.spinner`, `aria-busy` attributes)
- `waitForLoadingComplete` in wait-helpers.ts will ALWAYS return immediately on catalog page (no selector matches)
- To wait for catalog API, always register `page.waitForResponse` BEFORE triggering the action (not after)

### Tailwind responsive class selectors in E2E tests
- Tailwind responsive classes like `md:text-sm` contain a colon and are NOT matched by CSS selector `.text-sm`
- Example: `h3.text-base.md:text-sm.font-medium` → `.text-sm.font-medium` will NEVER match
- Always prefer stable semantic class names (e.g. `tile-title`) over Tailwind utility classes for E2E test selectors
- TileHeader uses `tile-title` as the stable class for tile header `h3` elements

### Promise.all pattern for filter helpers (catalog-test-helpers.ts)
- All `apply*Filter` helpers now use: `const responsePromise = page.waitForResponse(...)` BEFORE `await Promise.all([responsePromise, action()])`
- `waitForTableUpdate` no longer calls `waitForSearchResults` — it uses `waitForLoadingComplete` + DOM polling (`waitForFunction`) instead
- This avoids the race condition where `page.waitForResponse` is registered AFTER the response already arrived
- For direct actions outside helpers (e.g., raw button clicks followed by `waitForTableUpdate`), DOM polling in `waitForTableUpdate` picks up the state change without needing the response promise

## 2026-02-18 - US-003
- Fixed failing test 'should display AutoShow tiles automatically' in `frontend/test/e2e/core/dashboard.spec.ts`
- Files changed: `frontend/test/e2e/core/dashboard.spec.ts`
- **What was implemented:**
  - Replaced `.text-sm.font-medium` locator with `.tile-title` in the BackgroundTaskStatusTile assertion
  - Added an explanatory comment documenting the tileId naming convention (TileExtensions.GetTileId: lowercase + remove "tile" suffix)
  - Added a comment explaining why `.text-sm.font-medium` fails (TileHeader uses `text-base md:text-sm font-medium` - the `md:text-sm` is a different class string from `text-sm`)
- **Root cause:** `TileHeader.tsx` uses class `text-base md:text-sm font-medium tile-title ...` on the title h3. The CSS selector `.text-sm` requires a class literally named `text-sm`, but the element only has `md:text-sm` (a Tailwind responsive prefix class). So the selector never matched. The element does have the stable custom class `tile-title` which is the correct selector.
- **Learnings:**
  - Tailwind responsive classes (e.g. `md:text-sm`) have colons in their class name and are NOT matched by `.text-sm` CSS selectors
  - When TileHeader was updated to use responsive sizing (`text-base md:text-sm`), any test using `.text-sm.font-medium` broke silently
  - Always use stable semantic class names (like `tile-title`) for test selectors, not Tailwind utility classes that may be responsive variants

---

## 2026-02-18 - US-002
- Fixed race condition in catalog filter helpers so all 8 failing tests in `catalog/text-search-filters.spec.ts` pass
- Files changed: `frontend/test/e2e/helpers/catalog-test-helpers.ts`
- **What was implemented:**
  - Added `CATALOG_API_ENDPOINT` constant
  - Removed `waitForSearchResults` import (no longer needed in this file)
  - Updated all `apply*Filter` helpers (`applyProductNameFilter`, `applyProductNameFilterWithEnter`, `applyProductCodeFilter`, `applyProductCodeFilterWithEnter`, `selectProductType`, `clearAllFilters`) to register `page.waitForResponse` BEFORE triggering the action via `Promise.all([responsePromise, action()])`
  - Updated `waitForTableUpdate` to NOT call `waitForSearchResults`/`waitForResponse` (would race on already-consumed response). Instead uses `waitForLoadingComplete` + `waitForFunction` DOM polling to detect when table/empty-state appears
- **Learnings:**
  - `Promise.all([waitForResponse(...), click()])` is the correct pattern — the Promise must be in-flight BEFORE the click fires the request
  - `waitForFunction` DOM polling is a reliable fallback when there's no loading indicator and `waitForResponse` would be too late
  - Pre-existing lint/typecheck errors exist in unrelated files (e2e-auth-helper.ts, test files in src/) — not introduced by this change

---

## 2026-02-18 - US-001
- Analyzed root cause of catalog text-search-filter timeouts in all 8 tests in `catalog/text-search-filters.spec.ts`
- Files changed: `frontend/test/e2e/helpers/wait-helpers.ts` (added root cause comment block to `waitForSearchResults`)
- **Learnings:**
  - **Race condition confirmed**: `page.waitForResponse` is registered AFTER the API response already arrived
  - **Why it returns immediately**: `waitForLoadingComplete` checks for selectors `[data-loading="true"], .loading, .spinner, [aria-busy="true"]` - none of these are in CatalogList.tsx. The React Query loading state renders a plain `<div>` with `Loader2` icon (no matching attribute/class). Count is always 0, function returns immediately.
  - **Full code path**: `applyProductNameFilter` → fill → click → `waitForLoadingComplete` (returns immediately, no matching selector) → 500ms timeout → returns. Test then calls `waitForTableUpdate` → `waitForSearchResults` → `page.waitForResponse('/api/catalog')` which times out because the response already came in.
  - **Fix direction for US-002**: Register `page.waitForResponse` BEFORE triggering the filter action (Promise must be set up before the response arrives, not after)
---
