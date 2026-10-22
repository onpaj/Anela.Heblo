## Module
Leaflet

## Finding
`frontend/src/features/leaflet-generator/LeafletForm.tsx` contains no `dark:` Tailwind classes on any element that carries a color.

Concrete gaps (all in `LeafletForm.tsx`):

| Line(s) | Element | Missing |
|---------|---------|---------|
| 42 | `<label>` — `text-gray-700` | `dark:text-graphite-text` |
| 54 | `<input>` — `border-gray-300` | `dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint` |
| 56 | character-count `<span>` — `text-gray-500` | `dark:text-graphite-muted` |
| 63, 91 | `<legend>` — `text-gray-700` | `dark:text-graphite-text` |
| 66–85, 93–122 | radio `<label>` — default text | `dark:text-graphite-text` |
| 130 | submit `<button>` background — `bg-blue-600 hover:bg-blue-700` | no dark override needed for this one, but the disabled text contrast should be checked |

Additionally, `LeafletResult.tsx` lines 66–78 — the "Kopírovat" and "Generovat znovu" buttons use `border-gray-300 hover:bg-gray-50` with no `dark:` sibling, so both buttons are invisible-border + transparent-background in dark mode.

```tsx
// LeafletResult.tsx:66-78 — missing dark: variants
className="px-4 py-2 text-sm font-medium border border-gray-300 rounded-md hover:bg-gray-50"
```

## Why it matters
ADR-006 (accepted 2026-06-25) requires every frontend component that renders color to have both light and dark variants. The Leaflet generate tab is the primary user-facing feature of this module; broken contrast in dark (Graphite) mode degrades usability for dark-mode users and violates the accepted decision.

## Suggested fix
Apply the same graphite tokens already used in adjacent components (`LeafletGenerateTab.tsx`, `LeafletDocumentsTab.tsx`):

```tsx
// Label
<label className="block text-sm font-medium text-gray-700 dark:text-graphite-text">

// Input
<input className="... border-gray-300 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint">

// Character counter
<span className="text-xs text-gray-500 dark:text-graphite-muted">

// LeafletResult action buttons
<button className="... border border-gray-300 hover:bg-gray-50 dark:border-graphite-border dark:hover:bg-white/5 dark:text-graphite-text">
```

---
_Filed by daily arch-review routine on 2026-10-06._
