# Implementation: fix-classification-pagination-wait

## What was implemented

Replaced a fixed `page.waitForTimeout(2000)` in the `beforeEach` block of the invoice classification history E2E spec with a deterministic `page.waitForSelector(...)` call. The new wait resolves as soon as either the data table (`table`) or the empty-state message (`"Nebyly nalezeny žádné záznamy"`) is visible, with a 15 s timeout. This eliminates the race condition where the 2 s timeout was too short on staging, causing the `pagination functionality` test to time out when calling `nextButton.isDisabled()`.

## Files created/modified

- `frontend/test/e2e/core/invoice-classification-history.spec.ts` — replaced 3-line `waitForTimeout(2000)` block with a 3-line `waitForSelector` call in `beforeEach`

## How to verify

1. Run the E2E suite against staging: `./scripts/run-playwright-tests.sh`
2. Confirm the `pagination functionality` and `filters functionality` tests both pass without timing out.
3. Verify the `beforeEach` no longer contains `waitForTimeout(2000)`.

## Status
DONE
