## Summary

In the nightly E2E regression run **[#191](https://github.com/onpaj/Anela.Heblo/actions/runs/28888238966)** (branch `main`, commit `738a99c`), **2 tests in `manufacturing/batch-planning-error-handling.spec.ts` failed** because the batch planning calculator / modal never renders.

## Root cause signature

```
Error: expect(locator).toBeVisible() failed / element(s) not found
  - waiting for locator('h1, h2').filter({ hasText: /Plánovač|Planning|Dávek|Kalkulačka/i }).first()
  - waiting for locator('[role="combobox"]').first()
```

The planning heading and the product/combobox control never appear, so the "fixed products exceed volume" error-handling flow can't be exercised.

## Failing tests

- `batch-planning-error-handling.spec.ts:25` — should handle fixed products exceed volume with toaster and visual indicators
- `batch-planning-error-handling.spec.ts:248` — should allow user to correct fixed quantities and recalculate successfully

Note: the broader `manufacturing/batch-planning-workflow.spec.ts` and other manufacturing specs passed, so this is scoped to the error-handling entry point rather than the whole batch-planning feature.

## Environment

- Workflow: 🎭 E2E Nightly Regression Tests, run #191
- Target: `https://heblo.stg.anela.cz` (staging)
- Screenshots/video artifacts: `e2e-failure-screenshots-all-191`, `e2e-test-results-all-191` on the [run page](https://github.com/onpaj/Anela.Heblo/actions/runs/28888238966).

## Suggested first steps

1. Reproduce the batch-planning error-handling scenario on staging and confirm the planner modal (heading + combobox) opens.
2. Compare the setup against the passing `batch-planning-workflow.spec.ts` to see why this entry point doesn't reach the calculator.
