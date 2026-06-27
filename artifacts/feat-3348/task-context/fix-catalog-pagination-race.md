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

