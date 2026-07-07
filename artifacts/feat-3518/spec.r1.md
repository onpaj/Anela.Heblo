# Specification: Dark-mode variants for shared grid components (GridHeader & ColumnChooser)

## Summary
The two shared React grid components `GridHeader.tsx` and `ColumnChooser.tsx` in `frontend/src/features/grid-layout/` currently use only light-mode Tailwind utilities and have no `dark:` variants, violating ADR-006 (every color-rendering component must work in both light and the Graphite dark theme). Because these components back every grid that uses `useGridLayout`, their sticky headers and column-chooser dropdown render with broken contrast across the whole app in dark mode. This is a mechanical, CSS-only change: append `dark:` sibling classes to existing `className` strings per the project's `docs/design/dark-mode-conversion-guide.md`, without altering any light classes, logic, structure, props, or text.

## Background
The app uses Tailwind with `darkMode: 'class'` (see `frontend/tailwind.config.js`) and a dedicated `graphite-*` dark-mode color scale. ADR-006 (accepted 2026-06-25) requires all color-rendering components to support both themes. Most feature pages have already been converted (e.g. `frontend/src/pages/InvoiceClassification/ClassificationHistoryPage.tsx`, `GroupDetailPage.tsx`), but the shared grid-layout components were missed. Since these are shared, the omission degrades dark mode on every grid page simultaneously, making them a high-leverage fix.

The daily arch-review routine filed a brief (`artifacts/feat-3518/brief.md`) suggesting token mappings. **Two of the brief's suggested mappings are inverted relative to the actual codebase convention** and are corrected in this spec:
- Brief said `bg-gray-50 → dark:bg-graphite-surface` and `bg-white → dark:bg-graphite-surface-2`.
- The authoritative `dark-mode-conversion-guide.md` **and** real converted code specify the opposite: `bg-gray-50 → dark:bg-graphite-surface-2` (confirmed on a real `thead` at `ClassificationHistoryPage.tsx:261`) and `bg-white → dark:bg-graphite-surface` (confirmed at `GroupDetailPage.tsx:284`). This spec follows the guide and real code, not the brief.

The Graphite tokens defined in `tailwind.config.js` used here: `graphite-surface` (#202327), `graphite-surface-2` (#272A30), `graphite-border` (#2D3138), `graphite-text` (#E6E8EC), `graphite-muted` (#9AA0AA), `graphite-faint` (#6A707A), `graphite-accent` (#38BDF8).

## Functional Requirements

### FR-1: GridHeader dark-mode variants
Add `dark:` sibling classes to every light-only colored utility in `frontend/src/features/grid-layout/GridHeader.tsx`. Existing light classes, ordering, logic, and structure must be preserved; only append `dark:` utilities to the relevant `className` strings.

Exact changes (line numbers from current source; may drift):
- **Line 91 — header cell `<th>`**: `text-gray-500` → append `dark:text-graphite-muted`.
- **Line 99 — grip icon `<span>`**: `text-gray-300` → append `dark:text-graphite-faint`; `hover:text-gray-500` → append `dark:hover:text-graphite-muted`.
- **Line 105 — sortable label `<span>` (conditional string)**: inside the truthy branch `'cursor-pointer hover:text-gray-700'`, `hover:text-gray-700` → append `dark:hover:text-graphite-muted`. (Not called out in the brief, but it is a light-only class and must be converted for consistency; the empty `''` branch needs nothing.)
- **Line 110 — `ChevronUp` (ascending indicator)**: in the ternary, active `text-indigo-600` → append `dark:text-graphite-accent`; inactive `text-gray-300` → append `dark:text-graphite-faint`. Both ternary branches must be converted consistently.
- **Line 111 — `ChevronDown` (descending indicator)**: identical to line 110 — active `text-indigo-600` → append `dark:text-graphite-accent`; inactive `text-gray-300` → append `dark:text-graphite-faint`.
- **Line 117 — resize handle `<div>`**: `hover:bg-indigo-200` → append `dark:hover:bg-graphite-accent/20`. (No exact guide entry exists for an indigo *hover background*; the guide maps the active accent background `bg-indigo-50 → dark:bg-graphite-accent/10`. A slightly stronger `/20` tint is used here because `indigo-200` is a stronger light value than `indigo-50` and this is a transient hover affordance. Uses only existing design-system tokens.)
- **Line 162 — `<thead>`**: `bg-gray-50` → append `dark:bg-graphite-surface-2` (matches the real converted `thead` pattern at `ClassificationHistoryPage.tsx:261`).

**Acceptance criteria:**
- Every `className` in `GridHeader.tsx` that contains a `gray`/`indigo` color utility also contains a corresponding `dark:` variant per the mappings above.
- No light-mode class is removed, reordered, or altered; no JSX logic, props, structure, or text is changed.
- Focus-ring classes (none present in this file) and layout/spacing classes are untouched.
- `npm run build` and `npm run lint` pass.

### FR-2: ColumnChooser dark-mode variants
Add `dark:` sibling classes to every light-only colored utility in `frontend/src/features/grid-layout/ColumnChooser.tsx`, following the same append-only rules.

Exact changes (line numbers from current source; may drift):
- **Line 24 — trigger `<button>`**: `text-gray-600` → append `dark:text-graphite-muted`; `border-gray-300` → append `dark:border-graphite-border`; `hover:bg-gray-50` → append `dark:hover:bg-white/5`. Leave `focus:ring-indigo-500` as-is (guide: focus rings stay).
- **Line 35 — overlay `<div>` (`fixed inset-0 z-20`)**: no color classes; no change.
- **Line 38 — dropdown panel `<div>`**: `bg-white` → append `dark:bg-graphite-surface`; `border-gray-200` → append `dark:border-graphite-border`; `shadow-lg` → append `dark:shadow-soft-dark` (guide maps shadow utilities to `dark:shadow-soft-dark`).
- **Line 46 — column `<label>`**: `text-gray-700` → append `dark:text-graphite-muted`; `hover:text-gray-900` → append `dark:hover:text-graphite-text`.
- **Line 51 — checkbox `<input>`**: `text-indigo-600` (checkbox accent) → append `dark:text-graphite-accent`; `border-gray-300` → append `dark:border-graphite-border`. Leave `focus:ring-indigo-500` as-is.
- **Line 61 — footer separator `<div>`**: `border-gray-100` → append `dark:border-graphite-border`.
- **Line 64 — reset `<button>`**: `text-gray-500` → append `dark:text-graphite-muted`; `hover:text-gray-700` → append `dark:hover:text-graphite-muted`; `hover:bg-gray-50` → append `dark:hover:bg-white/5`.

**Acceptance criteria:**
- Every `className` in `ColumnChooser.tsx` that contains a `gray`/`indigo`/`white`/`shadow` color utility also contains a corresponding `dark:` variant per the mappings above (focus rings excepted).
- No light-mode class is removed, reordered, or altered; no JSX logic, props, structure, or text (including the Czech labels "Sloupce" / "Reset rozvržení") is changed.
- `npm run build` and `npm run lint` pass.

### FR-3: Conformance to conversion guide and existing convention
All added variants must use the `graphite-*` tokens and hover conventions defined in `docs/design/dark-mode-conversion-guide.md` and `frontend/tailwind.config.js`, and must match the patterns used by already-converted components (e.g. `ClassificationHistoryPage.tsx`, `GroupDetailPage.tsx`). Do not introduce new tokens or ad-hoc hex colors.

**Acceptance criteria:**
- No new colors/tokens are added to `tailwind.config.js`; only existing `graphite-*` tokens and `white/5`, `graphite-accent/20` opacity utilities are used.
- The two files match the brief's *intent* while using the corrected (guide-accurate) token names for `bg-gray-50` and `bg-white`.

## Non-Functional Requirements

### NFR-1: Performance
N/A. This is a static Tailwind class-string change with zero runtime or bundle-size impact of significance (a handful of additional utility classes generated at build time).

### NFR-2: Security
N/A. Presentational CSS only; no auth, data access, input handling, or sensitive-data surface is touched.

## Data Model
N/A. No entities, database, or persisted state are involved.

## API / Interface Design
No backend endpoints, events, or component APIs change. The only interface affected is the rendered appearance of two shared UI components under the `dark` class on the document root. Summary of exactly which classes change:

**`frontend/src/features/grid-layout/GridHeader.tsx`**
| Line | Element | Add |
|------|---------|-----|
| 91 | `<th>` | `dark:text-graphite-muted` |
| 99 | grip `<span>` | `dark:text-graphite-faint`, `dark:hover:text-graphite-muted` |
| 105 | label `<span>` | `dark:hover:text-graphite-muted` (truthy branch) |
| 110 | `ChevronUp` | `dark:text-graphite-accent` (active), `dark:text-graphite-faint` (inactive) |
| 111 | `ChevronDown` | `dark:text-graphite-accent` (active), `dark:text-graphite-faint` (inactive) |
| 117 | resize handle | `dark:hover:bg-graphite-accent/20` |
| 162 | `<thead>` | `dark:bg-graphite-surface-2` |

**`frontend/src/features/grid-layout/ColumnChooser.tsx`**
| Line | Element | Add |
|------|---------|-----|
| 24 | trigger `<button>` | `dark:text-graphite-muted`, `dark:border-graphite-border`, `dark:hover:bg-white/5` |
| 38 | dropdown panel | `dark:bg-graphite-surface`, `dark:border-graphite-border`, `dark:shadow-soft-dark` |
| 46 | column `<label>` | `dark:text-graphite-muted`, `dark:hover:text-graphite-text` |
| 51 | checkbox `<input>` | `dark:text-graphite-accent`, `dark:border-graphite-border` |
| 61 | footer separator | `dark:border-graphite-border` |
| 64 | reset `<button>` | `dark:text-graphite-muted`, `dark:hover:text-graphite-muted`, `dark:hover:bg-white/5` |

## Dependencies
- Tailwind CSS with `darkMode: 'class'` and the `graphite-*` color scale already defined in `frontend/tailwind.config.js` (present — no config change required).
- Conventions in `docs/design/dark-mode-conversion-guide.md` (source of truth for mappings).
- No new libraries, services, or feature flags.

## Out of Scope
- Any change to light-mode appearance or existing light classes.
- Any change to JSX logic, component props, structure, event handlers, or text/labels.
- Converting other components in `frontend/src/features/grid-layout/` (e.g. `types.ts`, the grid body/rows, `useGridLayout` hook) or any other files — only `GridHeader.tsx` and `ColumnChooser.tsx` are in scope.
- Adding new `graphite-*` tokens or modifying `tailwind.config.js`.
- Automated tests for dark-mode styling (styling verified via build/lint and visual review; no unit test asserts class strings for these components).
- The `focus:ring-indigo-500` focus rings, which the guide explicitly says to leave as-is.

## Open Questions
None. The scope is fully determined: the conversion guide plus real converted components resolve every mapping, including the one class without a literal guide entry (`hover:bg-indigo-200`), for which `dark:hover:bg-graphite-accent/20` is a defensible in-system choice noted in FR-1.

## Status: COMPLETE
