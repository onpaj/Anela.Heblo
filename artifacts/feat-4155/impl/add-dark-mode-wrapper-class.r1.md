# Implementation: add-dark-mode-wrapper-class

## What was implemented

Added the missing `dark:` Tailwind variant to `OrgChartPage.tsx`'s outermost
wrapper `<div>`, per ADR-006 ("Light and Dark Mode Required for Every
Frontend Component"). The wrapper previously had a light-mode-only
`bg-gradient-to-br from-indigo-500 to-purple-600` background with no dark
override, which bled through in dark mode (the wrapper is `h-screen` while
its children can exceed viewport height). The fix reuses the exact
`dark:from-graphite-bg dark:to-graphite-bg` pattern already used on the
chart-canvas wrapper at line 276 of the same file.

## Files created/modified

- `frontend/src/pages/OrgChartPage.tsx` — outer wrapper `className` (line 195)
  changed from `"h-screen bg-gradient-to-br from-indigo-500 to-purple-600
  flex flex-col"` to `"h-screen bg-gradient-to-br from-indigo-500
  to-purple-600 dark:from-graphite-bg dark:to-graphite-bg flex flex-col"`.
  This is the only line changed in the file.

## Tests

No new automated tests were added. Per the task spec, this is a
visual-only Tailwind class change with no new WCAG contrast requirement
(NFR-1), and the existing test suite for this component (if any) does not
assert on this class string. Verification is via build/lint gates plus
manual visual check in both themes (see below).

## How to verify

1. `git -C frontend diff -- src/pages/OrgChartPage.tsx` shows exactly one
   changed line, adding only `dark:from-graphite-bg dark:to-graphite-bg`.
2. `cd frontend && npm run build` — succeeds with no new TypeScript or
   Tailwind errors (confirmed: "Compiled successfully").
3. `npx eslint src/pages/OrgChartPage.tsx` — zero errors on the changed
   file itself.
4. Manual: toggle theme on the OrgChart page — light mode still shows the
   indigo-to-purple gradient unchanged; dark mode shows the flat
   `graphite-bg` color matching the chart canvas area beneath it, with no
   bright gradient bleeding through during scroll/overscroll.

## Notes

- `npm run lint` (full project lint) reports 236 pre-existing errors, all
  in unrelated test files elsewhere in the repo (e.g.
  `MarketingActionModal.test.tsx`, `OvertimePage.test.tsx`,
  `FinancialDataCards.test.tsx`, etc. — mostly `testing-library/no-node-access`
  and `testing-library/no-wait-for-*` rule violations). None are in
  `OrgChartPage.tsx`; running `npx eslint` scoped to just this file
  produces zero output/errors. These lint failures are pre-existing and
  out of scope for this one-line, additive-only change.
- Local `node_modules` in this worktree had to be installed with
  `npm ci --legacy-peer-deps` (plain `npm ci` fails on a pre-existing
  `knip`/`@types/node` peer-dependency conflict in `package.json`,
  unrelated to this task) purely to run the build/lint verification
  commands locally — no `package.json` or lockfile was modified.
- Step 6 of the task context (manual visual verification via a running
  dev server / Playwright instance) requires a browser and cannot be
  performed by this headless agent; the change is a straight reuse of an
  already-verified pattern (identical to line 276 in the same file), so
  visual correctness follows directly from that reuse.

## PR Summary
Fixed a dark-mode gap on the Org Chart page: the page's outermost wrapper had a light-mode-only indigo-to-purple gradient background with no `dark:` override, which bled through during scroll/overscroll in dark mode. Added the same `dark:from-graphite-bg dark:to-graphite-bg` override already used on the chart-canvas wrapper further down in the same file, so the outer wrapper is consistently themed with the rest of the page.

### Changes
- `frontend/src/pages/OrgChartPage.tsx` — added `dark:from-graphite-bg dark:to-graphite-bg` to the outer wrapper's `className` (one line changed)

## Status
DONE
