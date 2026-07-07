# Implementation: dark-mode-leaflet-generator-page

## What was implemented
Added Tailwind `dark:` class variants to the page header and tab-bar navigation in
`LeafletGeneratorPage.tsx` so the tab shell renders correctly under the Graphite dark theme
(ADR-006), per `docs/design/dark-mode-conversion-guide.md`. The change is purely additive to
`className` strings — no markup structure, props, the `tabs` array, conditional tab-rendering
logic, or the `hasPermission('marketing.leaflet.write')` gating were touched.

## Files created/modified
- `frontend/src/features/leaflet-generator/LeafletGeneratorPage.tsx` —
  - Header icon: `text-blue-600` → added `dark:text-graphite-accent`.
  - Heading: `text-gray-900` → added `dark:text-graphite-text`.
  - Tab bar container: `border-gray-200` → added `dark:border-graphite-border`.
  - Active tab branch: `border-blue-600 text-blue-600` → added
    `dark:border-graphite-accent dark:text-graphite-accent`.
  - Inactive tab branch: `border-transparent text-gray-500 hover:text-gray-700` → added
    `dark:text-graphite-muted dark:hover:text-graphite-text`.

## Tests
No new tests required for this styling-only change (purely additive Tailwind `dark:` classes,
no logic changes). `npm run build` and `npm run lint` were run from `frontend/` to confirm the
change compiles and lints cleanly.

## How to verify
1. `cd frontend && npm install --legacy-peer-deps` (if `node_modules` is not already present).
2. `CI=true npm run build` — compiles successfully with no new errors.
3. `npm run lint` — no lint errors reported for `LeafletGeneratorPage.tsx` (pre-existing,
   unrelated lint errors exist elsewhere in the test suite, e.g. `testing-library/no-node-access`
   violations in various `__tests__` files — these are baseline issues, not introduced by this
   change).
4. Manually: toggle the app to Graphite dark theme and open the Leaflet Generator page
   (`/marketing/leaflet-generator` or equivalent route) to visually confirm the header icon,
   heading, tab bar bottom border, and active/inactive tab colors render using the Graphite
   dark tokens.

## Notes
- `node_modules` was not present in the worktree; installed with `npm install --legacy-peer-deps`
  to match the flag used in the repo's CI workflows (`ci-feature-branch.yml`,
  `ci-main-branch.yml`) due to a pre-existing `react-i18next`/`typescript` peer-dependency
  conflict unrelated to this change.
- `artifacts/feat-3479/state.json` had an unstaged modification (task status tracking, presumably
  updated by the orchestrator) at the time of this task; it was intentionally left out of this
  commit since it's not part of the requested source change.
- No deviations from the task spec; all four required class-level changes were applied exactly
  as specified.

## PR Summary
This change adds Tailwind `dark:` class variants to the header and tab-bar navigation of the
Leaflet Generator page so it renders correctly under the Graphite dark theme, following
ADR-006 and the project's dark-mode conversion guide. It is a surgical, additive-only CSS class
change — no markup, props, or logic were altered. Verified with a clean `npm run build` and
`npm run lint` (no new lint errors introduced).

## Status
DONE
