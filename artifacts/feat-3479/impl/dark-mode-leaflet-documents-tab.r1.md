# Implementation: dark-mode-leaflet-documents-tab

## What was implemented
Added Tailwind `dark:` class variants to every raw-color utility in
`LeafletDocumentsTab.tsx` per the task-context spec: the `StatusBadge` color
map (indexed/processing/failed/fallback), the `ConfirmDeleteDialog` panel,
body/warning text, and cancel button, the `SortableHeader` header cell and
chevron-active/inactive ternaries, the filter bar (container, icons, labels,
filename input, both `<select>` filters), the empty-state message, the
loading skeleton and error text, the results table (divide colors, thead,
tbody, row hover, cell text colors, delete icon button), and the pagination
footer (container, mobile prev/next buttons, result-count/label text,
page-size select, nav shadow, prev/page-number/next nav buttons, including
the active/inactive page-number ternary). The three solid action buttons
(delete-confirm "Smazat", "Filtrovat", "Vymazat") were left unchanged per
the spec's explicit exemption. No markup, props, state, sorting/pagination/
filtering logic, URL-param sync, permission checks, or API calls were
touched.

## Files created/modified
- `frontend/src/features/leaflet-generator/LeafletDocumentsTab.tsx` — added
  `dark:` Tailwind variants to all raw-color utility classes as enumerated in
  the task context file (status badges, delete dialog, sortable header,
  filter bar, table, pagination footer). Purely additive class-string
  changes.

## Tests
No new tests required — this is a styling-only change with no
markup/logic changes. Verified via:
- `npm run build` (from `frontend/`) — compiled successfully, no
  TypeScript/JSX errors.
- `npm run lint` (from `frontend/`) — 162 pre-existing problems (148
  errors, 14 warnings) across unrelated test files, confirmed identical
  before and after this change (verified via `git stash`/`git stash pop`
  comparison). Linting the changed file directly
  (`npx eslint src/features/leaflet-generator/LeafletDocumentsTab.tsx`)
  produced zero errors/warnings.

## How to verify
1. `cd frontend && npm install` (or reuse existing `node_modules`).
2. `npm run build` — should compile with no errors.
3. `npx eslint src/features/leaflet-generator/LeafletDocumentsTab.tsx` —
   should report no problems.
4. Run the app, open the Leaflet Documents tab, toggle to dark theme, and
   visually confirm: status badges render with distinct muted colors,
   the delete-confirmation dialog uses dark surface/border/text tokens,
   the filter bar/table/pagination footer all use graphite dark tokens
   instead of raw white/gray backgrounds, and the three solid action
   buttons (Smazat/Filtrovat/Vymazat) are unchanged (still solid
   indigo/red/gray with white text) in both themes.

## Notes
No deviations from the task spec. Class changes were applied by exact
string match as instructed; all line numbers in the spec were
approximate but every target string was unique and located successfully.
The `node_modules` directory for the worktree's `frontend/` was missing;
`npm install` failed due to a pre-existing peer-dependency conflict
(`react-i18next` requires `typescript@^5`, but the project pins
`typescript@^4.9.5`) unrelated to this change, so a symlink to the main
checkout's `frontend/node_modules` was used locally to run `build`/`lint`
and then removed before committing — it is not part of the commit.

## PR Summary
This change adds dark-mode Tailwind class variants throughout
`LeafletDocumentsTab.tsx` (status badges, delete-confirmation dialog,
sortable table header, filter bar, results table, and pagination footer)
so the Leaflet Documents tab renders correctly under the Graphite dark
theme, following the conventions in `docs/design/dark-mode-conversion-guide.md`
and ADR-006. The change is purely additive to `className` strings — no
markup, component logic, state, sorting/pagination/filtering behavior, or
API calls were altered. Three solid action buttons (delete-confirm,
Filtrovat, Vymazat) were intentionally left without `dark:` variants since
their saturated backgrounds with white text already have sufficient
contrast in both themes.

## Status
DONE
