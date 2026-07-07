## Module
GridLayouts

## Finding
Both `GridHeader.tsx` and `ColumnChooser.tsx` use exclusively light-mode Tailwind utilities. No `dark:` variants are present anywhere in either file, violating ADR-006 (accepted 2026-06-25).

**`frontend/src/features/grid-layout/GridHeader.tsx`** — selected light-only classes:
- Line 91: `bg-gray-50 … text-gray-500` (thead background and header text)
- Line 99: `text-gray-300 hover:text-gray-500` (grip icon)
- Line 111: `text-indigo-600` / `text-gray-300` (sort chevrons)
- Line 117: `hover:bg-indigo-200` (resize handle hover)

**`frontend/src/features/grid-layout/ColumnChooser.tsx`** — selected light-only classes:
- Line 24: `text-gray-600 border border-gray-300 hover:bg-gray-50` (trigger button)
- Line 38: `bg-white border border-gray-200` (dropdown panel)
- Line 46: `text-gray-700 hover:text-gray-900` (column label)
- Line 51: `border-gray-300` (checkbox border)
- Line 61: `border-t border-gray-100` (footer separator)
- Line 65: `text-gray-500 hover:text-gray-700 hover:bg-gray-50` (reset button)

These are the shared grid UI components used by every page that embeds `useGridLayout`. Because they are shared, missing dark mode affects all grids across the app.

## Why it matters
ADR-006 requires every component that renders color to work correctly in both light and the Graphite dark theme. The column chooser dropdown (`bg-white`) and the sticky header (`bg-gray-50`) render with broken contrast in dark mode.

## Suggested fix
Per ADR-006 and `docs/design/dark-mode-conversion-guide.md`, add `dark:` sibling variants (or replace with design-system tokens where available). Key conversions:
- `bg-gray-50` → `bg-gray-50 dark:bg-graphite-surface`
- `bg-white` → `bg-white dark:bg-graphite-surface-2`
- `text-gray-500` → `text-gray-500 dark:text-graphite-muted`
- `text-gray-700` → `text-gray-700 dark:text-graphite-text`
- `border-gray-200` / `border-gray-300` → add `dark:border-graphite-border`
- Sort chevron active color `text-indigo-600` → add `dark:text-graphite-accent`

---
_Filed by daily arch-review routine on 2026-07-06._
