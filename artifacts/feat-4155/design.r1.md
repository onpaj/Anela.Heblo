# Design: OrgChartPage outer wrapper missing dark mode theming (ADR-006)

No UX/UI design work is required for this change. The architecture review sets `Skip Design: true`: the fix adds a `dark:` Tailwind variant to an already-existing element, reusing a color token (`graphite-bg`) and an override pattern (`dark:from-X dark:to-X`) already established one element below in the same file (`OrgChartPage.tsx:276`). There is no new component, no new interaction, no new layout, and no new visual decision to make — light-mode appearance is unchanged, and dark-mode appearance is brought into line with the existing Graphite theme already applied to every sibling element in this page.

## Component Design

**Component:** `OrgChartPage` (`frontend/src/pages/OrgChartPage.tsx`) — no structural change.

- **Outer wrapper `<div>`** (currently line 195): sole element touched by this change. Its responsibility (full-viewport flex container providing the page background behind the controls bar and chart canvas) is unchanged; only its dark-mode background color is corrected.
- **Controls bar** (line 197) and **chart canvas wrapper** (line 276): unchanged, already correctly themed, included here only to note that the fix must remain visually consistent with them (same `graphite-bg` token as the canvas wrapper directly below).

No new components, no new props, no new state, no changes to `buildTree`, `renderConnections`, filters, or zoom logic.

## Data Schemas

Not applicable. This change touches only a static `className` string on a presentational element — no request/response shapes, events, or persisted schemas are involved.
