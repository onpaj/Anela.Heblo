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
