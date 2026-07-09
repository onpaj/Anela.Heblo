## Summary

In the nightly E2E regression run **[#191](https://github.com/onpaj/Anela.Heblo/actions/runs/28888238966)** (branch `main`, commit `738a99c`), **29 tests across the `issued-invoices` module failed** with the same root cause: the "Seznam" (grid/list) tab button never becomes clickable.

## Root cause signature

The shared setup switches to the Grid tab after the page loads, but the button never appears (30s timeout):

```
TimeoutError: locator.click: Timeout 30000ms exceeded.
Call log:
  - waiting for locator('button:has-text("Seznam")')

  33 |     // Now switch to Grid tab (should be visible after loading completes)
> 34 |     await gridTab.click();
     at frontend/test/e2e/issued-invoices/filters.spec.ts:34
```

A couple of variants also time out waiting for `h1:has-text("Vydané faktury")` and `button:has-text("Statistiky")`, confirming the Issued Invoices page shell / tab bar isn't rendering (or is stuck loading).

## Affected specs (failing test count)

| Spec | Failures |
|------|---------:|
| issued-invoices/filters.spec.ts | 9 |
| issued-invoices/pagination.spec.ts | 7 |
| issued-invoices/sorting.spec.ts | 7 |
| issued-invoices/status-badges.spec.ts | 4 |
| issued-invoices/navigation.spec.ts | 2 |
| **Total** | **29** |

## Environment

- Workflow: 🎭 E2E Nightly Regression Tests, run #191
- Target: `https://heblo.stg.anela.cz` (staging)
- Screenshots/video artifacts: `e2e-failure-screenshots-all-191`, `e2e-test-results-all-191` on the [run page](https://github.com/onpaj/Anela.Heblo/actions/runs/28888238966).

## Suggested first steps

1. Open the Issued Invoices page on staging and confirm the `Vydané faktury` heading, `Seznam`, and `Statistiky` tabs render.
2. Check whether the page is stuck on a loading spinner (the tab is expected "after loading completes").
3. Inspect the invoices data/list API for errors or long latency exceeding the 30s wait.
