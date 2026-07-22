# [coverage-gap] Purchase/LowStockEfficiencyTile: stock efficiency filter business rule untested

## Module / File
`backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
The tile counts items matching `item.StockEfficiencyPercentage < 20 && item.IsConfigured`. Neither condition is exercised by any test.

Specific untested paths:
- An item at exactly 20% efficiency should NOT be counted (strict `<`).
- An item below 20% efficiency but with `IsConfigured = false` should NOT be counted.
- The `!response.Success` branch returns `{status: "error"}` rather than an item count.

## Why it matters
If `IsConfigured` is accidentally removed from the filter, all unconfigured materials at 0% efficiency would inflate the dashboard counter, triggering false alarms on the purchasing dashboard. The boundary at 20% (strict vs. inclusive) is similarly silent to regress.

## Suggested approach
Two to three unit tests with a mocked `IMediator` (≈ low effort):
1. Mock returns items at various efficiencies and configured/unconfigured states — verify count matches only those meeting both conditions.
2. Item at exactly 20% — verify it is excluded.
3. Mock returns `Success = false` — verify `{status: "error"}` response shape.

---
_Filed by weekly coverage-gap routine on 2026-07-20. Based on CI run #29525794843 (bba537b141de1dba71a2c6853c4ff3f7e96153b2)._
