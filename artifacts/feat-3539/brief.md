## Summary

In the nightly E2E regression run **[#191](https://github.com/onpaj/Anela.Heblo/actions/runs/28888238966)** (branch `main`, commit `738a99c`), **all 84 tests across the `catalog` module failed** with the same root cause: navigation to the Catalog page never actually lands on `/catalog`.

## Root cause signature

The shared `beforeEach` calls `navigateToCatalog(page)` and then asserts the URL, which fails:

```
expect(received).toContain(expected)

Expected substring: "/catalog"
Received string:    "https://heblo.stg.anela.cz/?e2e=true"

  16 |     console.log('🧭 Navigating to catalog page...');
  17 |     await navigateToCatalog(page);
> 18 |     expect(page.url()).toContain('/catalog');
  19 |     console.log('✅ On catalog page:', page.url());
     at frontend/test/e2e/catalog/*.spec.ts:18
```

The app stays on the root URL (`/?e2e=true`) — the catalog route is never reached. This blocks every downstream catalog test (filters, sorting, pagination, etc.). Likely candidates: broken sidebar navigation to Catalog, a routing regression, or the initial catalog load hanging so the redirect/route change never completes.

## Affected specs (failing test count)

| Spec | Failures |
|------|---------:|
| catalog/filter-edge-cases.spec.ts | 17 |
| catalog/text-search-filters.spec.ts | 16 |
| catalog/combined-filters.spec.ts | 13 |
| catalog/pagination-with-filters.spec.ts | 13 |
| catalog/clear-filters.spec.ts | 10 |
| catalog/sorting-with-filters.spec.ts | 10 |
| catalog/product-type-filter.spec.ts | 3 |
| catalog/margins-chart.spec.ts | 1 |
| catalog/ui.spec.ts | 1 |
| **Total** | **84** |

## Environment

- Workflow: 🎭 E2E Nightly Regression Tests, run #191
- Target: `https://heblo.stg.anela.cz` (staging)
- Screenshots/video artifacts: `e2e-failure-screenshots-all-191`, `e2e-test-results-all-191` on the [run page](https://github.com/onpaj/Anela.Heblo/actions/runs/28888238966).

## Suggested first steps

1. Manually verify Catalog navigation on staging (does the sidebar link route to `/catalog`?).
2. Check whether the initial catalog data load is failing/hanging and preventing the route from settling.
3. Once navigation works, re-run the catalog E2E suite to confirm downstream assertions pass.
