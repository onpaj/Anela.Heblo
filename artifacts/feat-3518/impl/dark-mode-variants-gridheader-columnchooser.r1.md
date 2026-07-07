# Implementation: dark-mode-variants-gridheader-columnchooser

## What was implemented
Added Tailwind `dark:` sibling classes to the shared `GridHeader` and `ColumnChooser` grid-layout
components so their sticky header row, sort/resize affordances, and column-chooser dropdown
render correctly in the Graphite dark theme. All edits are append-only onto existing `className`
strings, exactly per the task-context table, using only pre-existing `graphite-*` tokens plus the
`white/5` and `graphite-accent/20` opacity utilities already defined in
`frontend/tailwind.config.js`. Both branches of every ternary (chevron up/down active/inactive
state, sortable-label hover) were converted consistently. `focus:ring-indigo-500` rings on the
column-chooser trigger button and checkbox were left untouched, as were the overlay `<div>`,
layout/spacing classes, JSX structure, props, event handlers, and the Czech labels ("Sloupce",
"Reset rozvržení").

## Files created/modified
- `frontend/src/features/grid-layout/GridHeader.tsx` — appended `dark:` variants to 7 sites: `<th>`
  text color, drag-grip icon color, sortable-label hover color, `ChevronUp`/`ChevronDown` active
  and inactive colors (both ternary branches), resize-handle hover background, and `<thead>`
  background.
- `frontend/src/features/grid-layout/ColumnChooser.tsx` — appended `dark:` variants to 6 sites:
  trigger button text/border/hover background, dropdown panel background/border/shadow, column
  `<label>` text/hover color, checkbox accent/border color, footer separator border, and reset
  button text/hover color/hover background.

## Tests
No unit tests assert these class strings, so none needed updating. Verification instead relied on
`npm run build` (compiles/typechecks the JSX template literals and ternaries) and `npm run lint`
(catches any malformed className expressions), plus a manual `git diff` review of both files.

## How to verify
- `cd frontend && npm run build` — compiles successfully (`Compiled successfully.`).
- `cd frontend && npx eslint src/features/grid-layout/GridHeader.tsx src/features/grid-layout/ColumnChooser.tsx`
  — zero errors/warnings on the two changed files. (Note: the repo-wide `npm run lint` has 148
  pre-existing errors/14 warnings in unrelated `__tests__` files; confirmed via `git stash` that
  this count is identical before and after this change, i.e. pre-existing and unrelated.)
- `git diff -- frontend/src/features/grid-layout/GridHeader.tsx frontend/src/features/grid-layout/ColumnChooser.tsx`
  — review confirms every changed line is a pure addition of `dark:` tokens onto the existing
  class string; no light-mode class was removed, reordered, or altered; no non-class code changed.

## Notes
- `node_modules` did not exist in this worktree; had to run `npm install --legacy-peer-deps` (the
  root `npm install` fails with an ERESOLVE conflict between `react-i18next@15.7.4` wanting
  TypeScript 5 and the pinned `typescript@^4.9.5` — pre-existing repo condition, unrelated to this
  change) before `npm run build` / `npm run lint` could run.
- No deviations from the task-context table; the two brief-vs-guide inversions it called out
  (`bg-gray-50 → dark:bg-graphite-surface-2`, `bg-white → dark:bg-graphite-surface`) were applied
  exactly as specified as authoritative.

## PR Summary
This change adds dark-mode Tailwind variants to `GridHeader.tsx` and `ColumnChooser.tsx`, the two
shared grid-layout components, so the sticky column header (text, drag grip, sort chevrons, resize
handle, thead background) and the column-chooser dropdown (trigger button, panel, checkboxes,
labels, reset button) render correctly under the Graphite dark theme. All edits are strictly
additive `dark:` classes appended onto existing class strings using tokens already defined in
`tailwind.config.js` — no light-mode class was removed or reordered, no JSX logic/structure/text
changed, and the `focus:ring-indigo-500` focus rings were left untouched as required. Both
branches of each ternary (chevron up/down active/inactive, sortable-label hover) were converted
consistently. Verified with `npm run build` (compiles successfully) and targeted `eslint` on the
two files (zero issues); the repo-wide `npm run lint` failure count (148 errors/14 warnings) was
confirmed unchanged before/after via `git stash`, i.e. pre-existing and unrelated to this change.

### Changes
- `frontend/src/features/grid-layout/GridHeader.tsx` — appended `dark:` variants to `<th>` text
  color, drag-grip color, sortable-label hover, chevron active/inactive colors (both ternaries),
  resize-handle hover background, and `<thead>` background.
- `frontend/src/features/grid-layout/ColumnChooser.tsx` — appended `dark:` variants to trigger
  button, dropdown panel, column label, checkbox, footer separator, and reset button.

## Status
DONE
