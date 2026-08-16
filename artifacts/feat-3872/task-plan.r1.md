# Dark-mode support for Packing Statistics (Baleni) — Implementation Plan

**Goal:** Add Graphite dark-mode styling to `BaleniStatistics.tsx`, `PackingCharts.tsx`, and `PackingHourHeatmap.tsx` (the `frontend/src/components/baleni/statistics/` subtree) so the packing-statistics screen is legible in dark mode, with zero changes to logic, props, data flow, or test files.

**Architecture:** Pure frontend, presentation-only retrofit of an existing, already-proven pattern. `BaleniStatistics.tsx` needs only additive Tailwind `dark:` utility classes (CSS-cascade driven via `.dark` on `<html>`, no JS theme awareness — matches `BaleniHome.tsx`). `PackingCharts.tsx` and `PackingHourHeatmap.tsx` need runtime theme resolution for Recharts SVG props and inline `style` because those are not Tailwind classes — both files add `useTheme()` from `frontend/src/contexts/ThemeContext.tsx` and reuse the existing `GRAPHITE` token object from `frontend/src/components/common/reactSelectDarkStyles.ts`, mirroring the established precedent in `frontend/src/components/charts/BankStatementImportChart.tsx`.

**Tech Stack:** React 18 + TypeScript, Tailwind CSS (`darkMode: 'class'`), Recharts, existing `ThemeContext`/`GRAPHITE` token infrastructure (no new libraries, no new files, no backend/API changes).

## File Map

| Action | Path |
|--------|------|
| Modify | `frontend/src/components/baleni/statistics/BaleniStatistics.tsx` |
| Modify | `frontend/src/components/baleni/statistics/PackingCharts.tsx` |
| Modify | `frontend/src/components/baleni/statistics/PackingHourHeatmap.tsx` |

No new files. No files under `__tests__/` are touched (per `docs/design/dark-mode-conversion-guide.md` rule 7 — tests must pass unmodified).

---

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

### task: dark-mode-packing-charts-recharts

**Context:** `frontend/src/components/baleni/statistics/PackingCharts.tsx` exports four Recharts-based chart components (`ThroughputChart`, `CarrierMixChart`, `PackerLeaderboard`, `PackagesPerOrderChart`) plus a shared `EmptyState`. Recharts `stroke`/`fill` are plain SVG attributes, not Tailwind classes, so they need runtime theme resolution — not `dark:` classes. The precedent is `frontend/src/components/charts/BankStatementImportChart.tsx`, which does exactly this (`BankStatementImportChart.tsx:44-53`):

```tsx
const { theme } = useTheme();
const isDark = theme === 'dark';
const colors = {
  grid: isDark ? GRAPHITE.border : '#f0f0f0',
  axis: isDark ? GRAPHITE.muted : '#6b7280',
  ...
};
```

`GRAPHITE` is exported from `frontend/src/components/common/reactSelectDarkStyles.ts` as:
```ts
export const GRAPHITE = {
  surface: "#202327",
  surface2: "#272A30",
  hover: "#2E323A",
  border: "#2D3138",
  borderStrong: "#3C424B",
  text: "#E6E8EC",
  muted: "#9AA0AA",
  faint: "#6A707A",
  accent: "#38BDF8",
  accentInk: "#08171F",
} as const;
```
Import it as `import { GRAPHITE } from "../../common/reactSelectDarkStyles";` and `useTheme` as `import { useTheme } from "../../../contexts/ThemeContext";` — this matches the relative-depth pattern already used by sibling `baleni/*` files (`statistics/` is 3 levels below `src/`).

**Contrast decisions already verified for this task (do not re-litigate — apply as specified):**
- `CARRIER_COLORS` (`["#2563eb", "#0ea5e9", "#14b8a6", "#f59e0b", "#a855f7", "#ec4899", "#64748b"]`) and `OTHER_COLOR` (`"#64748b"`) all clear a 3:1 contrast ratio against `graphite-surface` (`#202327`, the panel background in dark mode) — the lowest is `#2563eb` at ≈3.12:1, the rest are 3.4–7.5:1. **Do not change `CARRIER_COLORS`, `OTHER_COLOR`, `sliceColor`, or `buildCarrierSlices`** — leave them exactly as they are.
- `#93c5fd` (`ThroughputChart`'s secondary `orderCount` bar) and `#0ea5e9` (`PackagesPerOrderChart`'s only bar) both clear 3:1 against `#202327` (≈8.9:1 and ≈5.8:1 respectively) — **leave both unchanged**, no dark variant needed.
- `#2563eb` used as the **primary** bar fill in `ThroughputChart` (`packageCount`, line 106) and `PackerLeaderboard` (`orderCount`, line 175) is swapped to `GRAPHITE.accent` in dark mode per the spec, for consistency with `text-primary-blue → dark:text-graphite-accent` used everywhere else in this module (not purely a contrast fix — it's the established accent-color convention).

Apply the following edits to `frontend/src/components/baleni/statistics/PackingCharts.tsx`:

- [ ] **Add imports** at the top of the file, after the existing imports (after line 22, before line 24 `const CARRIER_COLORS = ...`):
  ```tsx
  import { useTheme } from "../../../contexts/ThemeContext";
  import { GRAPHITE } from "../../common/reactSelectDarkStyles";
  ```
- [ ] **`EmptyState`** (currently lines 25–27):
  ```tsx
  // before
  const EmptyState: React.FC = () => (
    <p className="text-sm text-neutral-gray italic">Žádná data k zobrazení.</p>
  );
  // after
  const EmptyState: React.FC = () => (
    <p className="text-sm text-neutral-gray italic dark:text-graphite-muted">Žádná data k zobrazení.</p>
  );
  ```
- [ ] **`ThroughputChart`** (currently lines 83–112) — add theme resolution and use it for grid/axis/primary-bar/tooltip:
  ```tsx
  // before
  export const ThroughputChart: React.FC<{ data: DailyThroughput[] }> = ({ data }) => {
    if (data.length === 0) return <EmptyState />;
    const chartData = data.map((d) => ({
      ...d,
      label: format(parseISO(d.date), "dd.MM.", { locale: cs }),
    }));
    return (
      <div className="h-72 w-full">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={chartData} margin={{ top: 10, right: 20, left: 0, bottom: 10 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
            <XAxis dataKey="label" tick={{ fontSize: 11 }} stroke="#6b7280" />
            <YAxis tick={{ fontSize: 11 }} stroke="#6b7280" allowDecimals={false} />
            <Tooltip
              formatter={(value, name) => [
                value,
                name === "packageCount" ? "Balíků" : "Objednávek",
              ]}
              labelFormatter={(label) => `Den ${label}`}
            />
            <Legend
              formatter={(value) => (value === "packageCount" ? "Balíků" : "Objednávek")}
            />
            <Bar dataKey="packageCount" fill="#2563eb" radius={[2, 2, 0, 0]} />
            <Bar dataKey="orderCount" fill="#93c5fd" radius={[2, 2, 0, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    );
  };
  // after
  export const ThroughputChart: React.FC<{ data: DailyThroughput[] }> = ({ data }) => {
    const { theme } = useTheme();
    const isDark = theme === "dark";
    if (data.length === 0) return <EmptyState />;
    const chartData = data.map((d) => ({
      ...d,
      label: format(parseISO(d.date), "dd.MM.", { locale: cs }),
    }));
    return (
      <div className="h-72 w-full">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={chartData} margin={{ top: 10, right: 20, left: 0, bottom: 10 }}>
            <CartesianGrid strokeDasharray="3 3" stroke={isDark ? GRAPHITE.border : "#f0f0f0"} />
            <XAxis dataKey="label" tick={{ fontSize: 11 }} stroke={isDark ? GRAPHITE.muted : "#6b7280"} />
            <YAxis tick={{ fontSize: 11 }} stroke={isDark ? GRAPHITE.muted : "#6b7280"} allowDecimals={false} />
            <Tooltip
              formatter={(value, name) => [
                value,
                name === "packageCount" ? "Balíků" : "Objednávek",
              ]}
              labelFormatter={(label) => `Den ${label}`}
              contentStyle={isDark ? { backgroundColor: GRAPHITE.surface, border: `1px solid ${GRAPHITE.border}`, borderRadius: 8 } : undefined}
              itemStyle={isDark ? { color: GRAPHITE.text } : undefined}
              labelStyle={isDark ? { color: GRAPHITE.muted } : undefined}
            />
            <Legend
              formatter={(value) => (value === "packageCount" ? "Balíků" : "Objednávek")}
            />
            <Bar dataKey="packageCount" fill={isDark ? GRAPHITE.accent : "#2563eb"} radius={[2, 2, 0, 0]} />
            <Bar dataKey="orderCount" fill="#93c5fd" radius={[2, 2, 0, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    );
  };
  ```
  Note: `isDark` is computed before the `data.length === 0` early return so hook order stays stable across renders (Rules of Hooks — `useTheme()` must run unconditionally on every render of this component).
- [ ] **`CarrierMixChart`** (currently lines 114–153) — add theme resolution, used only for the tooltip (per the contrast decisions above, `CARRIER_COLORS`/`OTHER_COLOR` are unchanged):
  ```tsx
  // before
  export const CarrierMixChart: React.FC<{ data: CarrierMix[] }> = ({ data }) => {
    if (data.length === 0) return <EmptyState />;
    const slices = buildCarrierSlices(data);
    return (
      <div className="h-72 w-full">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={slices}
              dataKey="packageCount"
              nameKey="name"
              cx="50%"
              cy="45%"
              innerRadius={45}
              outerRadius={75}
              paddingAngle={2}
            >
              {slices.map((entry, index) => (
                <Cell key={entry.key} fill={sliceColor(entry, index)} />
              ))}
            </Pie>
            <Tooltip formatter={(value) => [value, "Balíků"]} />
            <Legend
              layout="horizontal"
              verticalAlign="bottom"
              align="center"
              iconSize={8}
              wrapperStyle={{
                fontSize: 11,
                lineHeight: "16px",
                maxHeight: 64,
                overflowY: "hidden",
                paddingTop: 4,
              }}
            />
          </PieChart>
        </ResponsiveContainer>
      </div>
    );
  };
  // after
  export const CarrierMixChart: React.FC<{ data: CarrierMix[] }> = ({ data }) => {
    const { theme } = useTheme();
    const isDark = theme === "dark";
    if (data.length === 0) return <EmptyState />;
    const slices = buildCarrierSlices(data);
    return (
      <div className="h-72 w-full">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={slices}
              dataKey="packageCount"
              nameKey="name"
              cx="50%"
              cy="45%"
              innerRadius={45}
              outerRadius={75}
              paddingAngle={2}
            >
              {slices.map((entry, index) => (
                <Cell key={entry.key} fill={sliceColor(entry, index)} />
              ))}
            </Pie>
            <Tooltip
              formatter={(value) => [value, "Balíků"]}
              contentStyle={isDark ? { backgroundColor: GRAPHITE.surface, border: `1px solid ${GRAPHITE.border}`, borderRadius: 8 } : undefined}
              itemStyle={isDark ? { color: GRAPHITE.text } : undefined}
              labelStyle={isDark ? { color: GRAPHITE.muted } : undefined}
            />
            <Legend
              layout="horizontal"
              verticalAlign="bottom"
              align="center"
              iconSize={8}
              wrapperStyle={{
                fontSize: 11,
                lineHeight: "16px",
                maxHeight: 64,
                overflowY: "hidden",
                paddingTop: 4,
              }}
            />
          </PieChart>
        </ResponsiveContainer>
      </div>
    );
  };
  ```
  `sliceColor` and `buildCarrierSlices` (module-level functions, currently lines 49–81) are **not modified** — their signatures and bodies stay exactly as-is, per the contrast decision above.
- [ ] **`PackerLeaderboard`** (currently lines 155–180):
  ```tsx
  // before
  export const PackerLeaderboard: React.FC<{ data: PackerThroughput[] }> = ({ data }) => {
    if (data.length === 0) return <EmptyState />;
    return (
      <div className="w-full" style={{ height: Math.max(160, data.length * 44) }}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart
            layout="vertical"
            data={data}
            margin={{ top: 5, right: 20, left: 10, bottom: 5 }}
          >
            <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" horizontal={false} />
            <XAxis type="number" tick={{ fontSize: 11 }} stroke="#6b7280" allowDecimals={false} />
            <YAxis
              type="category"
              dataKey="packerName"
              width={120}
              tick={{ fontSize: 12 }}
              stroke="#6b7280"
            />
            <Tooltip formatter={(value) => [value, "Objednávek"]} />
            <Bar dataKey="orderCount" fill="#2563eb" radius={[0, 4, 4, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    );
  };
  // after
  export const PackerLeaderboard: React.FC<{ data: PackerThroughput[] }> = ({ data }) => {
    const { theme } = useTheme();
    const isDark = theme === "dark";
    if (data.length === 0) return <EmptyState />;
    return (
      <div className="w-full" style={{ height: Math.max(160, data.length * 44) }}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart
            layout="vertical"
            data={data}
            margin={{ top: 5, right: 20, left: 10, bottom: 5 }}
          >
            <CartesianGrid strokeDasharray="3 3" stroke={isDark ? GRAPHITE.border : "#f0f0f0"} horizontal={false} />
            <XAxis type="number" tick={{ fontSize: 11 }} stroke={isDark ? GRAPHITE.muted : "#6b7280"} allowDecimals={false} />
            <YAxis
              type="category"
              dataKey="packerName"
              width={120}
              tick={{ fontSize: 12 }}
              stroke={isDark ? GRAPHITE.muted : "#6b7280"}
            />
            <Tooltip
              formatter={(value) => [value, "Objednávek"]}
              contentStyle={isDark ? { backgroundColor: GRAPHITE.surface, border: `1px solid ${GRAPHITE.border}`, borderRadius: 8 } : undefined}
              itemStyle={isDark ? { color: GRAPHITE.text } : undefined}
              labelStyle={isDark ? { color: GRAPHITE.muted } : undefined}
            />
            <Bar dataKey="orderCount" fill={isDark ? GRAPHITE.accent : "#2563eb"} radius={[0, 4, 4, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    );
  };
  ```
- [ ] **`PackagesPerOrderChart`** (currently lines 182–206):
  ```tsx
  // before
  export const PackagesPerOrderChart: React.FC<{ data: PackagesPerOrderBucket[] }> = ({ data }) => {
    if (data.length === 0) return <EmptyState />;
    const chartData = data.map((b) => ({
      ...b,
      label: b.packageCount >= 3 ? "3+" : String(b.packageCount),
    }));
    return (
      <div className="h-60 w-full">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={chartData} margin={{ top: 10, right: 20, left: 0, bottom: 10 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
            <XAxis
              dataKey="label"
              tick={{ fontSize: 12 }}
              stroke="#6b7280"
              label={{ value: "Balíků v objednávce", position: "insideBottom", offset: -5, fontSize: 11 }}
            />
            <YAxis tick={{ fontSize: 11 }} stroke="#6b7280" allowDecimals={false} />
            <Tooltip formatter={(value) => [value, "Objednávek"]} />
            <Bar dataKey="orderCount" fill="#0ea5e9" radius={[2, 2, 0, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    );
  };
  // after
  export const PackagesPerOrderChart: React.FC<{ data: PackagesPerOrderBucket[] }> = ({ data }) => {
    const { theme } = useTheme();
    const isDark = theme === "dark";
    if (data.length === 0) return <EmptyState />;
    const chartData = data.map((b) => ({
      ...b,
      label: b.packageCount >= 3 ? "3+" : String(b.packageCount),
    }));
    return (
      <div className="h-60 w-full">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={chartData} margin={{ top: 10, right: 20, left: 0, bottom: 10 }}>
            <CartesianGrid strokeDasharray="3 3" stroke={isDark ? GRAPHITE.border : "#f0f0f0"} />
            <XAxis
              dataKey="label"
              tick={{ fontSize: 12 }}
              stroke={isDark ? GRAPHITE.muted : "#6b7280"}
              label={{ value: "Balíků v objednávce", position: "insideBottom", offset: -5, fontSize: 11 }}
            />
            <YAxis tick={{ fontSize: 11 }} stroke={isDark ? GRAPHITE.muted : "#6b7280"} allowDecimals={false} />
            <Tooltip
              formatter={(value) => [value, "Objednávek"]}
              contentStyle={isDark ? { backgroundColor: GRAPHITE.surface, border: `1px solid ${GRAPHITE.border}`, borderRadius: 8 } : undefined}
              itemStyle={isDark ? { color: GRAPHITE.text } : undefined}
              labelStyle={isDark ? { color: GRAPHITE.muted } : undefined}
            />
            <Bar dataKey="orderCount" fill="#0ea5e9" radius={[2, 2, 0, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    );
  };
  ```

Do not change `MAX_CARRIERS`, `OTHER_LABEL`, `OTHER_KEY`, `CarrierSlice`, or the `PackingStatisticsResponse`/data-hook imports — only the JSX/prop edits shown above.

**Verification steps:**

- [ ] Run the existing test file unmodified and confirm it still passes:
  ```bash
  cd frontend && CI=true npm test -- --testPathPattern=PackingCharts
  ```
  Expected: all tests in `src/components/baleni/statistics/__tests__/PackingCharts.test.tsx` pass unmodified (they exercise `buildCarrierSlices`/rendering, not colors).
- [ ] Run the build to confirm no TypeScript errors (in particular that `useTheme()` is called unconditionally at the top of each component, before any early return, satisfying the Rules of Hooks lint check):
  ```bash
  cd frontend && npm run build
  ```
  Expected: build succeeds with no new errors.
- [ ] Run the linter, specifically watching for `react-hooks/rules-of-hooks`:
  ```bash
  cd frontend && npm run lint
  ```
  Expected: no new lint errors.
- [ ] Manual/visual spot-check (if a dev instance is available): toggle Graphite dark mode and confirm all four charts' grid lines and axis labels are visible against the dark panel background, the primary bars in "Průběh balení v čase" and "Baliči" render in the cyan accent color, and no chart's tooltip renders as a stark white box.
- [ ] Commit:
  ```bash
  git add frontend/src/components/baleni/statistics/PackingCharts.tsx
  git commit -m "Add theme-aware Recharts colors to PackingCharts.tsx"
  ```

---

### task: dark-mode-packing-hour-heatmap

**Context:** `frontend/src/components/baleni/statistics/PackingHourHeatmap.tsx` renders a weekday × hour activity heatmap as a plain HTML `<table>` with per-`<td>` inline `style={{ backgroundColor }}`. It has two dark-mode defects:
1. No `dark:` classes on the hour/weekday `<th>`/`<td>` labels or the empty-state message (`text-neutral-gray` is illegible against a dark page).
2. The empty-cell color reads `var(--heatmap-empty, #f1f5f9)` — this CSS custom property is referenced nowhere else in the codebase and is defined nowhere in `frontend/src/index.css`, so it always resolves to the light-only fallback `#f1f5f9`, making empty and low-activity cells visually indistinguishable from the dark page background.

The fix follows the same `useTheme()` + `GRAPHITE` pattern as task `dark-mode-packing-charts-recharts` (see that task for the full `GRAPHITE` object definition and import paths) rather than defining the CSS variable, so this subtree has one dark-mode mechanism, not two.

**Occupied-cell contrast finding (verified, apply as specified — do not re-derive):** the existing occupied-cell formula `rgba(37, 99, 235, ${0.15 + intensity * 0.85})` (blue `#2563eb` base) computed against the panel's dark background (`graphite-surface`, `#202327`) tops out at only ≈2.9:1 contrast even at full intensity (`alpha=1`), and at the low end (`alpha=0.15`) is visually indistinguishable from the proposed empty-cell color (`GRAPHITE.surface2`, `#272A30`) — contrast ratio ≈1.07:1. Two changes are needed for dark mode specifically (light mode keeps the exact current formula, unchanged):
- Swap the base hue from `#2563eb` (`rgb(37,99,235)`) to `GRAPHITE.accent` (`#38BDF8` = `rgb(56,189,248)`), which reaches ≈6.9:1 contrast at full intensity against `#202327`.
- Raise the alpha floor from `0.15` to `0.35` in dark mode, so the lowest-intensity occupied cell is still perceptibly brighter than the empty cell.

Apply the following edits to `frontend/src/components/baleni/statistics/PackingHourHeatmap.tsx`:

- [ ] **Add imports** after the existing import (currently line 2, before line 4 `interface PackingHourHeatmapProps`):
  ```tsx
  // before
  import React from "react";
  import { HourBucket } from "../../../api/hooks/usePackingStatistics";

  interface PackingHourHeatmapProps {
  // after
  import React from "react";
  import { HourBucket } from "../../../api/hooks/usePackingStatistics";
  import { useTheme } from "../../../contexts/ThemeContext";
  import { GRAPHITE } from "../../common/reactSelectDarkStyles";

  interface PackingHourHeatmapProps {
  ```
- [ ] **Compute `isDark`** inside the component, immediately after the existing `data` destructure (currently line 18, before the `counts` memo on line 19):
  ```tsx
  // before
  const PackingHourHeatmap: React.FC<PackingHourHeatmapProps> = ({ data }) => {
    const counts = React.useMemo(() => {
  // after
  const PackingHourHeatmap: React.FC<PackingHourHeatmapProps> = ({ data }) => {
    const { theme } = useTheme();
    const isDark = theme === "dark";
    const counts = React.useMemo(() => {
  ```
- [ ] **Empty-state message** (currently lines 47–50):
  ```tsx
  // before
  if (data.length === 0) {
    return (
      <p className="text-sm text-neutral-gray italic">Žádná data k zobrazení.</p>
    );
  }
  // after
  if (data.length === 0) {
    return (
      <p className="text-sm text-neutral-gray italic dark:text-graphite-muted">Žádná data k zobrazení.</p>
    );
  }
  ```
- [ ] **Hour header labels** (currently line 60):
  ```tsx
  // before
  <th key={hour} className="text-xs font-normal text-neutral-gray text-center w-7">
  // after
  <th key={hour} className="text-xs font-normal text-neutral-gray text-center w-7 dark:text-graphite-muted">
  ```
- [ ] **Weekday labels** (currently line 71):
  ```tsx
  // before
  <td className="text-xs text-neutral-gray pr-1 text-right">{label}</td>
  // after
  <td className="text-xs text-neutral-gray pr-1 text-right dark:text-graphite-muted">{label}</td>
  ```
- [ ] **Cell background color** (currently lines 76–87) — replace the undefined CSS variable with the theme-aware inline color, and use the adjusted dark-mode formula for occupied cells:
  ```tsx
  // before
  return (
    <td
      key={hour}
      className="h-7 w-7 rounded"
      title={`${label} ${hour}:00 — ${count} balíků`}
      style={{
        backgroundColor:
          count === 0
            ? "var(--heatmap-empty, #f1f5f9)"
            : `rgba(37, 99, 235, ${0.15 + intensity * 0.85})`,
      }}
    />
  );
  // after
  return (
    <td
      key={hour}
      className="h-7 w-7 rounded"
      title={`${label} ${hour}:00 — ${count} balíků`}
      style={{
        backgroundColor:
          count === 0
            ? isDark
              ? GRAPHITE.surface2
              : "#f1f5f9"
            : isDark
              ? `rgba(56, 189, 248, ${0.35 + intensity * 0.65})`
              : `rgba(37, 99, 235, ${0.15 + intensity * 0.85})`,
      }}
    />
  );
  ```

Do not change `cellKey`, `counts`, `maxCount`, `fromHour`, `toHour`, `WEEKDAY_LABELS`, `DEFAULT_FROM_HOUR`/`DEFAULT_TO_HOUR`, the component's props (`PackingHourHeatmapProps { data: HourBucket[] }`), or the `title` tooltip text — only the four edits above.

**Verification steps:**

- [ ] Confirm no test file exists for this component today (only `BaleniStatistics.test.tsx` and `PackingCharts.test.tsx` exist under `__tests__/`, so there is no dedicated suite to run for this file):
  ```bash
  ls frontend/src/components/baleni/statistics/__tests__/
  ```
  Expected: `BaleniStatistics.test.tsx` and `PackingCharts.test.tsx` only — no `PackingHourHeatmap.test.tsx`. Since `BaleniStatistics.tsx` renders `PackingHourHeatmap` (via the `Panel title="Vytížení podle hodin"` block), re-run the `BaleniStatistics` suite as an integration smoke check:
  ```bash
  cd frontend && CI=true npm test -- --testPathPattern=BaleniStatistics
  ```
  Expected: passes unmodified (no assertions on this component's classes or inline styles).
- [ ] Run the build to confirm no TypeScript errors:
  ```bash
  cd frontend && npm run build
  ```
  Expected: build succeeds with no new errors.
- [ ] Run the linter:
  ```bash
  cd frontend && npm run lint
  ```
  Expected: no new lint errors.
- [ ] Manual/visual spot-check (if a dev instance is available): toggle Graphite dark mode and confirm (a) hour/weekday labels are legible, (b) empty cells (`GRAPHITE.surface2`) are visibly distinct from the panel background (`graphite-surface`) they sit on, and (c) the occupied-cell gradient from lowest to highest activity is perceptibly ordered and distinct from the empty-cell color at every step, not just at the high end.
- [ ] Run the full frontend suite one final time to confirm the three-file change set is clean together:
  ```bash
  cd frontend && CI=true npm test && npm run build && npm run lint
  ```
  Expected: all pass with zero new failures/errors.
- [ ] Commit:
  ```bash
  git add frontend/src/components/baleni/statistics/PackingHourHeatmap.tsx
  git commit -m "Fix undefined --heatmap-empty variable and theme heatmap for dark mode"
  ```
