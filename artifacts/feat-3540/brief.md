## Summary

In the nightly E2E regression run [#191](https://github.com/onpaj/Anela.Heblo/actions/runs/28888238966) (branch `main`, commit `738a99c`), 56 tests across the `stock-operations` module failed with the same root cause: the operations table never renders any rows and the empty-state header never appears either.

## Root cause signature

The shared setup waits for either a data row or the "no results" header, and times out — neither appears:

```
Error: expect(locator).toBeVisible() failed
Error: element(s) not found

  - Expect "toBeVisible" with timeout 15000ms
  - waiting for locator('tbody tr').first().or(locator('h3').filter({ hasText: 'Žádné výsledky' }))

  24 |   await expect(
> 26 |   ).toBeVisible({ timeout: 15000 });
```

Because neither `tbody tr` nor the `Žádné výsledky` empty-state renders, the Stock Operations list page is effectively not loading its data. This blocks every downstream test (badges, filters, sorting, retry/accept actions, panel). One test — "should display error state on API failure" — passes, which suggests the page shell loads but the normal data path fails.

## Affected specs (failing test count)

| Spec | Failures |
|------|---------:|
| stock-operations/filters.spec.ts | 18 |
| stock-operations/badges.spec.ts | 7 |
| stock-operations/panel.spec.ts | 6 |
| stock-operations/retry.spec.ts | 6 |
| stock-operations/state-filter.spec.ts | 6 |
| stock-operations/navigation.spec.ts | 4 |
| stock-operations/accept.spec.ts | 3 |
| stock-operations/sorting.spec.ts | 3 |
| stock-operations/source-filter.spec.ts | 3 |
| **Total** | **56** |

## Environment

- Workflow: E2E Nightly Regression Tests, run #191
- Target: `https://heblo.stg.anela.cz` (staging)
- Screenshots/video artifacts: `e2e-failure-screenshots-all-191`, `e2e-test-results-all-191` on the run page.

## Suggested first steps

1. Load the Stock Operations page on staging and inspect the list API call (does it return data / error / hang?).
2. Confirm whether the default filters (State: Active, Source: All) return results in the current staging dataset.
3. Verify the empty-state (`Žádné výsledky`) renders when there are genuinely no rows.
