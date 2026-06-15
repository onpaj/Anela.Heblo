## Module / File
`backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrderExtensions.cs`

## Coverage
Line coverage: 49% (filter threshold: 60%)

## What's not tested
`GetDefaultExpiration(DateTime manufactureDate, int months)` contains two non-obvious computations:
- **ISO week number (`GetWeekNumber`)**: Thursday-anchored ISO 8601 week — the Sunday-to-7 correction, year-boundary edge cases (week 52 vs 53, first week of new year), and the `Math.Ceiling` calculation are unchecked
- **Expiration date arithmetic**: adds `months` then goes to `+2` additional months minus 1 day — this double-AddMonths-minus-one pattern produces the last day of the expiration month plus one full month; the exact intended result is non-obvious and could silently shift by a month if changed

`GetDefaultLot` format `wwyyyyMM`:
- Week number zero-padding (`D2`) for single-digit weeks is untested
- Month zero-padding for months 1–9 is untested

## Why it matters
Lot numbers are printed on physical cosmetic products and used for regulatory traceability. Expiration dates are stored in ERP and on product labels. A wrong ISO week calculation produces a mismatched lot number; a one-month shift in expiration silently mislabels product shelf life. These are stamped on manufactured product batches and cannot easily be corrected after production.

## Suggested approach
Pure unit tests — no mocks or DI needed:
1. GetDefaultLot: date in week 1 (e.g. 2024-01-01), mid-year (week 22), week 52, Sunday (boundary check)
2. GetDefaultExpiration: verify the result is the last day of the target month for typical months, February (non-leap), February (leap year), year-boundary dates
3. SetDefaultExpiration/SetDefaultLot: verify they write back to the correct property on each entity type
Effort: ~1 hour

---
_Filed by weekly coverage-gap routine on 2026-06-14. Based on CI run #27416879267 (3a6b7f99ee715caf82fc0efa17de5a5ede7b46fb)._