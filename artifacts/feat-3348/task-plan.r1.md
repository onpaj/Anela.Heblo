# Task Plan: fix(catalog): resolve pagination-reset inconsistency on text filter

### task: fix-catalog-pagination-race

## Goal

Remove the redundant `useEffect` at lines 257–275 of `CatalogList.tsx` that fires with stale `pageNumber` state after `handleApplyFilters` calls `setSearchParams`, causing `page=2` to be re-written to the URL after a filter resets it to 1. The effect at lines 188–206 already owns state→URL sync via its `pageNumber` dependency and handles this correctly.

## Files

- `frontend/src/components/pages/CatalogList.tsx`

## Changes

Delete lines 257–275 in their entirety (the comment line and the entire `useEffect` block):

```
  // Sync URL parameter with page number state
  React.useEffect(() => {
    const currentPage = searchParams.get("page");
    const currentPageNumber = currentPage ? parseInt(currentPage, 10) : 1;

    if (pageNumber === 1) {
      // Remove page parameter when on page 1
      if (currentPage) {
        const newParams = new URLSearchParams(searchParams);
        newParams.delete("page");
        setSearchParams(newParams); // Creates history entry when cleaning up page param
      }
    } else if (currentPageNumber !== pageNumber) {
      // Update page parameter when page number changes
      const newParams = new URLSearchParams(searchParams);
      newParams.set("page", pageNumber.toString());
      setSearchParams(newParams); // Creates history entry for page number change
    }
  }, [pageNumber, searchParams, setSearchParams]);
```

After removal, line 256 (the closing `}, [searchParams, pageNumber]);` of the preceding effect) should be followed directly by the blank line and `// Modal handlers` comment (previously line 277).

## Verification

1. `cd frontend && npm run build` — must complete with no TypeScript errors.
2. `npm run lint` — must pass with no new lint errors.
3. Manual smoke test against staging: navigate to the catalog, go to page 2, apply a text filter — URL must show no `page` parameter (i.e. reset to page 1).

---

### task: update-e2e-test-assertions

## Goal

Update three locations across two E2E test files to reflect the now-correct behavior: applying a filter resets to page 1, and navigating to page 2 with a filter stays on page 2. Remove all "KNOWN BUG" / "KNOWN APPLICATION BUG" comment blocks that documented the old broken behavior.

## Files

- `frontend/test/e2e/catalog/combined-filters.spec.ts`
- `frontend/test/e2e/catalog/pagination-with-filters.spec.ts`

## Changes

### combined-filters.spec.ts — test "should reset page to 1 when any filter changes" (around line 149)

**Change 1** — Replace the buggy assertion and its surrounding comment block.

Old text (lines 164–170):
```typescript
    // KNOWN APPLICATION BUG: Applying filters does not reset pagination to page 1
    // Expected: Page should reset to 1 when filters change
    // Actual: Page remains on page 2 after applying filter
    // This is the same pagination reset bug documented in catalog-pagination-with-filters.spec.ts
    // TODO: Change expectation to toBe(1) when backend pagination reset is implemented
    currentPage = await getCurrentPageFromUrl(page);
    expect(currentPage).toBe(2); // Should be 1 when bug is fixed
```

New text:
```typescript
    currentPage = await getCurrentPageFromUrl(page);
    expect(currentPage).toBe(1);
```

**Change 2** — Update the comment that follows (line 172), changing "despite pagination bug" to neutral language:

Old text:
```typescript
    // Verify filter was still applied correctly despite pagination bug
```

New text:
```typescript
    // Verify filter was still applied correctly
```

---

### pagination-with-filters.spec.ts — Change 1: "page stays on 2 with filter" test (around line 113)

Remove the "KNOWN BUG" comment block (lines 113–124) and the `if`-block that logs the confirmed bug (lines 127–130), and update the assertion at line 137.

Old text (lines 113–137):
```typescript
      // KNOWN BUG: Pagination with filters causes automatic reset to page 1
      // Root cause: After clicking page 2, React Query refetches data which triggers
      // useEffect hooks that reset pagination state, removing the page parameter from URL.
      // Expected behavior: Should stay on page 2 with filter applied
      // Actual behavior: Automatically resets to page 1 after brief page 2 API call
      //
      // Workaround test: Verify that:
      // 1. API call for page 2 was made (confirms click worked)
      // 2. Filter is maintained (confirms filter state is preserved)
      // 3. Page resets to 1 (documents the bug)
      //
      // TODO(e2e-map): Once bug is fixed in application, change assertion to expect(currentPage).toBe(2)

      // For now, document that the bug exists
      if (currentPage === 1 && firstRowCode === newFirstRowCode) {
        console.log('⚠️  APPLICATION BUG CONFIRMED: Pagination was reset to page 1 after clicking page 2');
        console.log('   This is a known issue that needs to be fixed in the React component');
      }

      // Verify filter is still applied despite the pagination bug
      await validateFilteredResults(page, { productType: 'Produkt' }, { maxRowsToCheck: 5 });

      // Current buggy behavior: Page resets to 1
      // When bug is fixed, this line should be changed to: expect(currentPage).toBe(2);
      expect(currentPage).toBe(1); // TODO(e2e-map): Change to 2 when pagination race condition is fixed
```

New text:
```typescript
      // Verify filter is still applied
      await validateFilteredResults(page, { productType: 'Produkt' }, { maxRowsToCheck: 5 });

      expect(currentPage).toBe(2);
```

Also update the `console.log` on the line immediately after the assertion:

Old text:
```typescript
      console.log('✅ Test passed (with documented pagination reset bug)');
```

New text:
```typescript
      console.log('✅ Test passed');
```

---

### pagination-with-filters.spec.ts — Change 2: "page size change resets to page 1" test (around line 292)

Old text (lines 292–312):
```typescript
    // KNOWN BUG: Page size changes don't reset pagination to page 1
    // Root cause: Same pagination state management issue as other tests
    // Expected behavior: Changing page size should reset to page 1 to show beginning of results
    // Actual behavior: Stays on page 2 after changing page size (confusing UX)
    //
    // This is related to the React Query refetch and state management bug
    // where pagination state isn't properly reset on filter/page size changes.
    //
    // TODO(e2e-map): Once bug is fixed, change assertion to expect(currentPage).toBe(1)

    const currentPage = await getCurrentPageNumber(page);
    console.log(`📍 Current page after page size change: ${currentPage}`);

    // Current buggy behavior: Page stays on 2
    // When bug is fixed, this line should be changed to: expect(currentPage).toBe(1);
    expect(currentPage).toBe(2); // TODO(e2e-map): Change to 1 when pagination reset bug is fixed

    // Verify filter is still applied despite the pagination bug
    await validateFilteredResults(page, { productType: 'Produkt' }, { maxRowsToCheck: 5 });

    console.log('✅ Test passed (with documented pagination reset bug - stays on page 2)');
```

New text:
```typescript
    const currentPage = await getCurrentPageNumber(page);
    console.log(`📍 Current page after page size change: ${currentPage}`);

    expect(currentPage).toBe(1);

    // Verify filter is still applied
    await validateFilteredResults(page, { productType: 'Produkt' }, { maxRowsToCheck: 5 });

    console.log('✅ Test passed');
```

## Verification

1. `cd frontend && npm run build` — TypeScript must compile cleanly.
2. `npm run lint` — no lint errors.
3. Run E2E suite for the catalog module: `./scripts/run-playwright-tests.sh --grep "catalog"` against staging.
   - "should reset page to 1 when any filter changes" must pass with `toBe(1)`.
   - "should stay on page 2 after applying filter and clicking page 2" must pass with `toBe(2)`.
   - "page size change resets to page 1" must pass with `toBe(1)`.
