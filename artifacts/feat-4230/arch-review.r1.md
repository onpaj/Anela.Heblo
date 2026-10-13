# Architecture Review: Unify product color-mapping logic in ProductMarginSummary.tsx

## Skip Design: true

This is a pure internal refactor of one existing component's presentation logic. No new UI component, screen, layout, or visual design decision is introduced — the rendered chart and table are required (FR-5) to look pixel-identical to today for the existing data shape. There is nothing for a designer phase to contribute; the planner should proceed straight to task planning using this review plus the spec.

## Architectural Fit Assessment

This change is frontend-only, confined to `frontend/src/components/pages/ProductMarginSummary.tsx` (600 lines) and, if extraction is chosen, one new co-located file next to it. It touches no backend module, no `contracts/`, no DTOs, no API surface — so the module-boundary rules in `docs/architecture/development_guidelines.md` (DTOs live in `contracts/`, module independence, MediatR-only business logic, etc.) do not apply here; they govern the backend Vertical Slice architecture, which this change never crosses into.

I verified the codebase already has an established, directly analogous precedent for exactly this kind of extraction: `frontend/src/components/pages/financial-overview/utils.ts` is a co-located, page-local module of plain exported pure functions (`formatCurrency`, `getPeriodLabel`, types) pulled out of a sibling page's component tree, sitting next to `FinancialComparisonChart.tsx`, `FinancialDataTable.tsx`, etc. in a `financial-overview/` folder. `ProductMarginSummary.tsx` currently has no such sibling folder — it's a single flat file directly under `components/pages/`. This review's guidance follows that same established pattern rather than introducing a new one (e.g. a global/shared hooks module), consistent with `docs/architecture/filesystem.md`'s `components/pages/` co-located-file convention and the "surgical changes" rule in `CLAUDE.md` (extract only what's needed, don't invent new shared infrastructure for a single consumer).

I also confirmed via grep that no other file imports `PRODUCT_COLORS`, `DEFAULT_COLOR`, `OTHER_COLOR`, or anything from `ProductMarginSummary.tsx` — this is a fully local, single-consumer change. No cross-component or cross-module coordination is required.

The existing test file `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx` (212 lines) covers loading/error/empty states, chart rendering, and one dropdown interaction, but has only a single `topProducts` entry in its `mockData` — it does not currently exercise multi-product color consistency between chart and table. This is a real coverage gap relative to FR-5's "no regression" requirement and relative to verifying the bug this change fixes; see Prerequisites / Specification Amendments below.

## Proposed Architecture

### Component Overview

```
ProductMarginSummary.tsx (component)
│
├── productColorMap  ─── useMemo, depends on [data?.topProducts]
│     reads: PRODUCT_COLORS, DEFAULT_COLOR
│     produces: Map<groupKey, colorHex>
│
├── chartData ─── useMemo, depends on [data, productColorMap]
│     delegates dataset construction to a plain helper (buildChartDatasets)
│     reads: productColorMap.get(groupKey) ?? DEFAULT_COLOR
│
└── tableData ─── useMemo, depends on [data?.topProducts, productColorMap]
      reads: productColorMap.get(groupKey) ?? DEFAULT_COLOR
```

Both `chartData` and `tableData` become pure *consumers* of `productColorMap`; neither computes color independently. `productColorMap` is the single place `PRODUCT_COLORS[index % ...]` is evaluated.

### Key Design Decisions

#### Decision 1: Where does `productColorMap` live — extracted hook/module, or inline `useMemo`?

**Options considered:**
1. Extract to a shared/global hook (e.g. `frontend/src/hooks/useProductColorMap.ts`), as the brief's suggested fix names it.
2. Extract to a co-located local module next to the component (matching the `financial-overview/utils.ts` precedent), e.g. `frontend/src/components/pages/product-margin-summary/colorMap.ts` (plain function, not a hook) with the component itself calling `useMemo` around it, or a co-located `useProductColorMap.ts` hook file.
3. Keep it as a `useMemo` directly inside `ProductMarginSummary.tsx`, no new file.

**Chosen approach:** Option 3 — keep `productColorMap` as a `useMemo` directly inside the component. Do **not** create a new file or folder for this change.

**Rationale:** There is exactly one consumer today (confirmed by grep — nothing else imports from this file), so extracting a shared/global hook (Option 1) would be speculative reuse the spec explicitly says to avoid ("extract only if the architecture review calls for it, e.g. for testability" — Data Model/API section of `spec.r1.md`). Option 2 (co-located file, matching the `financial-overview/` precedent) is defensible but not necessary here: `financial-overview/utils.ts` was extracted because that folder already has *multiple* sibling components sharing `formatCurrency`/`getPeriodLabel`/types; `ProductMarginSummary.tsx` has no siblings and gains nothing from a new folder for a single `useMemo`. Keeping `productColorMap` inline is the smallest change that satisfies FR-1–FR-4, is trivially testable via the existing component test file (assert computed colors from rendered chart/table DOM — see Prerequisites), and follows "surgical changes": don't create new module structure the task doesn't require. If a second consumer of this color-mapping logic ever appears, promote it to a co-located module at that point — not speculatively now.

#### Decision 2: How to split the oversized `chartData` `useMemo` (FR-4)

**Options considered:**
1. A second `useMemo` for dataset construction (`chartDatasets = useMemo(() => buildDatasets(...), [...])`).
2. A plain, non-memoized helper function (module-level, above or below the component) that `chartData`'s `useMemo` body calls.

**Chosen approach:** Option 2 — a plain module-level helper function (e.g. `buildChartDatasets(monthlyData, topProductsForChart, otherProductsForChart, productColorMap, productDisplayNames): any[]`), called from inside the single `chartData` `useMemo`.

**Rationale:** The spec (FR-4) explicitly allows either and prefers the plain-helper option "unless there's a clear performance reason for a separate memo." There is no such reason here: dataset construction only runs when `chartData`'s own dependency (`data`, `productColorMap`) changes, so a second independent `useMemo` would only add a redundant dependency array to keep in sync, not additional memoization value (both memos would always invalidate together, since dataset construction depends on the same `data`/`productColorMap` inputs `chartData` itself depends on). A plain helper avoids that duplicate dependency-array maintenance burden and is simpler to unit-test directly (pure function, no React runtime needed) if implementation adds a Decision-3-driven unit test around it.

#### Decision 3: How to preserve the top-15/"Other" split without re-deriving it a third time

**Options considered:**
1. Compute `sortedByTotalMargin` once (module-level or memo-internal), feed it into both `productColorMap` construction and the top-15/"Other" split inside `chartData`.
2. Derive the top-15/"Other" split from `productColorMap` itself (e.g. `productColorMap.get(key) !== DEFAULT_COLOR` ⇒ "in top 15").

**Chosen approach:** Option 1 — a single `sortedByTotalMargin` array, computed once inside the `productColorMap` `useMemo`, and **also** computed (or passed through) for `chartData`'s "Other" grouping. Since `productColorMap` and `chartData` are two separate memos with two different dependency arrays (`[data?.topProducts]` vs `[data, productColorMap]`), the cleanest way to avoid a second independent sort is for `chartData` to derive its top-15/other split directly from `productColorMap`'s keys via Option 2's equality check (`productColorMap.get(p.groupKey) === DEFAULT_COLOR` ⇒ belongs in "Other"), rather than re-sorting `data.topProducts` a second time. This satisfies FR-2's requirement ("exactly one sort-by-totalMargin computation feeding both") without needing to lift `sortedByTotalMargin` to module scope or thread it through an extra memo. Flagging as a Specification Amendment below since the spec left this as implementer's choice — this review picks the equality-check approach as the concrete, unambiguous instruction for the planner/developer.

**Edge case to preserve:** a product with `totalMargin` tied for the 15th/16th position, or a product colored `DEFAULT_COLOR` legitimately because it's beyond top-15 — the equality check (`=== DEFAULT_COLOR`) is safe here because `DEFAULT_COLOR` is never assigned to a top-15 product (top-15 always gets a `PRODUCT_COLORS[...]` value, which is guaranteed disjoint from `DEFAULT_COLOR`'s literal `#9CA3AF` — confirmed `PRODUCT_COLORS` array does not contain `#9CA3AF`). Implementer should add a one-line comment noting this invariant so a future edit to the palette doesn't silently break the equality check.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. All changes stay inside `frontend/src/components/pages/ProductMarginSummary.tsx`.

### Interfaces and Contracts
No exported/public interface changes — this is entirely internal to the component. The internal shape to implement:

```ts
// Inside the component, replacing the current color logic in both chartData and tableData:

const productColorMap = useMemo(() => {
  const map = new Map<string, string>();
  if (!data?.topProducts) return map;
  const sorted = [...data.topProducts].sort(
    (a, b) => (b.totalMargin ?? 0) - (a.totalMargin ?? 0),
  );
  sorted.forEach((p, i) => {
    if (p.groupKey) {
      map.set(
        p.groupKey,
        i < TOP_CHART_PRODUCTS
          ? PRODUCT_COLORS[i % PRODUCT_COLORS.length]
          : DEFAULT_COLOR,
      );
    }
  });
  return map;
}, [data?.topProducts]);
```

`TOP_CHART_PRODUCTS` (currently a local `const` inside the old `chartData` body, value 15) must be hoisted to module scope (alongside `PRODUCT_COLORS`/`DEFAULT_COLOR`/`OTHER_COLOR`) so both `productColorMap` and `chartData`'s "Other" split can reference the same named constant.

`chartData` then becomes, in outline:
```ts
const chartData = useMemo(() => {
  if (!data?.monthlyData || !data?.topProducts) return null;
  const labels = data.monthlyData.map((m) => m.monthDisplay);
  const productDisplayNames = /* unchanged existing logic */;
  const datasets = buildChartDatasets(
    data.monthlyData,
    data.topProducts,
    productColorMap,
    productDisplayNames,
  );
  return { labels, datasets };
}, [data, productColorMap]);
```

`buildChartDatasets` (module-level plain function, placed above the component, near `PRODUCT_COLORS`) contains the current lines ~108–165 logic (building the "Other" dataset entry, using `productColorMap.get(key) === DEFAULT_COLOR` to determine "Other" membership instead of re-slicing `sortedByTotalMargin`, and building each top-product dataset entry using `productColorMap.get(productKey) ?? DEFAULT_COLOR`).

`tableData` becomes:
```ts
const tableData = useMemo(() => {
  if (!data?.topProducts) return [];
  return data.topProducts
    .map((product) => ({
      groupKey: product.groupKey || "",
      displayName: product.displayName || "",
      colorCode: productColorMap.get(product.groupKey || "") ?? DEFAULT_COLOR,
      totalMargin: product.totalMargin || 0,
      rank: product.rank || 0,
      m0Amount: product.m0Amount || 0,
      // ...unchanged remaining fields...
    }))
    .sort((a, b) => a.rank - b.rank);
}, [data?.topProducts, productColorMap]);
```

### Data Flow
`data.topProducts` (from `useProductMarginSummaryQuery`) → `productColorMap` (single sort + color assignment) → consumed by both `chartData` (chart datasets + legend colors) and `tableData` (table row color dots). `data.monthlyData` continues to flow only into `chartData` (for per-month segment values), unchanged. No change to how `data` itself is fetched or to `chartOptions`/tooltip logic.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Silent behavior change if `chartData`'s dependency array (`[data, productColorMap]`) is wrong (e.g. omitting `productColorMap`), causing stale colors after a data refetch | Medium | Explicitly include `productColorMap` in both `chartData` and `tableData` dependency arrays, as shown above; this is the most likely subtle bug in this refactor — call out in code review checklist |
| `TOP_CHART_PRODUCTS` hoist accidentally changes its value or introduces a second definition | Low | Single module-level `const TOP_CHART_PRODUCTS = 15;` next to `PRODUCT_COLORS`; delete the old local declaration inside `chartData` |
| Test coverage gap: existing tests use a single-product `mockData.topProducts`, so a color-mismatch regression between chart and table would not be caught | Medium | See Specification Amendments — recommend planner include a task to extend `mockData` to 2+ products (one in top-15, one beyond) and assert `chart` and table row color values match, both before (documenting current risk, optional) and after the refactor |
| `buildChartDatasets` extraction accidentally changes stacking order (top products must be added *after* "Other" so highest-margin products render at top of stack — currently relies on iteration order of `topProductKeys.sort(...)`) | Medium | Preserve exact current ordering: "Other" dataset pushed first, then top products pushed in ascending-`totalMargin` order (lowest first) so Chart.js stacks highest margin at top, exactly as today; do not reorder unless a test explicitly verifies stacking order is unaffected |

## Specification Amendments

1. **Decision 3 resolves the spec's open implementer-choice**: use `productColorMap.get(groupKey) === DEFAULT_COLOR` as the top-15/"Other" membership test inside `chartData`'s dataset construction, rather than re-deriving `sortedByTotalMargin` a second time for that purpose. This is now the mandated approach (not merely one option among several) for the planner/developer to follow.
2. **`TOP_CHART_PRODUCTS` must be hoisted to module scope** (next to `PRODUCT_COLORS`) rather than staying a local constant inside a `useMemo`, since it is now needed both by `productColorMap` and by `buildChartDatasets`. The spec's suggested snippet already implies this constant is shared; this review makes the hoist an explicit requirement.
3. **Test-coverage recommendation** (not a hard requirement, but strongly recommended — see Prerequisites): extend `ProductMarginSummary.test.tsx`'s `mockData.topProducts` to include at least 2 products (e.g. one that lands in the top 15, one that would land beyond it if the array had >15 entries — or, more practically given real palette size, just verify 2+ products get distinct, matching-between-chart-and-table colors) so the fix (and any future regression) is actually verifiable by the test suite. The planner should include this as a task alongside the refactor itself, since "no visual regression" (FR-5) cannot currently be automatically verified for the multi-product case.

## Prerequisites
None — no migrations, no config, no infrastructure changes needed before implementation can start. Implementation can begin immediately against the current `main`/feature branch state of `ProductMarginSummary.tsx`.
