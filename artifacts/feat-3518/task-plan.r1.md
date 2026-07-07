# Task Plan: Dark-mode variants for shared grid components (GridHeader & ColumnChooser)

## Overview

This feature is a purely mechanical, CSS-only conversion: append `dark:` sibling Tailwind
utilities to the existing `className` strings in exactly two shared React components,
`GridHeader.tsx` and `ColumnChooser.tsx`, so they conform to ADR-006 (every color-rendering
component must work in both light and the Graphite dark theme). No logic, props, structure,
text, or light-mode classes change; no config or token changes; no new files.

The spec (`spec.r1.md`) and architecture review (`arch-review.r1.md`) already define every
change down to the per-line class string, and the architect re-verified all 13 line references
against the real source. There is nothing left to derive or design (Skip Design: true).
Decomposing a two-file append-only edit into multiple tasks would add coordination overhead
with zero benefit, so this is planned as a **single task** handed to one developer with one
reviewer pass.

### task: dark-mode-variants-gridheader-columnchooser

**Goal:** Add `dark:` sibling classes to the two shared grid-layout components so their sticky
headers and column-chooser dropdown render correctly in the Graphite dark theme, matching the
established append-in-place convention. Append-only: never remove, reorder, or rewrite any
existing (light-mode) class; never touch logic, props, structure, or text; leave
`focus:ring-indigo-500` focus rings untouched. Add the dark variant inside **each** branch of
every ternary consistently.

**Files:**
- `/home/user/worktrees/feature-3518-Arch-Review-Gridlayouts-Gridheader-And-Columnchoos/frontend/src/features/grid-layout/GridHeader.tsx`
- `/home/user/worktrees/feature-3518-Arch-Review-Gridlayouts-Gridheader-And-Columnchoos/frontend/src/features/grid-layout/ColumnChooser.tsx`

Do not touch any other file — not `types.ts`, `useGridLayout`, the grid body/rows, or
`tailwind.config.js`. Anchor edits on the element + existing class strings below (not raw line
numbers) in case lines have drifted.

**Details:**

Every token used already exists in `frontend/tailwind.config.js` (`graphite-surface` #202327,
`graphite-surface-2` #272A30, `graphite-border` #2D3138, `graphite-text` #E6E8EC,
`graphite-muted` #9AA0AA, `graphite-faint` #6A707A, `graphite-accent` #38BDF8,
`shadow-soft-dark`). Do **not** add tokens or edit config. `white/5` and `graphite-accent/20`
are opacity utilities on existing colors and are valid with JIT.

**`GridHeader.tsx`** — append the "Append" tokens onto the existing `className` string of each
element (existing tokens shown for anchoring; do not alter them):

| Line (approx) | Element | Existing (do not alter) | Append |
|------|---------|-------------------------|--------|
| 91  | `<th>` | `text-gray-500` | `dark:text-graphite-muted` |
| 99  | grip `<span>` | `text-gray-300 hover:text-gray-500` | `dark:text-graphite-faint dark:hover:text-graphite-muted` |
| 105 | sortable label `<span>` — **truthy ternary branch only** (`'cursor-pointer hover:text-gray-700'`); empty `''` branch needs nothing | `hover:text-gray-700` | `dark:hover:text-graphite-muted` |
| 110 | `ChevronUp` (ascending indicator ternary) | active `text-indigo-600` / inactive `text-gray-300` | active `dark:text-graphite-accent` / inactive `dark:text-graphite-faint` — **both branches** |
| 111 | `ChevronDown` (descending indicator ternary) | active `text-indigo-600` / inactive `text-gray-300` | active `dark:text-graphite-accent` / inactive `dark:text-graphite-faint` — **both branches** |
| 117 | resize handle `<div>` | `hover:bg-indigo-200` | `dark:hover:bg-graphite-accent/20` |
| 162 | `<thead>` | `bg-gray-50` | `dark:bg-graphite-surface-2` |

**`ColumnChooser.tsx`**:

| Line (approx) | Element | Existing (do not alter) | Append |
|------|---------|-------------------------|--------|
| 24 | trigger `<button>` — leave `focus:ring-indigo-500` as-is | `text-gray-600 border-gray-300 hover:bg-gray-50` | `dark:text-graphite-muted dark:border-graphite-border dark:hover:bg-white/5` |
| 35 | overlay `<div>` (`fixed inset-0 z-20`) | no color classes | **no change** |
| 38 | dropdown panel `<div>` | `bg-white border-gray-200 shadow-lg` | `dark:bg-graphite-surface dark:border-graphite-border dark:shadow-soft-dark` |
| 46 | column `<label>` | `text-gray-700 hover:text-gray-900` | `dark:text-graphite-muted dark:hover:text-graphite-text` |
| 51 | checkbox `<input>` — leave `focus:ring-indigo-500` as-is | `text-indigo-600 border-gray-300` | `dark:text-graphite-accent dark:border-graphite-border` |
| 61 | footer separator `<div>` | `border-gray-100` | `dark:border-graphite-border` |
| 64 | reset `<button>` | `text-gray-500 hover:text-gray-700 hover:bg-gray-50` | `dark:text-graphite-muted dark:hover:text-graphite-muted dark:hover:bg-white/5` |

Notes carried from the spec/arch-review (do not second-guess these — they are intentional and
grounded):
- `bg-gray-50 → dark:bg-graphite-surface-2` and `bg-white → dark:bg-graphite-surface` (the brief
  had these two inverted; the spec/guide/real code are authoritative and correct here).
- `hover:bg-indigo-200 → dark:hover:bg-graphite-accent/20` and `shadow-lg → dark:shadow-soft-dark`
  have no literal guide entry but are within-system, reasoned choices — apply as written, do not
  substitute.

**Acceptance criteria:**
- Every `className` in `GridHeader.tsx` containing a `gray`/`indigo` color utility also contains
  its corresponding `dark:` variant per the table above (all 7 edit sites, both ternary branches
  on lines 110 and 111 converted).
- Every `className` in `ColumnChooser.tsx` containing a `gray`/`indigo`/`white`/`shadow` color
  utility also contains its corresponding `dark:` variant per the table above, **except** the two
  `focus:ring-indigo-500` rings (lines 24, 51), which remain untouched.
- No light-mode class is removed, reordered, or altered anywhere; the diff shows **only additions
  of `dark:` tokens** (append-only). No JSX logic, props, structure, event handlers, or text
  (including the Czech labels "Sloupce" / "Reset rozvržení") is changed.
- Only existing `graphite-*` tokens plus the `white/5` and `graphite-accent/20` opacity utilities
  are used; `tailwind.config.js` is not modified and no new token or hex color is introduced.
- Overlay `<div>` (`fixed inset-0 z-20`) and all layout/spacing classes are untouched.
- From `frontend/`: `npm run build` passes and `npm run lint` passes. No unit tests assert these
  class strings, so none need updating.
