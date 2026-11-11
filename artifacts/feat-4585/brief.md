## Module / File
`frontend/src/pages/StockOperationsPage.tsx`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
`isOperationStuck` checks Submitted > 5 minutes and Pending > 10 minutes — wrong constants would show incorrect "stuck" warnings to warehouse operators. `canRetry` allows retry for Failed/Submitted/Pending but not Completed — no test pins this contract. The URL query-parameter initialization effect reads 9 parameters and sets paired input+filter states; a parsing bug (e.g. `parseInt` on an invalid page value) would silently produce NaN-based pagination.

## Why it matters
A wrong stuck threshold causes operators to prematurely or belatedly retry operations, potentially causing Shoptet duplicate orders (as warned in the UI itself). A broken `canRetry` gate exposes or hides the retry button in incorrect states.

## Suggested approach
Unit tests for `isOperationStuck` with controlled `Date.now` for each state × threshold variant; unit tests for `canRetry` for all four states. ~S effort (pure functions, no rendering required).

---
_Filed by weekly coverage-gap routine on 2026-10-26. Based on CI run #39780303564 (cad5f9218f4b3982c6605f346e1549562e175d67)._
