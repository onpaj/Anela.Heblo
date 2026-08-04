# Architecture assessment: BankStatementImportChart theme-aware colors

## Verdict

Approved with one required change: reuse the existing `GRAPHITE` hex constants instead of inventing an approximated palette. Everything else in the plan/design (scope, mechanism, `useTheme()` wiring, file boundary) is correct and matches codebase conventions.

## Alignment with existing patterns

Checked against source, not assumption:

- `frontend/src/contexts/ThemeContext.tsx` — confirmed: `useTheme()` returns `{ theme: 'light'|'dark', toggle }`, throws outside a `ThemeProvider`. App-wide provider means no new wiring needed. Design's `const { theme } = useTheme(); const isDark = theme === 'dark';` matches this exactly.
- `frontend/src/components/charts/BankStatementImportChart.tsx` (249 lines, read in full) — every line number and hex literal cited in the finding/plan/design matches the current file content verbatim (grid `#f0f0f0` at CartesianGrid, axis `#6b7280` ×2, weekend `#e0f2fe`, threshold `#dc2626` ×2, line `#3b82f6` ×2, dot `#dc2626`/`#fff`). No drift between the finding and current source.
- `docs/architecture/development_guidelines.md` ADR-006 — confirmed as written: every color-rendering component must work in both themes.
- **`docs/design/dark-mode-conversion-guide.md` rule 8** — "Leave chart library colors (recharts fill/stroke hex props) alone unless trivially a className." This is not a conflict with the task: that rule scopes the *generic, mechanical* dark-mode sweep (append `dark:` classes to `className` strings) and explicitly excludes SVG props because they can't be handled that way. It implicitly confirms that fixing Recharts colors requires exactly the JS-computed approach the design proposes (`useTheme()` + a value lookup), not a blanket "don't touch charts" directive. No conflict.
- **Precedent found that the design missed**: `frontend/src/components/common/reactSelectDarkStyles.ts` solves the *identical* problem — react-select, like Recharts, takes JS style objects, not Tailwind classes. It defines:
  ```ts
  export const GRAPHITE = {
    surface: "#202327", surface2: "#272A30", hover: "#2E323A",
    border: "#2D3138", borderStrong: "#3C424B", text: "#E6E8EC",
    muted: "#9AA0AA", faint: "#6A707A", accent: "#38BDF8", accentInk: "#08171F",
  } as const;
  ```
  and a `getSelectStyles(isDark, options)` function keyed on the same `isDark` boolean. This is the codebase's established precedent for "theme-aware colors in a JS-only rendering context" — and it mirrors `tailwind.config.js`'s `graphite` scale exactly (verified: `surface #202327`, `border #2D3138`, `muted #9AA0AA` match the Tailwind config token-for-token).

## Problem with the current design

The design/plan's `colors` map (design-01.md, lines 41-48) uses **approximated generic Tailwind gray/red hexes** instead of the actual graphite tokens, despite the plan's own stated intent (plan-01.md line 43) to "pick the closest matching hex equivalents" from the existing dark palette:

| key | design's value | nearest real graphite token | delta |
|---|---|---|---|
| `grid` | `#374151` (gray-700) | `graphite-border` `#2D3138` / `border-strong` `#3C424B` | picked neither — off-palette |
| `axis` | `#9ca3af` (gray-400) | `graphite-muted` `#9AA0AA` | close but not the actual token |
| `dotStroke` | `#1f2937` (gray-800) | `graphite-surface` `#202327` | close but not the actual token |

These are visually similar but **not the same values** as what the rest of the app's dark theme uses. This matters because:
1. It creates a second, parallel, slightly-off palette instead of the single source of truth (`tailwind.config.js` → mirrored in `reactSelectDarkStyles.ts`'s `GRAPHITE` const).
2. A future maintainer changing the graphite scale in `tailwind.config.js` (e.g. adjusting `border` or `surface`) would have no way to find this chart's independently-invented near-duplicates — they aren't grep-able as the same token.
3. It's inconsistent with how the codebase already solved this exact class of problem (react-select).

`threshold` (`#dc2626`→`#f87171`, red-400) and `weekendFill` (`#e0f2fe`→`#0ea5e9`, sky-500) don't have direct graphite equivalents — these are semantic/accent hues, not surface/text/border tokens, and the conversion guide's own rule ("status pills keep their hue, darken") supports picking a suitable red/blue shade rather than forcing them onto the neutral graphite scale. No change needed there. `graphite-accent` (`#38BDF8`) is close to the proposed `weekendFill` dark value and could be considered, but sky-500 at the existing `fillOpacity={0.3}` is a reasonable, defensible choice — not a hard requirement to change.

## Required change

Import and reuse the existing `GRAPHITE` constants for the three tokens that have a direct match, rather than re-deriving approximate values:

```tsx
import { useTheme } from '../../contexts/ThemeContext';
import { GRAPHITE } from '../common/reactSelectDarkStyles';

const { theme } = useTheme();
const isDark = theme === 'dark';
const colors = {
  grid:        isDark ? GRAPHITE.border : '#f0f0f0',
  axis:        isDark ? GRAPHITE.muted  : '#6b7280',
  weekendFill: isDark ? '#0ea5e9'       : '#e0f2fe',   // sky accent, no direct graphite token — unchanged
  threshold:   isDark ? '#f87171'       : '#dc2626',   // semantic red, unchanged
  line:        '#3b82f6',
  dotStroke:   isDark ? GRAPHITE.surface : '#fff',
};
```

This is still a same-file-only change from the caller's perspective (`BankStatementImportChart.tsx` is the only file *edited*; `reactSelectDarkStyles.ts` is only imported from, not modified) — it does not violate the finding's "no changes outside this one component file" instruction, which governs edits, not imports of existing shared constants. `GRAPHITE` is already exported (`export const GRAPHITE = ...`) for exactly this kind of cross-component reuse.

Everything else in design-01.md and plan-01.md stands: single-file edit, no new abstraction/hook, no prop/schema/API changes, `CustomDot` and JSX prop replacement plan, out-of-scope call-outs.

## Additional observation (informational, not blocking)

Two more charts have the identical hardcoded-hex pattern and were not mentioned in the plan's "out of scope" list (which only names `InvoiceImportChart.tsx`):
- `frontend/src/components/packing-materials/modals/PackingMaterialConsumptionChart.tsx`
- `frontend/src/components/baleni/statistics/PackingCharts.tsx`

Same recommendation as the plan already made for `InvoiceImportChart.tsx`: leave out of scope for this task, worth a follow-up arch-review finding (possibly a shared `useChartColors()` hook once 3+ charts need the same fix — no such hook exists yet, confirmed via search).

## Implementation guidance

- Edit only `frontend/src/components/charts/BankStatementImportChart.tsx`.
- Add two imports: `useTheme` from `../../contexts/ThemeContext`, `GRAPHITE` from `../common/reactSelectDarkStyles`.
- Derive `isDark` and the `colors` object once, near the top of the function body (before `chartData`), as in design-01.md.
- Replace the 9 hex literals per the FR-2 table, using `GRAPHITE.border` / `GRAPHITE.muted` / `GRAPHITE.surface` in place of the three approximated dark values called out above; keep the two semantic-hue dark values (`threshold`, `weekendFill`) as proposed.
- No test file exists for this component today (confirmed) — none required to close this finding.

## Risks and mitigations

- **Risk**: `reactSelectDarkStyles.ts` is under `components/common/`, a different subdirectory than `components/charts/` — confirm the relative import path resolves (`../common/reactSelectDarkStyles` from `components/charts/`). Verified directory layout: both are siblings under `components/`, so `../common/reactSelectDarkStyles` is correct.
- **Risk**: pulling in `GRAPHITE` couples a chart component to a file named for react-select. This is a naming smell, not a functional risk — the constant itself is a generic palette mirror, and renaming/relocating it (e.g. to a shared `theme/graphitePalette.ts`) is reasonable future cleanup but out of scope for this fix; don't do it as a side effect here.
- **Risk (low)**: `GRAPHITE` doesn't export a `borderStrong`-vs-`border` distinction judgment for `grid` — `border` (`#2D3138`) is the correct pick since it's the general-purpose divider/grid-line token used elsewhere (`border-graphite-border` in the conversion guide's own mapping table for `border-gray-200/300/100`).

## Prerequisites before implementation

None — `ThemeProvider` is already mounted app-wide, `GRAPHITE` is already exported, no build/dependency changes needed.
