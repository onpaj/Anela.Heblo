# Implementation: dark-mode-baleni-statistics-jsx

## What was implemented
Added Tailwind `dark:` variant classes to every existing `className` string in
`BaleniStatistics.tsx` (the packing-statistics screen container) so it renders correctly
under Graphite dark mode. Only `dark:` classes were appended — no light-mode class was
removed, reordered, or rewritten, and no imports/props/JSX structure changed. This
mirrors the precedent pattern already used in `BaleniHome.tsx` and needs no `useTheme()`
import since Tailwind's `dark:` variant resolves purely via the CSS cascade against
`<html class="dark">`.

Edits covered: `Panel` wrapper/title/subtitle, `KpiCard` wrapper/label/loading-pulse/value,
the error banner/icon/heading/body/retry-button, the H1 heading/icon and date-range
subtitle, the range-preset button ternary (both active and inactive branches got matching
dark siblings), the refresh button, and the full-page loading box/spinner/label.

## Files created/modified
- `frontend/src/components/baleni/statistics/BaleniStatistics.tsx` — appended `dark:` Tailwind classes to 15 `className` locations (21 total `dark:` occurrences), per the exact before/after mapping in the task context. No other changes.

## Tests
- `frontend/src/components/baleni/statistics/__tests__/BaleniStatistics.test.tsx` — existing test file, run unmodified. All 5 tests pass (loading state, error state with retry, KPI/panel rendering, packer-attribution hint shown/omitted). It asserts on text content and structure, not class strings, so it was unaffected by this change as expected.

## How to verify
```bash
cd frontend
grep -c "dark:" src/components/baleni/statistics/BaleniStatistics.tsx   # => 21 (>= 20 expected)
CI=true npm test -- --testPathPattern=BaleniStatistics                  # => 5 passed
npm run build                                                            # => Compiled successfully
npm run lint                                                             # => 188 problems (175 errors, 13 warnings), identical to pre-change baseline (verified via git stash), 0 in BaleniStatistics.tsx itself
```

## Notes
- `frontend/node_modules` was not present in this checkout; installed via `npm install --legacy-peer-deps` (there is a known upstream ERESOLVE conflict between `react-i18next@15.7.4` wanting `typescript@^5` and the project's pinned `typescript@^4.9.5`, unrelated to this change). `package-lock.json` was verified unchanged after install (`git status --short package-lock.json` empty), so nothing outside the target file was committed.
- Lint run showed 188 pre-existing problems repo-wide; confirmed via `git stash`/`git stash pop` that this count is identical with and without this change, and `npx eslint` on `BaleniStatistics.tsx` alone reports zero issues. No new lint errors were introduced.
- `artifacts/feat-3872/state.json` was already modified in the working tree before this task started (pipeline-managed file, out of scope) and was deliberately left uncommitted, per the instruction to commit only the target `.tsx` file.
- Manual/visual spot-check via a running dev server was not performed (no dev server available in this non-interactive environment); the class-string diff was instead double-checked by eye against the task's before/after mapping table, and it matches exactly.

## PR Summary
Adds Graphite dark-mode Tailwind classes to `BaleniStatistics.tsx`, the packing-statistics
screen container, which previously had zero `dark:` variants and rendered illegibly (white
panels/cards, light-gray text) against the app's dark background when Graphite dark mode is
active. Only `dark:` classes were appended to existing `className` strings — no light-mode
classes, logic, props, or JSX structure were touched. Follows the same
no-`useTheme()`-needed pattern already established in `BaleniHome.tsx`.

### Changes
- `frontend/src/components/baleni/statistics/BaleniStatistics.tsx` — appended `dark:` classes to the `Panel` and `KpiCard` sub-components, the error banner/retry-button, the header (H1/icon/date subtitle), the range-preset button ternary (both branches), the refresh button, and the full-page loading state.

## Status
DONE
