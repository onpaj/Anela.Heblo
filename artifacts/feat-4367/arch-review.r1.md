# Architecture Review: FinancialSummaryCards extraction

## Skip Design: false
A new UI component is introduced, though it is visually identical to the current output. A short design note on component contract is warranted.

## Architectural Fit Assessment
Fully aligned. `frontend/src/components/pages/financial-overview/` already holds the page's presentational sub-components (`FinancialDataCards`, `FinancialFilters`, ...), each a named-export `React.FC` with a props interface and a test under `__tests__/`. The summary block is the sole inline holdout. No backend, API client or routing impact.

## Proposed Architecture

### Component Overview
```
FinancialOverview (page: state, queries, chart data)
 |- FinancialFilters
 |- FinancialSummaryCards   <- NEW (summary, includeStockData)
 |- FinancialChart / FinancialDataTable / FinancialDataCards ...
```

### Key Design Decisions

#### Decision 1: Config array vs. small card subcomponent
**Options considered:** (a) only a config array mapped inline; (b) private `SummaryCard` subcomponent plus config array.
**Chosen approach:** (b), both in the one file. `SummaryCard` takes `{ icon: LucideIcon, iconClass, label, value, valueClass }`; `FinancialSummaryCards` builds the array and maps it. `SummaryCard` is not exported.
**Rationale:** Matches `LabeledRow` in `FinancialDataCards.tsx`; keeps card markup in one place.

#### Decision 2: Colour logic
**Chosen approach:** Keep class strings as full literals in the config (Tailwind JIT needs complete class names; do not build them by interpolation of colour names). Use a small local helper only to choose between two literal strings.
**Rationale:** Avoids purge breakage; preserves exact current output.

#### Decision 3: Preserve falsy semantics
Stock values use `x && x >= 0` today (undefined/0 -> negative colour, formatted as 0). Keep it byte-for-byte; this is a refactor, not a bug fix. Mention in a code comment only if useful.

#### Decision 4: Prop type
Use `FinancialSummaryDto` exported from `api/hooks/useFinancialOverview` (already re-exported there), not the generated client directly.

## Implementation Guidance

### Directory / Module Structure
- New: `frontend/src/components/pages/financial-overview/FinancialSummaryCards.tsx`
- New: `frontend/src/components/pages/financial-overview/__tests__/FinancialSummaryCards.test.tsx`
- Edit: `frontend/src/components/pages/FinancialOverview.tsx` (replace block, import component, drop unused lucide imports `TrendingUp`, `TrendingDown`, `Calendar`, `Package`, `BarChart3`; keep `DollarSign`, `AlertTriangle`, `ChevronDown`).

### Interfaces and Contracts
```ts
interface FinancialSummaryCardsProps { summary: FinancialSummaryDto; includeStockData: boolean }
export const FinancialSummaryCards: React.FC<FinancialSummaryCardsProps>
```
The page keeps the `viewMode === "timeline" && data?.summary` guard; the component assumes `summary` is defined. Import `formatCurrency` from `./utils`.

### Data Flow
`useFinancialOverviewQuery` -> `data.summary` -> props -> config array -> cards. No state inside the component.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Subtle class/markup drift | Low | Copy class strings verbatim; tests assert key classes |
| Tailwind classes lost via interpolation | Low | Literal strings only |
| Unused import lint failure | Low | Run `npm run lint` and `npm run build` |

## Specification Amendments
None. (Line numbers in the brief are stale; the block is the `{/* Summary Cards */}` section.)

## Prerequisites
None. Per `docs/architecture/filesystem.md` conventions, no process-doc change is needed (no sync/calculation changes).
