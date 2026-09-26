# Implementation: wire-target-url-in-tile-registry

## What was implemented

Updated the `packingstats` entry in `frontend/src/components/dashboard/tiles/tileRegistry.tsx`
to pass `targetUrl="/baleni"` into `PackingStatsTile`, wiring up the `targetUrl`
prop that `PackingStatsTile` gained in the prior task
(`refactor-packing-stats-tile-drilldown`). This restores the tile's drill-down
navigation to `/baleni` (now built through the shared `urlUtils` helpers and
`drillDown.filters`, instead of the old hardcoded/ignored-filters behavior).

## Files created/modified

- `frontend/src/components/dashboard/tiles/tileRegistry.tsx` — the single-line
  change described in the task context, from
  `packingstats: ({ data }) => <PackingStatsTile data={data} />,` to
  `packingstats: ({ data }) => <PackingStatsTile data={data} targetUrl="/baleni" />,`.
  No other change in this file.

## Tests

No new test file (none required by the task context — `PackingStatsTile`'s own
test suite already covers `targetUrl` behavior directly via props and does not
import `tileRegistry.tsx`).

## How to verify

```bash
cd frontend
npm install --legacy-peer-deps   # node_modules was not pre-installed in this worktree
CI=true npx react-scripts test src/components/dashboard/tiles/__tests__/PackingStatsTile.test.tsx --watchAll=false
npm run build
npm run lint
CI=true npm test -- --watchAll=false
```

## Results

- `PackingStatsTile.test.tsx`: 7/7 passed (unaffected by this file, as expected).
- `npm run build`: `Compiled successfully.` (exit 0).
- `npm run lint`: `npx eslint src/components/dashboard/tiles/tileRegistry.tsx`
  alone reports zero problems — the changed file introduces no new lint
  errors. The full `npm run lint` run reports 245 pre-existing errors / 13
  warnings across unrelated test files elsewhere in the codebase (mostly
  `testing-library/no-node-access`); confirmed pre-existing and unrelated by
  checking `git diff --stat`, which shows only `tileRegistry.tsx` (plus this
  artifact file) changed.
- Full frontend suite (`npm test -- --watchAll=false`): `Test Suites: 384
  passed, 384 total`, `Tests: 5 skipped, 3342 passed, 3347 total`, exit code
  0. No regressions from the `targetUrl` addition.

## Notes

- `node_modules` was missing in this worktree (fresh worktree checkout);
  installed with `npm install --legacy-peer-deps`, matching the flag used by
  this repo's own CI workflows (`ci-feature-branch.yml`, `ci-main-branch.yml`)
  — plain `npm ci`/`npm install` fails on a pre-existing `@types/node` peer
  dependency conflict between `knip` and the CRA toolchain, unrelated to this
  change.
- This was the last pending task for feat-4314's `developing` phase per
  `state.json` — no further developer tasks remain after this one.

## PR Summary
Wires the `targetUrl="/baleni"` prop into the `packingstats` entry of
`tileRegistry.tsx`, completing the drill-down fix started by
`refactor-packing-stats-tile-drilldown`: the Packing Stats dashboard tile now
actually navigates to `/baleni` (with `drillDown.filters` as query params)
instead of silently doing nothing.

### Changes
- `frontend/src/components/dashboard/tiles/tileRegistry.tsx` — pass
  `targetUrl="/baleni"` into `PackingStatsTile`

## Status
DONE
