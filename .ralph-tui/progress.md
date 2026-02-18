# Ralph Progress Log

This file tracks progress across iterations. Agents update this file
after each iteration and it's included in prompts for context.

## Codebase Patterns (Study These First)

### CatalogList loading state
- React Query's `isLoading` flag renders a plain `<div>` with `Loader2` icon (no `data-loading`, `.loading`, `.spinner`, `aria-busy` attributes)
- `waitForLoadingComplete` in wait-helpers.ts will ALWAYS return immediately on catalog page (no selector matches)
- To wait for catalog API, always register `page.waitForResponse` BEFORE triggering the action (not after)

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
