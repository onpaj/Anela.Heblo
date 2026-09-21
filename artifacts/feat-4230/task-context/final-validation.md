### task: final-validation

**Files:**
- No file changes in this task — verification only.

This task runs the full validation suite required before declaring the change done, per project convention (`CLAUDE.md` → Validation before completion: FE `npm run build` + `npm run lint`, all touched tests pass). No backend files are touched by this feature, so BE validation (`dotnet build`/`dotnet format`) is not applicable and may be skipped.

- [ ] **Step 1: Run the full frontend build**

Run: `cd frontend && npm run build`
Expected: exit code 0, no TypeScript errors.

- [ ] **Step 2: Run lint**

Run: `cd frontend && npm run lint`
Expected: exit code 0. If lint reports a pre-existing issue unrelated to this change (e.g. the pre-existing `datasets: any[]` typing), do not fix it — out of scope per spec (`Out of Scope` section); only fix lint issues on lines this plan's tasks touched.

- [ ] **Step 3: Run the full frontend test suite (not just this one file), to confirm no unrelated regression**

Run: `cd frontend && CI=true npm test -- --watchAll=false`
Expected: all tests pass, including every test in `ProductMarginSummary.test.tsx`.

- [ ] **Step 4: Manually re-read the final `chartData`/`tableData`/`productColorMap`/`buildChartDatasets` code in `frontend/src/components/pages/ProductMarginSummary.tsx` and confirm against spec FR-1–FR-5**

Checklist (confirm each, no code changes expected unless a gap is found):
- FR-1: exactly one place reads `PRODUCT_COLORS[index % PRODUCT_COLORS.length]` (inside `productColorMap`). ✅ if true.
- FR-2: neither `chartData` nor `tableData` independently sorts/indexes `data.topProducts` for color purposes anymore. ✅ if true.
- FR-3: `DEFAULT_COLOR` and `OTHER_COLOR` both still exist as separate named constants, both still `#9CA3AF`. ✅ if true.
- FR-4: no single `useMemo` body in the file is anywhere near the old 101-line `chartData` length. ✅ if true.
- FR-5: single-product existing tests still pass unchanged (confirmed in Step 3).

If any checklist item fails, fix it before considering the plan complete — do not commit a partial fix as done.

- [ ] **Step 5: Commit (only if Step 4 required a fix; otherwise this task has nothing new to commit)**

```bash
git status --short frontend/
# If clean, no commit needed for this task.
# If Step 4 required a fix:
git add frontend/src/components/pages/ProductMarginSummary.tsx
git commit -m "fix: address final-validation checklist gap"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (single canonical color-mapping source) → `build-product-color-map` task.
- FR-2 (`chartData`/`tableData` consume the shared map) → `extract-chart-dataset-builder` + `wire-table-data-to-color-map` tasks.
- FR-3 (`DEFAULT_COLOR`/`OTHER_COLOR` reconciliation — keep both, no merge) → explicitly preserved in `build-product-color-map` (Step 1 keeps both constants) and `extract-chart-dataset-builder` (helper still uses `OTHER_COLOR` for the "Other" dataset, `DEFAULT_COLOR` for per-product fallback).
- FR-4 (reduce `chartData` below ~50 lines) → `extract-chart-dataset-builder` task (dataset construction moved to `buildChartDatasets`).
- FR-5 (no visual/behavioral regression) → verified by existing single-product tests staying green throughout (`extract-chart-dataset-builder` Step 4, `wire-table-data-to-color-map` Step 2, `final-validation` Step 3), plus the explicit FR-5 checklist item in `final-validation` Step 4.
- NFR-1 (performance: single sort, no redundant passes) → arch-review Decision 3, implemented via `otherKeys`/`topProductEntries` derived from `productColorMap` in `buildChartDatasets`, no second independent `[...data.topProducts].sort(by totalMargin)` call anywhere.
- NFR-2 (maintainability, ~50-line guideline) → covered by FR-4 task.
- NFR-3 (no API/contract changes) → no task touches backend, `contracts/`, or DTOs — confirmed by file list across all tasks (frontend-only).
- Architecture review Decision 1 (no new file/hook) → all tasks modify only `ProductMarginSummary.tsx` and its co-located test file.
- Architecture review Decision 2 (plain helper, not a second memo) → `buildChartDatasets` is a plain function, not wrapped in its own `useMemo`.
- Architecture review Decision 3 (`=== DEFAULT_COLOR` membership test, not re-sort) → implemented exactly this way in `buildChartDatasets`.
- Architecture review Specification Amendment 3 (extend test coverage for multi-product color consistency) → `add-regression-test` task.

No spec or arch-review requirement was found without a corresponding task.

**2. Placeholder scan:** No "TBD"/"TODO"/"implement later" strings. Every code step shows complete, copy-pasteable code, not a description of what to write. Every test step shows the actual assertions, not "add appropriate assertions."

**3. Type consistency:** `productColorMap: Map<string, string>` is defined once in `build-product-color-map` and referenced with that exact name and type in `extract-chart-dataset-builder` and `wire-table-data-to-color-map`. `buildChartDatasets` is defined with that exact name/signature in `extract-chart-dataset-builder` and called with that exact name in the same task's `chartData` body (not referenced elsewhere). `TOP_CHART_PRODUCTS`, `DEFAULT_COLOR`, `OTHER_COLOR`, `PRODUCT_COLORS` are used consistently by their existing names throughout — no renamed constants introduced without updating every reference in this plan.
