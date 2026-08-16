# Design: Dark-mode support for Packing Statistics (Baleni)

## UX/UI Design

### Scope of the visual change

Three screens/sub-views, all reached from `/baleni/statistiky`, currently render as light-only surfaces regardless of the app's active theme. This design adds the Graphite dark palette to each, with **zero layout, copy, or structural change** — only color.

```
BaleniHome (/baleni)  ──►  BaleniStatistics (/baleni/statistiky)
                              ├─ Header + range presets + refresh
                              ├─ KPI card row (6 cards)
                              ├─ Panel: "Průběh balení v čase"    → ThroughputChart
                              ├─ Panel: "Vytížení podle hodin"    → PackingHourHeatmap
                              ├─ Panel: "Baliči"                  → PackerLeaderboard
                              ├─ Panel: "Dopravci"                → CarrierMixChart
                              └─ Panel: "Balíků na objednávku"    → PackagesPerOrderChart
```

### Wireframe — header + KPI row

Structure is unchanged; only fill/text/border colors shift. Light values shown above the arrow, dark (Graphite) values below.

```
┌──────────────────────────────────────────────────────────────────────────┐
│  📊 Statistiky balicí stanice                    [7 dní][30 dní][90 dní] ⟳│
│  6. 8. 2026 – 5. 9. 2026                                                  │
│                                                                            │
│  bg-white / text-neutral-slate            →  dark:bg-graphite-bg (page)  │
│  active preset: bg-secondary-blue-pale       dark:bg-graphite-accent/10  │
│                 border-primary-blue          dark:border-graphite-accent │
│                 text-primary-blue            dark:text-graphite-accent   │
│  inactive preset: bg-white/border-border-    dark:bg-graphite-surface    │
│                    light/text-neutral-gray   dark:border-graphite-border │
│                                               dark:text-graphite-muted   │
├──────────────────────────────────────────────────────────────────────────┤
│ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐  │
│ │ Balíků  │ │Objednáv.│ │Ø bal/obj│ │Tracking │ │ Baličů  │ │Nejvytíž.│  │
│ │  1 284  │ │   612   │ │  2.10   │ │  94 %   │ │    5    │ │ 3. 8.   │  │
│ └─────────┘ └─────────┘ └─────────┘ └─────────┘ └─────────┘ └─────────┘  │
│  card: bg-white/border-border-light/shadow-soft                          │
│     →  dark:bg-graphite-surface / dark:border-graphite-border /          │
│        dark:shadow-soft-dark                                             │
│  value: text-primary-blue → dark:text-graphite-accent                    │
│  label: text-neutral-gray → dark:text-graphite-muted                     │
└──────────────────────────────────────────────────────────────────────────┘
```

### Wireframe — panels, charts, heatmap

```
┌ Panel "Vytížení podle hodin" ──────────────────────────────────────────┐
│  panel: bg-white/border-border-light/shadow-soft (same mapping as card)│
│  title: text-neutral-slate → dark:text-graphite-text                   │
│  subtitle: text-neutral-gray → dark:text-graphite-muted                │
│                                                                          │
│         6   7   8   9  10  11  ...                                     │
│   Po   ▢   ▢   ▨   ▨   ▨   ▩  ...   ▢ = empty cell                     │
│   Út   ▢   ▨   ▨   ▩   ▩   ▩  ...   ▨ = low intensity (rgba blue)      │
│   St   ▢   ▢   ▨   ▨   ▩   ▩  ...   ▩ = high intensity (rgba blue)     │
│                                                                          │
│   light empty: #f1f5f9 (near-white, reads on white panel)              │
│   dark  empty: GRAPHITE.surface2 #272A30 (reads on graphite-surface)   │
│   occupied: rgba(37,99,235, 0.15 + intensity*0.85) — same base hue in  │
│   both themes; verify the 0.15-alpha floor still separates from        │
│   #272A30 (see Component Design → PackingHourHeatmap for the check)    │
└──────────────────────────────────────────────────────────────────────────┘

┌ Panel "Průběh balení v čase" ───────────┐  ┌ Panel "Dopravci" ─────────┐
│  bar chart: grid/axis strokes swap to   │  │  pie chart: same grid/    │
│  GRAPHITE.border / GRAPHITE.muted in    │  │  axis rules (n/a for pie, │
│  dark; primary bar fill swaps to        │  │  slice fills evaluated    │
│  GRAPHITE.accent (#38BDF8) in dark      │  │  individually for AA)     │
└──────────────────────────────────────────┘  └───────────────────────────┘
```

### Error and loading states

```
Error (light):  bg-red-50 / border-red-200 / text-red-600,700,800
Error (dark):   + dark:bg-red-950/30 dark:border-red-900/50
                + dark:text-red-400 (all three red text shades collapse to
                  one dark shade per the guide's semantic-status mapping)
retry button:   bg-red-100 text-red-800 hover:bg-red-200
                + dark:bg-red-900/30 dark:text-red-300 dark:hover:bg-red-900/50

Loading (full-page): bg-white/border-border-light/shadow-soft (same panel
mapping); spinner text-primary-blue → dark:text-graphite-accent; label
text-neutral-gray → dark:text-graphite-muted
```

### Key interactions (unchanged, verified not to regress)

- Range preset buttons (`7 dní` / `30 dní` / `90 dní`) — click swaps `rangeDays` state; the active/inactive class ternary gets `dark:` added to **both** branches (see Component Design § BaleniStatistics for the exact strings — this is the one spot most likely to be half-done by a mechanical edit).
- Refresh button — spins `RefreshCw` icon while `isFetching`; icon/border/text get `dark:` siblings, spin behavior untouched.
- Heatmap cell hover — native `title` tooltip (`"{den} {hodina}:00 — {count} balíků"`) is browser-rendered chrome, not themeable and not in scope.
- Theme toggle (elsewhere in the app shell) — flips `<html class="dark">`; `BaleniStatistics.tsx`'s Tailwind-only surfaces re-theme via the CSS cascade with no JS involvement, while `PackingCharts.tsx`'s four chart components and `PackingHourHeatmap.tsx` each read `useTheme()` and re-render once on toggle (new subscription, not present today — expected, not a regression).

### Accessibility target

WCAG 2.1 AA: 4.5:1 for KPI values/labels, panel titles/subtitles, error text; 3:1 for chart grid/axis strokes and heatmap cell fills. Spot-check with devtools contrast checker against `graphite-surface` (`#202327`) for text/borders and against the page background for chart marks, per NFR-3 in the spec. No new UI element introduces a fixed light-only color anywhere in the three files after this change.

## Component Design

No new components, no new props, no new files. Three existing files are edited in place; two existing modules are imported (not modified).

### `BaleniStatistics.tsx` — Tailwind-only, no `useTheme()`

Matches `BaleniHome.tsx`'s already-themed `StatCard`/panel pattern exactly (`BaleniHome.tsx:45`, `:90`) — this file needs no JS theme awareness because the `.dark` class on `<html>` drives the CSS cascade for plain Tailwind utilities.

| Element | Light classes (unchanged) | Dark classes to append |
|---|---|---|
| `Panel` wrapper | `bg-white border border-border-light rounded-xl p-6 shadow-soft` | `dark:bg-graphite-surface dark:border-graphite-border dark:shadow-soft-dark` |
| `Panel` title | `text-neutral-slate` | `dark:text-graphite-text` |
| `Panel` subtitle | `text-neutral-gray` | `dark:text-graphite-muted` |
| `KpiCard` wrapper | `bg-white border border-border-light rounded-xl p-5 shadow-soft` | `dark:bg-graphite-surface dark:border-graphite-border dark:shadow-soft-dark` |
| `KpiCard` label | `text-neutral-gray` | `dark:text-graphite-muted` |
| `KpiCard` loading pulse | `bg-secondary-blue-pale` | `dark:bg-graphite-surface-2` |
| `KpiCard` value | `text-primary-blue` | `dark:text-graphite-accent` |
| Error banner | `bg-red-50 border border-red-200` | `dark:bg-red-950/30 dark:border-red-900/50` |
| Error heading | `text-red-800` | `dark:text-red-400` |
| Error body | `text-red-700` | `dark:text-red-400` |
| Error icon | `text-red-600` | `dark:text-red-400` |
| Retry button | `bg-red-100 text-red-800 hover:bg-red-200` | `dark:bg-red-900/30 dark:text-red-300 dark:hover:bg-red-900/50` |
| H1 heading | `text-neutral-slate` | `dark:text-graphite-text` |
| H1 icon (`BarChart3`) | `text-primary-blue` | `dark:text-graphite-accent` |
| Date-range subtitle | `text-neutral-gray` | `dark:text-graphite-muted` |
| Preset button — active branch | `bg-secondary-blue-pale border-primary-blue text-primary-blue` | `dark:bg-graphite-accent/10 dark:border-graphite-accent dark:text-graphite-accent` |
| Preset button — inactive branch | `bg-white border-border-light text-neutral-gray hover:bg-secondary-blue-pale` | `dark:bg-graphite-surface dark:border-graphite-border dark:text-graphite-muted dark:hover:bg-white/5` |
| Refresh button | `border-border-light text-neutral-gray hover:bg-secondary-blue-pale` | `dark:border-graphite-border dark:text-graphite-muted dark:hover:bg-white/5` |
| Full-page loading box | `bg-white border border-border-light shadow-soft` | `dark:bg-graphite-surface dark:border-graphite-border dark:shadow-soft-dark` |
| Loading spinner icon | `text-primary-blue` | `dark:text-graphite-accent` |
| Loading label | `text-neutral-gray` | `dark:text-graphite-muted` |

**Contract:** unchanged — `BaleniStatistics: React.FC` with no props; internal `Panel`/`KpiCard` sub-components keep the same prop shapes (`{title, children, subtitle?}` and `{label, value, loading}`). Both branches of the preset-button ternary (currently `rangeDays === preset.days ? "..." : "..."`) must receive matching dark siblings — this is the guide's rule 4 and the arch-review's top-listed risk.

### `PackingCharts.tsx` — `useTheme()` + `GRAPHITE` per component

Follows `BankStatementImportChart.tsx:44-53`'s shape exactly: each of the four exported components reads `useTheme()` independently (no prop threading from `BaleniStatistics.tsx`, matching the spec's explicit "no new props" requirement) and builds a local `colors` object.

```ts
import { useTheme } from "../../../contexts/ThemeContext";
import { GRAPHITE } from "../../common/reactSelectDarkStyles";

// inside each of ThroughputChart, CarrierMixChart, PackerLeaderboard, PackagesPerOrderChart
const { theme } = useTheme();
const isDark = theme === "dark";
const colors = {
  grid: isDark ? GRAPHITE.border : "#f0f0f0",
  axis: isDark ? GRAPHITE.muted : "#6b7280",
  primaryBar: isDark ? GRAPHITE.accent : "#2563eb",
};
```

| Component | Prop (unchanged) | Colors touched |
|---|---|---|
| `ThroughputChart` | `{ data: DailyThroughput[] }` | `CartesianGrid stroke` → `colors.grid`; `XAxis`/`YAxis stroke` → `colors.axis`; `Bar dataKey="packageCount" fill` → `colors.primaryBar`; `Bar dataKey="orderCount" fill="#93c5fd"` kept unless contrast check fails against `#202327` |
| `CarrierMixChart` | `{ data: CarrierMix[] }` | `CARRIER_COLORS`/`OTHER_COLOR` (module-level, currently theme-agnostic) evaluated for AA against `graphite-surface`; only swapped per-value if a specific hex fails — `buildCarrierSlices`/`sliceColor` signatures stay pure and untouched unless `sliceColor` needs to close over `isDark` |
| `PackerLeaderboard` | `{ data: PackerThroughput[] }` | `CartesianGrid stroke` (`horizontal={false}`) → `colors.grid`; `XAxis`/`YAxis stroke` → `colors.axis`; `Bar dataKey="orderCount" fill` → `colors.primaryBar` |
| `PackagesPerOrderChart` | `{ data: PackagesPerOrderBucket[] }` | `CartesianGrid stroke` → `colors.grid`; `XAxis`/`YAxis stroke` → `colors.axis`; `Bar fill="#0ea5e9"` kept unless contrast check fails |
| `EmptyState` (shared, module-level) | none | `text-neutral-gray` → append `dark:text-graphite-muted` |

**Tooltip/Legend:** Recharts' default `Tooltip`/`Legend` chrome (white box, dark text) renders as a light "flash" in dark mode. Per the spec, prefer the lighter-weight `contentStyle`/`itemStyle`/`labelStyle` props over `BankStatementImportChart.tsx`'s full `CustomTooltip` component (that file's tooltip does more — per-payload formatting, weekend/threshold badges — than these charts need); escalate to a custom tooltip only if `contentStyle` alone can't reach AA contrast for every sub-element (label, value, legend swatch text).

**Contract:** no exported function signature changes. `buildCarrierSlices(data): CarrierSlice[]` and `sliceColor(slice, index): string` stay pure; `sliceColor`'s *return value* may become theme-conditional (requires it to read `isDark` from closure or an added parameter) only if the contrast check in the Risks table below fails — this is an internal implementation detail, not a public contract change, since `sliceColor` is not exported today.

### `PackingHourHeatmap.tsx` — `useTheme()` + deterministic empty-cell color

```ts
import { useTheme } from "../../../contexts/ThemeContext";
import { GRAPHITE } from "../../common/reactSelectDarkStyles";

const { theme } = useTheme();
const isDark = theme === "dark";
const emptyCellColor = isDark ? GRAPHITE.surface2 : "#f1f5f9"; // #272A30 in dark
```

Replaces the current `backgroundColor: count === 0 ? "var(--heatmap-empty, #f1f5f9)" : ...` — the CSS custom property is referenced nowhere else in the codebase and defined nowhere (`grep -rn "heatmap-empty" frontend/src/` → one hit, this file), so it always silently resolved to the light-only fallback. Resolving the color in JS instead of CSS keeps the file on the same single mechanism (`useTheme()` + `GRAPHITE`) as `PackingCharts.tsx`, rather than adding a second, single-use CSS-variable convention to `frontend/src/index.css`.

The intensity-scaled occupied-cell color (`rgba(37, 99, 235, ${0.15 + intensity * 0.85})`) keeps its blue base in both themes; implementation must visually verify the low end (`alpha 0.15`) stays distinguishable from `GRAPHITE.surface2` (`#272A30`) — if it collapses, raise the alpha floor or swap the RGB base to `GRAPHITE.accent`'s components in dark mode only. This is a visual judgment call flagged by the spec, not a fixed formula to derive here.

| Element | Light | Dark |
|---|---|---|
| Hour/weekday `<th>`/`<td>` labels | `text-neutral-gray` | `dark:text-graphite-muted` |
| Empty-state message (`data.length === 0`) | `text-neutral-gray` | `dark:text-graphite-muted` |
| Empty cell | `#f1f5f9` | `GRAPHITE.surface2` (`#272A30`) |
| Occupied cell | `rgba(37,99,235, 0.15 + intensity*0.85)` | same formula; alpha floor/base hue adjusted only if the contrast check fails |

**Contract:** `PackingHourHeatmapProps { data: HourBucket[] }` unchanged. `cellKey`, `counts`, `maxCount`, `fromHour`, `toHour` memoized values untouched — only the `style={{ backgroundColor }}` expression on the `<td>` changes.

### Cross-file dependency graph

```
BaleniStatistics.tsx  ──(renders, no useTheme)──►  Panel, KpiCard (local)
        │
        ├──► PackingHourHeatmap.tsx  ──uses──►  useTheme()  +  GRAPHITE.surface2
        │
        └──► PackingCharts.tsx
                ├─ ThroughputChart       ──uses──►  useTheme() + GRAPHITE.{border,muted,accent}
                ├─ CarrierMixChart       ──uses──►  useTheme() (only if palette fails AA)
                ├─ PackerLeaderboard     ──uses──►  useTheme() + GRAPHITE.{border,muted,accent}
                └─ PackagesPerOrderChart ──uses──►  useTheme() + GRAPHITE.{border,muted}

Shared, imported not modified:
  frontend/src/contexts/ThemeContext.tsx           → useTheme()
  frontend/src/components/common/reactSelectDarkStyles.ts → GRAPHITE token object
```

## Data Schemas

No new or changed data schemas. This is a presentation-only fix — no DTOs, no API requests/responses, no event payloads, no database entities are introduced or touched. `usePackingStatistics`, `PackingStatisticsResponse`, `HourBucket`, `DailyThroughput`, `CarrierMix`, `PackerThroughput`, and `PackagesPerOrderBucket` (all imported from `../../../api/hooks/usePackingStatistics`) are consumed exactly as they are today; none of their shapes change.

The only "shape" this design reuses is the existing, already-shipped `GRAPHITE` color-token object exported from `frontend/src/components/common/reactSelectDarkStyles.ts` (imported, never modified):

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

Note the property naming: this JS token object uses `surface2` (no hyphen), while the corresponding Tailwind utility class is `graphite-surface-2` (hyphenated) — both refer to the same color and are already used side-by-side elsewhere (`BaleniHome.tsx` uses the Tailwind class; `BankStatementImportChart.tsx` uses the JS token). Neither this design nor its implementation introduces a new token, renames anything in `reactSelectDarkStyles.ts`, or adds entries to `tailwind.config.js` — the full `graphite` scale already needed here is defined and stable.
