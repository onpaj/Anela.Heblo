## Module
Leaflet

## Finding
Every component in the `frontend/src/features/leaflet-generator/` feature directory uses light-only Tailwind classes with no `dark:` counterparts. ADR-006 (accepted 2026-06-25) requires every component that renders color to work correctly in both the light and Graphite dark themes.

Affected files and example violations:

**`LeafletGeneratorPage.tsx`** (lines 26–55)
- Tab nav: `border-gray-200`, `text-gray-500`, `hover:text-gray-700`, `text-gray-900`
- Active tab indicator: `border-blue-600 text-blue-600` — no `dark:` variant

**`LeafletDocumentsTab.tsx`** (entire component, 515 lines)
- Filter bar card: `bg-white shadow` — renders as white on dark background
- Table: `divide-gray-200`, `bg-gray-50` (header), `divide-gray-100` (rows), `hover:bg-gray-50`
- Sortable header: `text-gray-500`, `hover:bg-gray-100`, `text-gray-300`, `text-indigo-600`
- Status badges: `bg-green-100 text-green-800`, `bg-yellow-100 text-yellow-800`, `bg-red-100 text-red-800` — raw Tailwind hues with no dark variant
- Cell text: `text-gray-900`, `text-gray-500`
- Delete confirmation dialog: `bg-white rounded-lg`, `text-gray-600`
- Pagination: `bg-white`, `border-gray-300`, `text-gray-700`, `hover:bg-gray-50`

**`LeafletGenerateTab.tsx`** (lines 63–70, 87–90)
- Error banners: `bg-amber-100 text-amber-900`, `bg-red-100 text-red-900` — no dark counterparts
- Loading skeleton: `bg-gray-200` — visible as bright stripe on dark background

## Why it matters
ADR-006 states: "Every frontend component that renders color **must render correctly in both light and dark mode**. Applies to all routes, modals, drawers, tab panels, tables, forms, badges, and shared components." The Leaflet feature is one of the most visually complex screens (table, filter bar, modal, tab panel) and will render with broken contrast in Graphite dark mode.

## Suggested fix
For each component, add `dark:` variants following the `docs/design/dark-mode-conversion-guide.md` token map:
- `bg-white` → `dark:bg-graphite-surface`
- `text-gray-900` → `dark:text-graphite-text`
- `text-gray-500` / `text-gray-600` → `dark:text-graphite-muted`
- `border-gray-200` / `border-gray-300` → `dark:border-graphite-border`
- `bg-gray-50` / `hover:bg-gray-50` / `hover:bg-gray-100` → `dark:bg-graphite-surface-2` / `dark:hover:bg-graphite-surface-2`
- `divide-gray-200` / `divide-gray-100` → `dark:divide-graphite-border`
- Status badges: use `~900/30` bg + `~300` text pattern in dark (e.g., `dark:bg-green-900/30 dark:text-green-300`)

Start with `LeafletDocumentsTab.tsx` (most impact) and proceed through the other components.

---
_Filed by daily arch-review routine on 2026-07-04._
