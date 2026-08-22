# Implementation: validate-frontend-gates

## What was implemented

No source changes were required. This task runs the full merge gate defined
by arch review A-6 against the diff produced by the previous task
(`remove-dead-transitions-hook-and-plumbing`, commit `1f083c6`) and confirms
none of the six gate steps require a fix.

## Files created/modified

None. (Per the task context, any gate failure would have been fixed in the
previous task's six files and amended into commit `1f083c6` — no failure
occurred, so no amendment was made.)

## Gate results

- **Step 1 — Production build** (`npm run build`): `Compiled successfully.`
  Exit code 0. No `Attempted import error` and no
  `Property 'transportBoxTransitions' does not exist` — the removal order
  from the previous task holds. Bundle: 1.28 MB JS / 21.31 kB CSS gzip (the
  pre-existing "bundle size is significantly larger than recommended" CRA
  advisory is unrelated to this change; net source change is a decrease).

- **Step 2 — Lint** (`npm run lint`): exit code 1, but with **zero new
  problems**. Ran lint at the merge-base commit (`1690134`, `origin/main`)
  via a disposable worktree with a symlinked `node_modules` (same
  `package.json`/`package-lock.json`, confirmed unchanged in this diff) and
  diffed the two outputs byte-for-byte after normalizing worktree paths:
  identical — 188 pre-existing problems (175 errors, 13 warnings) in both,
  none touching transport-box files. The nonzero exit code is pre-existing
  repo lint debt, not something this change introduced; the task's
  acceptance criterion is "no new warnings versus the pre-change branch,"
  which holds.

- **Step 3 — Full Jest suite** (`CI=true REACT_APP_USE_MOCK_AUTH=true npm
  test -- --coverage --watchAll=false`, matching
  `.github/workflows/ci-feature-branch.yml:45`): exit code 0.
  `Test Suites: 311 passed, 311 total`. `Tests: 5 skipped, 2597 passed, 2602
  total` (the 5 skips are pre-existing and unrelated). `Snapshots: 2 passed,
  2 total`. No `coverageThreshold` is configured in `package.json`'s `jest`
  block, so the deleted-and-uncovered file could not and did not fail a
  coverage gate.

- **Step 4 — No E2E gate run**: not run, by design. Per
  `docs/architecture/testing-strategy.md:248-251` and
  `scripts/run-playwright-tests.sh:27,77`, the Playwright suite always
  targets deployed staging (`https://heblo.stg.anela.cz`), so running it
  pre-merge would exercise the deployed build, not this branch, and
  produces no evidence about this change. The `transport` project's nightly
  staging run (`box-workflow.spec.ts`, `box-management.spec.ts`,
  `boxes-basic.spec.ts`) is the post-deploy regression backstop; being
  green on the first run after deployment is the acceptance criterion. This
  reasoning belongs in the PR description per the task context.

- **Step 5 — Backend genuinely untouched**:
  `git diff --name-only origin/main...HEAD -- backend/
  frontend/src/api/generated/ docs/superpowers/` → empty output. Confirmed
  no `.cs` file changed; `dotnet build`/`dotnet format` correctly skipped.

- **Step 6 — No commit for this task**:
  `git status --short frontend docs` → empty output before writing this
  artifact (the gitignored `frontend/build/` from Step 1 did not appear in
  status). No source fix was needed, so nothing was amended into commit
  `1f083c6`. The only change from running this task is this artifact file
  itself plus the checkpoint state update, both under `artifacts/`.

## Tests

No new tests were written (validation-only task). The full existing Jest
suite (2602 tests across 311 suites) was executed as the gate itself; see
Step 3 above.

## How to verify

```bash
cd frontend
npm install --legacy-peer-deps   # matches CI's install invocation
npm run build                    # expect: Compiled successfully., exit 0
npm run lint                     # expect: same 188 pre-existing problems as origin/main, no new ones
CI=true REACT_APP_USE_MOCK_AUTH=true npm test -- --coverage --watchAll=false
                                  # expect: Test Suites: 311 passed, Tests: 2597 passed / 5 skipped, exit 0
cd ..
git diff --name-only origin/main...HEAD -- backend/ frontend/src/api/generated/ docs/superpowers/
                                  # expect: empty
```

## Notes

- `frontend/node_modules` was not present in the worktree before this task;
  it was installed fresh with `npm install --legacy-peer-deps` to match the
  exact flag CI uses (`.github/workflows/ci-feature-branch.yml:38`). A plain
  `npm install`/`npm ci` fails with an `ERESOLVE` conflict between
  `typescript@^4.9.5` (pinned by the project) and `react-i18next`'s
  `typescript@^5` peer-optional dependency — this is pre-existing repo
  state unrelated to this task, and `--legacy-peer-deps` is exactly how CI
  already handles it.
- Lint's nonzero exit code is flagged above only for completeness; it does
  not represent a regression and per the task context there is nothing to
  fix here — the six files touched in the previous task are not among the
  188 flagged problems.

## PR Summary
Ran the full frontend merge gate (production build, lint, full Jest suite
with coverage, backend-untouched check) against the dead-code removal from
the previous task. All gates pass: the build compiles cleanly with no
dangling references to the removed `useTransportBoxTransitions` hook, the
full 2602-test Jest suite is green, and lint output is byte-identical to
`origin/main` (188 pre-existing, unrelated problems — no new lint issues).
No source fix was needed, so no code was amended into the removal commit.
E2E was intentionally not run pre-merge, per
`docs/architecture/testing-strategy.md`, since the Playwright suite always
targets deployed staging rather than this branch; the nightly `transport`
project run against staging is the acceptance backstop instead.

### Changes
- No source files modified.
- `artifacts/feat-3889/impl/validate-frontend-gates.r1.md` — this summary.

## Status
DONE
