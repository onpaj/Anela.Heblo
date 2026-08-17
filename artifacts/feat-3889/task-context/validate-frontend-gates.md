### task: validate-frontend-gates

Run the full merge gate defined by arch review A-6. This task changes no source file; if a gate fails, fix the cause in the previous task's files and amend that commit (`git commit --amend --no-edit`) so the removal stays a single commit.

**Files:** none modified (fixes, if any, go back into the six files listed in the previous task and are amended into its commit).

- [ ] **Step 1: Production build**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti/frontend
npm run build
```

Expected: `Compiled successfully.` (warnings that already existed on the branch before this change are acceptable; a *new* warning is not). Exit code 0. There must be no `Attempted import error` or `Property 'transportBoxTransitions' does not exist` — either would mean the removal order in the previous task was not followed.

Note: the build must not grow. There is no bundle-size budget in this repo, so no numeric threshold applies; a marginal decrease (~49 lines of source plus one `QUERY_KEYS` entry) is expected.

- [ ] **Step 2: Lint**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti/frontend
npm run lint
```

Expected: exit code 0, no new warnings versus the pre-change branch.

- [ ] **Step 3: Full Jest suite, matching CI exactly**

This is the same invocation as `.github/workflows/ci-feature-branch.yml:45`.

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti/frontend
CI=true REACT_APP_USE_MOCK_AUTH=true npm test -- --coverage --watchAll=false
```

Expected: `Test Suites: … passed`, `Tests: … passed`, zero failures, exit code 0.

There is no `coverageThreshold` in `frontend/package.json`'s `jest` block (only `transformIgnorePatterns`), so deleting an uncovered file cannot fail CI on a coverage gate. Do **not** add one defensively.

- [ ] **Step 4: Confirm no E2E gate is run**

Do **not** run `./scripts/run-playwright-tests.sh` as a merge gate. `scripts/run-playwright-tests.sh:27` hardcodes `STAGING_URL="https://heblo.stg.anela.cz"` and exports it as `PLAYWRIGHT_BASE_URL` (line 77), and `docs/architecture/testing-strategy.md:248-251` requires the suite to always target deployed staging — so a pre-merge run exercises the deployed build, not this branch, and can produce no evidence about this change. The nightly staging run for the `transport` project (`frontend/test/e2e/transport/box-workflow.spec.ts`, `box-management.spec.ts`, `boxes-basic.spec.ts`) is the post-deploy regression backstop, and being green on the first run after deployment is the acceptance criterion. Record this reasoning in the PR description.

- [ ] **Step 5: Confirm the backend is genuinely untouched**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
git diff --name-only origin/main...HEAD -- backend/ frontend/src/api/generated/ docs/superpowers/
```

Expected: empty output. No `dotnet build` or `dotnet format` run is required, because no `.cs` file changed.

- [ ] **Step 6: No commit for this task**

```bash
cd /home/user/worktrees/feature-3889-Arch-Review-Transportboxes-Usetransportboxtransiti
git status --short frontend docs
```

Expected: empty output (the `frontend/build/` directory produced by Step 1 is gitignored; if it shows up, do not add it). If any gate required a source fix, that fix must have been amended into the single removal commit, not committed separately.

---

