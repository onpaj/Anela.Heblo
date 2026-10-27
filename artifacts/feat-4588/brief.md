## Module / File
`frontend/src/components/pages/ManufacturingStockAnalysis.tsx`

## Coverage
Line coverage: 47.4% (filter threshold: 60%)

## What's not tested
`readSalesMultiplierCookie` parses a cookie value, rejects it if outside [0.1, 3.0] or NaN, and silently returns 1.0 as default. The boundary values (0.1 and 3.0) themselves are untested — a `<=` vs `<` mistake would reject a valid user setting. The `getCookie` helper relies on `document.cookie.split("; ")` to find a match, which would break if a cookie value contains `; ` — edge case untested.

## Why it matters
Silently resetting a user's custom multiplier to 1.0 causes manufacturing stock coverage to be calculated at 100% of historical sales rather than the intended scaling, leading to wrong reorder signals without any visible error.

## Suggested approach
Unit tests for `readSalesMultiplierCookie` with: null cookie, valid mid-range value, exact boundary values 0.1 and 3.0, out-of-range values (0.09, 3.01), NaN string. ~XS effort (pure function).

---
_Filed by weekly coverage-gap routine on 2026-10-26. Based on CI run #39780303564 (cad5f9218f4b3982c6605f346e1549562e175d67)._
