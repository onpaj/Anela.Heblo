## Module
Purchase

## Finding
`frontend/src/api/hooks/usePurchaseStockAnalysis.ts` exports two helper functions at lines 72–109 that are never imported anywhere in the codebase:

```typescript
export const getSeverityColorClass = (severity: StockSeverity | undefined): string => { ... }  // line 72
export const getSeverityDisplayText = (severity: StockSeverity | undefined): string => { ... }  // line 92
```

A project-wide search for `getSeverityColorClass` and `getSeverityDisplayText` finds only the definitions — no import or invocation site. Both are dead exports.

Additionally, `getSeverityColorClass` returns hardcoded light-mode-only Tailwind classes (e.g. `"text-red-600 bg-red-50"`) with no `dark:` variants, which would violate ADR-006 if the function were activated:

```typescript
case StockSeverity.Critical:
    return "text-red-600 bg-red-50";   // no dark:text-... or dark:bg-...
case StockSeverity.Low:
    return "text-orange-600 bg-orange-50";
// ...
```

The `PurchaseStockAnalysis.tsx` page, which is the natural consumer, imports `formatNumber` and `formatCurrency` from the same hook but does not import these helpers — it handles severity display with its own inline logic.

## Why it matters
Dead exports violate YAGNI and silently grow the public surface of the hook, making it harder to understand what callers actually use. `getSeverityColorClass` in particular, if someone copies it as a quick fix, would produce broken dark-mode rendering (breaking ADR-006 on a high-visibility status column).

## Suggested fix
Remove both functions from `usePurchaseStockAnalysis.ts`. If severity display helpers are genuinely needed in the future, add them when there is a concrete consumer, co-locate them in the component that uses them, and follow ADR-006 by including `dark:` variants (e.g. `"text-red-600 bg-red-50 dark:text-red-400 dark:bg-red-900/30"`).

---
_Filed by daily arch-review routine on 2026-07-10._
