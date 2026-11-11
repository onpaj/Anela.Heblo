# Design: FinancialSummaryCards

## UX/UI Design
No visual change. The row keeps its current look:
```
[ icon  Label        ] x6 (xl) / x4 (lg, no stock) / 2 columns on small screens
[       12 345 Kc    ]
```
Each card: `bg-white dark:bg-graphite-surface overflow-hidden shadow dark:shadow-soft-dark rounded-lg` > `p-3` > `flex items-center` > icon (`h-5 w-5`) + `ml-3 w-0 flex-1` > `<dl>` with `<dt class="text-xs font-medium text-gray-500 dark:text-graphite-muted truncate">` and `<dd class="text-sm font-medium {valueClass}">`.
Grid: `grid grid-cols-2 md:grid-cols-2 ${includeStockData ? "xl:grid-cols-6" : "lg:grid-cols-4"} gap-4 mb-6`.

## Component Design
`FinancialSummaryCards` (exported) builds `CardConfig[]`:
```ts
interface CardConfig { key: string; icon: LucideIcon; iconClass: string; label: string; value: number; valueClass: string }
```
Base cards: income (neutral), expenses (neutral), totalBalance (>=0 emerald else red), averageMonthlyBalance (>=0 blue else red). When `includeStockData && summary.stockSummary`, append stockValueChange (purple/orange, `v && v >= 0`, value `v || 0`) and balanceWithStock (emerald/red, same pattern). Neutral value class: `text-gray-900 dark:text-graphite-text`.
`SummaryCard` (private) renders one config. Both live in `FinancialSummaryCards.tsx`; `formatCurrency` from `./utils`.
`FinancialOverview.tsx` renders `<FinancialSummaryCards summary={data.summary} includeStockData={includeStockData} />` inside the existing `viewMode === "timeline" && data?.summary` guard.

## Data Schemas
No API or DB change. Props: `{ summary: FinancialSummaryDto; includeStockData: boolean }`; fields used: `totalIncome`, `totalExpenses`, `totalBalance`, `averageMonthlyBalance`, `stockSummary.totalStockValueChange`, `stockSummary.totalBalanceWithStock`.
