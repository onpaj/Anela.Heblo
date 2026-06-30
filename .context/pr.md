# PR Context

- **PR**: #3361 — #3348: fix(catalog): resolve pagination-reset inconsistency on text filter
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3361
- **Branch**: `feature/3348-Fix-Catalog-Resolve-Pagination-Reset-Inconsistency` → `main`
- **State**: open
- **Author**: onpaj
- **Changes**: +869 / -72 across 15 files
- **Absorbed**: already up to date with `main`, failing CI fixed, all 285 test suites passing

## Description

Closes #3348

Applying a text filter (name or code) on the catalog page failed to reset pagination to page 1. When a user was on page 2 and applied a filter, the URL retained `page=2` instead of dropping it, causing the API to return page 2 of the (now smaller) filtered result set — often an empty page.

The root cause was a race condition between two concurrent state updates in `handleApplyFilters`: `setPageNumber(1)` and `setSearchParams(params)`. A redundant `useEffect` depending on `[pageNumber, searchParams, setSearchParams]` fired with stale `pageNumber=2` after `searchParams` updated first, re-writing `page=2` to the URL before `pageNumber=1` resolved.

Removed the "Sync URL parameter with page number state" `useEffect` (previously lines 257–275 of `CatalogList.tsx`). This was the only effect that wrote the `page` URL parameter based on `pageNumber` state changes.

## What pr-autoabsorb fixed

`ThemeContext.test.tsx` (2 tests) was failing because `setupTests.ts` globally mocks
`ThemeContext` for all test suites, replacing `ThemeProvider` with a passthrough and
`useTheme()` with a static `"light"` return. ThemeContext.test.tsx needs the real
implementation to assert storage persistence and toggle behaviour. Fixed by adding
`jest.unmock("../ThemeContext")` at the top of that test file.

Result: 285/285 frontend test suites, 2339 tests passing.
