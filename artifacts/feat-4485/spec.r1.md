# Specification: Remove decimal.MaxValue sentinel from PackingMaterial.CalculateForecastedDays

## Summary
`PackingMaterial.CalculateForecastedDays` returns `decimal.MaxValue` to mean "no consumption history", forcing every caller to know and test for that sentinel. Change the method to return `decimal?` (null = no forecast possible) and remove both sentinel checks from the application handlers. Externally visible behaviour (API/DTO values) must not change.

## Background
Two handlers duplicate the same translation: `GetPackingMaterialsListHandler` (lines ~45-49) and `UpdatePackingMaterialQuantityHandler` (lines ~46-47). Both do `forecastedDays == decimal.MaxValue ? null : (decimal?)Math.Round(forecastedDays, 1)`. This leaks a domain implementation detail into the Application layer. `PackingMaterialDto.ForecastedDays` is already `decimal?` and `PackingMaterialMapper.ToDto(material, decimal? forecastedDays)` already accepts nullable.

Note: the issue's suggested snippet `var displayForecast = material.CalculateForecastedDays(recentLogs);` drops the existing `Math.Round(..., 1)`. Rounding to one decimal is display behaviour that is currently observable via the API and tested (list handler test expects 10m; rounding must be preserved).

## Functional Requirements

### FR-1: Domain method returns nullable
`PackingMaterial.CalculateForecastedDays(List<PackingMaterialLog>)` returns `decimal?`.
**Acceptance criteria:**
- Returns `0` when `CurrentQuantity <= 0` (unchanged, takes precedence over the no-history case).
- Returns `null` when there are no log entries with `ChangeAmount < 0` and non-zero magnitude.
- Otherwise returns `CurrentQuantity / average(|negative changes|)` unrounded (unchanged).
- `decimal.MaxValue` is no longer returned or referenced anywhere in the PackingMaterials feature.

### FR-2: Handlers have no sentinel knowledge
**Acceptance criteria:**
- `GetPackingMaterialsListHandler` and `UpdatePackingMaterialQuantityHandler` contain no `decimal.MaxValue` comparison.
- Both still round non-null forecasts to 1 decimal place (`Math.Round(x, 1)`) before passing to `PackingMaterialMapper.ToDto`; null passes through as null.
- The duplicated rounding is expressed once-per-site as a simple null-propagating expression (e.g. `forecast.HasValue ? Math.Round(forecast.Value, 1) : (decimal?)null`) or a single shared helper; no sentinel logic remains.

### FR-3: Behaviour preserved
**Acceptance criteria:**
- API responses for list and update-quantity are identical to before for all inputs (null when no history, 0 when quantity <= 0, rounded to 1 dp otherwise).
- The debug log counters `withForecast` / `withoutForecast` in the list handler keep the same semantics.

### FR-4: Tests
**Acceptance criteria:**
- Add domain unit tests for `PackingMaterial.CalculateForecastedDays`: null for empty logs, null for logs with only positive/zero changes, 0 for zero quantity (even with no history), correct value with negative logs.
- Existing `GetPackingMaterialsListHandlerTests` and `PackingMaterialMapperTests` continue to pass unchanged.
- Add/confirm a test for `UpdatePackingMaterialQuantityHandler` returning null forecast with no history and rounded forecast with history (if none exists).

## Non-Functional Requirements

### NFR-1: Performance
No change; same computation.

### NFR-2: Security
No change.

### NFR-3: Compatibility
No change to OpenAPI contract: `PackingMaterialDto.ForecastedDays` is already `decimal?`; generated TS/C# clients must not change.

## Data Model
No entity or database changes; no migration.

## API / Interface Design
Only the internal domain signature changes: `decimal CalculateForecastedDays(...)` -> `decimal? CalculateForecastedDays(...)`. Only two callers exist (the two handlers). No HTTP surface change.

## Dependencies
None. Process doc `docs/processes/module-packing-materials.md` (and possibly `calc-packing-material-consumption.md`) should be checked; if they describe the sentinel, update, else bump `verified_at` per CLAUDE.md process-doc rule and run `python3 scripts/process-docs/check.py index`.

## Out of Scope
- Changing the forecasting formula, window (one month), or rounding precision.
- Refactoring other handler logic or moving rounding into the domain.
- Frontend changes.

## Open Questions
None.

## Status: COMPLETE
