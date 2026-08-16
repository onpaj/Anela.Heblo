### task: dark-mode-baleni-statistics-jsx

**Context:** `frontend/src/components/baleni/statistics/BaleniStatistics.tsx` is the container for the packing-statistics screen. It currently has zero `dark:` Tailwind variants, so every white `Panel`/`KpiCard` surface and light-gray text renders illegibly against the app's dark background when Graphite dark mode is active (toggled via `<html class="dark">`, persisted in `localStorage` under key `anela-theme` by `frontend/src/contexts/ThemeContext.tsx`).

This file needs **no JS theme awareness** — Tailwind's `dark:` variant is resolved by the CSS cascade automatically. The precedent is `frontend/src/components/baleni/BaleniHome.tsx`, which already themes an identical `StatCard`/panel pattern with no `useTheme()` import at all, e.g. at `BaleniHome.tsx:45`:
```tsx
<div className="bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-xl p-6 shadow-soft dark:shadow-soft-dark">
```

**Rule (from `docs/design/dark-mode-conversion-guide.md`):** ONLY append `dark:` classes to existing `className` strings. NEVER remove, reorder, or rewrite a light-mode class. NEVER change logic, props, structure, or text. For ternary/conditional `className` strings, add the dark variant inside **each** branch.

All required Tailwind tokens already exist in `frontend/tailwind.config.js` under the `graphite` color scale (`bg`, `surface`, `surface-2`, `hover`, `chrome`, `border`, `border-strong`, `text`, `muted`, `faint`, `accent`, `accent-strong`, `accent-ink`) and `boxShadow.soft-dark` — no Tailwind config changes are needed.

Apply the following edits to `frontend/src/components/baleni/statistics/BaleniStatistics.tsx` exactly (each is a `className` string change only; nothing else on the line changes):

- [ ] **`Panel` wrapper** (currently line 32):
  ```tsx
  // before
  <div className="bg-white border border-border-light rounded-xl p-6 shadow-soft">
  // after
  <div className="bg-white border border-border-light rounded-xl p-6 shadow-soft dark:bg-graphite-surface dark:border-graphite-border dark:shadow-soft-dark">
  ```
- [ ] **`Panel` title** (currently line 34):
  ```tsx
  // before
  <h3 className="text-sm font-semibold text-neutral-slate">{title}</h3>
  // after
  <h3 className="text-sm font-semibold text-neutral-slate dark:text-graphite-text">{title}</h3>
  ```
- [ ] **`Panel` subtitle** (currently line 35):
  ```tsx
  // before
  {subtitle && <p className="text-xs text-neutral-gray mt-1">{subtitle}</p>}
  // after
  {subtitle && <p className="text-xs text-neutral-gray mt-1 dark:text-graphite-muted">{subtitle}</p>}
  ```
- [ ] **`KpiCard` wrapper** (currently line 46):
  ```tsx
  // before
  <div className="bg-white border border-border-light rounded-xl p-5 shadow-soft">
  // after
  <div className="bg-white border border-border-light rounded-xl p-5 shadow-soft dark:bg-graphite-surface dark:border-graphite-border dark:shadow-soft-dark">
  ```
- [ ] **`KpiCard` label** (currently line 47):
  ```tsx
  // before
  <p className="text-sm text-neutral-gray mb-2">{label}</p>
  // after
  <p className="text-sm text-neutral-gray mb-2 dark:text-graphite-muted">{label}</p>
  ```
- [ ] **`KpiCard` loading pulse** (currently line 49):
  ```tsx
  // before
  <div className="h-8 w-20 bg-secondary-blue-pale rounded animate-pulse" />
  // after
  <div className="h-8 w-20 bg-secondary-blue-pale rounded animate-pulse dark:bg-graphite-surface-2" />
  ```
- [ ] **`KpiCard` value** (currently line 51):
  ```tsx
  // before
  <p className="text-3xl font-bold text-primary-blue">{value}</p>
  // after
  <p className="text-3xl font-bold text-primary-blue dark:text-graphite-accent">{value}</p>
  ```
- [ ] **Error banner** (currently line 81):
  ```tsx
  // before
  <div className="bg-red-50 border border-red-200 rounded-lg p-4">
  // after
  <div className="bg-red-50 border border-red-200 rounded-lg p-4 dark:bg-red-950/30 dark:border-red-900/50">
  ```
- [ ] **Error icon** (currently line 83):
  ```tsx
  // before
  <AlertCircle className="h-5 w-5 text-red-600" />
  // after
  <AlertCircle className="h-5 w-5 text-red-600 dark:text-red-400" />
  ```
- [ ] **Error heading** (currently line 85):
  ```tsx
  // before
  <h3 className="text-sm font-medium text-red-800">Chyba při načítání statistik</h3>
  // after
  <h3 className="text-sm font-medium text-red-800 dark:text-red-400">Chyba při načítání statistik</h3>
  ```
- [ ] **Error body** (currently line 86):
  ```tsx
  // before
  <p className="text-sm text-red-700 mt-1">
  // after
  <p className="text-sm text-red-700 mt-1 dark:text-red-400">
  ```
- [ ] **Retry button** (currently line 93):
  ```tsx
  // before
  className="mt-3 px-3 py-1 text-sm bg-red-100 text-red-800 rounded hover:bg-red-200 transition-colors"
  // after
  className="mt-3 px-3 py-1 text-sm bg-red-100 text-red-800 rounded hover:bg-red-200 transition-colors dark:bg-red-900/30 dark:text-red-300 dark:hover:bg-red-900/50"
  ```
- [ ] **H1 heading** (currently line 110):
  ```tsx
  // before
  <h1 className="text-2xl font-bold text-neutral-slate flex items-center gap-3">
  // after
  <h1 className="text-2xl font-bold text-neutral-slate flex items-center gap-3 dark:text-graphite-text">
  ```
- [ ] **H1 icon** (currently line 111):
  ```tsx
  // before
  <BarChart3 className="h-6 w-6 text-primary-blue" />
  // after
  <BarChart3 className="h-6 w-6 text-primary-blue dark:text-graphite-accent" />
  ```
- [ ] **Date-range subtitle** (currently line 115):
  ```tsx
  // before
  <p className="text-sm text-neutral-gray mt-1">
  // after
  <p className="text-sm text-neutral-gray mt-1 dark:text-graphite-muted">
  ```
- [ ] **Range-preset button ternary** (currently lines 126–130) — both branches must get matching dark siblings:
  ```tsx
  // before
  className={`px-3 py-2 rounded-lg border text-sm transition-colors ${
    rangeDays === preset.days
      ? "bg-secondary-blue-pale border-primary-blue text-primary-blue"
      : "bg-white border-border-light text-neutral-gray hover:bg-secondary-blue-pale"
  }`}
  // after
  className={`px-3 py-2 rounded-lg border text-sm transition-colors ${
    rangeDays === preset.days
      ? "bg-secondary-blue-pale border-primary-blue text-primary-blue dark:bg-graphite-accent/10 dark:border-graphite-accent dark:text-graphite-accent"
      : "bg-white border-border-light text-neutral-gray hover:bg-secondary-blue-pale dark:bg-graphite-surface dark:border-graphite-border dark:text-graphite-muted dark:hover:bg-white/5"
  }`}
  ```
- [ ] **Refresh button** (currently line 138):
  ```tsx
  // before
  className="flex items-center gap-2 px-3 py-2 rounded-lg border border-border-light text-neutral-gray hover:bg-secondary-blue-pale transition-colors disabled:opacity-50"
  // after
  className="flex items-center gap-2 px-3 py-2 rounded-lg border border-border-light text-neutral-gray hover:bg-secondary-blue-pale transition-colors disabled:opacity-50 dark:border-graphite-border dark:text-graphite-muted dark:hover:bg-white/5"
  ```
- [ ] **Full-page loading box** (currently line 173):
  ```tsx
  // before
  <div className="flex items-center justify-center h-72 bg-white border border-border-light rounded-xl shadow-soft">
  // after
  <div className="flex items-center justify-center h-72 bg-white border border-border-light rounded-xl shadow-soft dark:bg-graphite-surface dark:border-graphite-border dark:shadow-soft-dark">
  ```
- [ ] **Loading spinner icon** (currently line 175):
  ```tsx
  // before
  <RefreshCw className="h-8 w-8 text-primary-blue animate-spin mx-auto mb-4" />
  // after
  <RefreshCw className="h-8 w-8 text-primary-blue animate-spin mx-auto mb-4 dark:text-graphite-accent" />
  ```
- [ ] **Loading label** (currently line 176):
  ```tsx
  // before
  <p className="text-neutral-gray">Načítání dat...</p>
  // after
  <p className="text-neutral-gray dark:text-graphite-muted">Načítání dat...</p>
  ```

Do not touch anything else in the file — no import changes, no prop changes, no structural JSX changes. `BaleniStatistics.tsx` does NOT import `useTheme` or `GRAPHITE` (that's only needed in the two Recharts-touching files, tasks 2 and 3).

**Verification steps:**

- [ ] Run `cd frontend && grep -c "dark:" src/components/baleni/statistics/BaleniStatistics.tsx` — expect the count to be ≥ 20 (one per edit above, several lines have 2+ dark classes).
- [ ] Run the existing test file unmodified and confirm it still passes:
  ```bash
  cd frontend && CI=true npm test -- --testPathPattern=BaleniStatistics
  ```
  Expected: all tests in `src/components/baleni/statistics/__tests__/BaleniStatistics.test.tsx` pass (it asserts on text content and structure, not class strings, so this change cannot break it).
- [ ] Run the build to confirm no TypeScript/JSX errors:
  ```bash
  cd frontend && npm run build
  ```
  Expected: build succeeds with no new errors.
- [ ] Run the linter:
  ```bash
  cd frontend && npm run lint
  ```
  Expected: no new lint errors introduced by this file.
- [ ] Manual/visual spot-check (if a running dev instance is available): toggle to Graphite dark mode via `ThemeToggle` and confirm the KPI cards, panels, header, range-preset buttons (both active and inactive states), refresh button, and the error/loading states all render with visible, non-white surfaces and legible text. This is optional if no dev server is available in this environment, but the class-string diff above must be double-checked by eye against the mapping table before committing.
- [ ] Commit:
  ```bash
  git add frontend/src/components/baleni/statistics/BaleniStatistics.tsx
  git commit -m "Add Graphite dark-mode classes to BaleniStatistics.tsx"
  ```

---
