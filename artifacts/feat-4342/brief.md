## Module / File
`frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx`

## Coverage
Line coverage: 1.3% (filter threshold: 60%)

## What's not tested
`mapMarginDataToMonthlyArrays` contains a year-boundary correction (lines 97–101): when `currentMonth - monthsBack` results in 0 or a negative month number, the code decrements the year by 1 and adds 12 to the month. Neither the boundary case (running in January, looking 12 months back crosses into the previous year) nor the mid-year case is tested.

Additionally:
- The current-month exclusion filter (line 71) — records whose date falls in the current month are skipped — is untested.
- The fallback to `0` for missing keys (lines 104–111) — months with no margin data are filled with zeros — is untested.

## Why it matters
If the year rollback is wrong (e.g., off-by-one on the month adjustment), margin data for January through the year boundary would appear in the wrong slot on the chart. For a cosmetics business where seasonal margin trends matter, a silently misaligned chart would mislead decisions about product pricing.

## Suggested approach
Unit tests for `mapMarginDataToMonthlyArrays` extracted or tested via the component:
- Running in January (current month = 1): verify that months 1–12 of the previous year appear in the correct slots
- Running mid-year: verify normal case without year crossover
- A record in the current month is excluded from the chart arrays
- A month with no matching record gets `0` in all metric arrays

Effort: ~1–2 hours using Jest + React Testing Library or pure function extraction.

---
_Filed by weekly coverage-gap routine on 2026-09-28. Based on CI run #35977921040 (22bb3b8ff6194bdd055cb438d08b7c6633a85221)._
