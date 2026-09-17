# Design: Remove dead `getManufacturingSeverityColorClass` / `getManufacturingSeverityDisplayText` exports

## Component Design

**Module:** `frontend/src/api/hooks/useManufacturingStockAnalysis.ts`

**Responsibility before this change:** Re-exports generated API-client types/enums (`GetManufacturingStockAnalysisResponse`, `ManufacturingStockItemDto`, `ManufacturingStockSummaryDto`, `ManufacturingStockSeverity`, `ManufacturingStockSortBy`), re-exports the `getTimePeriodDisplayText` utility, exposes the `useManufacturingStockAnalysis()` React Query hook, and — as unused dead code — two standalone helper functions (`getManufacturingSeverityColorClass`, `getManufacturingSeverityDisplayText`) that map a `ManufacturingStockSeverity` value to a Tailwind color-class string and a Czech display label respectively.

**Responsibility after this change:** Identical, minus the two dead helper functions. The module's contract with its real consumers (`ManufacturingStockAnalysis.tsx`, `ManufactureBatchPlanning.tsx`) is unchanged, since neither consumer imports the deleted symbols.

**Interface/contract:**
- Removed from the public surface of the module: `export const getManufacturingSeverityColorClass: (severity: ManufacturingStockSeverity) => string` and `export const getManufacturingSeverityDisplayText: (severity: ManufacturingStockSeverity) => string`.
- Unchanged: every other export listed above, including the `ManufacturingStockSeverity` enum re-export that the deleted functions consumed (it stays because other files still import it from this module).

No other component or module has a boundary change. `ManufacturingStockAnalysis.tsx` keeps its own private, inline, dark-mode-aware severity-to-color logic exactly as it exists today; this design does not touch that component.

## Data Schemas
Not applicable — no database schema, API request/response shape, or event payload is affected. This change removes two pure, stateless, non-exported-to-backend TypeScript helper functions with no data persistence or transport role.
