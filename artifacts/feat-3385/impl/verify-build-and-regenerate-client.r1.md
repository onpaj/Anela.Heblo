# Implementation: verify-build-and-regenerate-client

## What was implemented
Verification run — no code changes.

## Verification Results

### Step 1: dotnet build
PASS — `Build succeeded` with 0 errors, 253 warnings (all pre-existing nullable/async warnings in test files). Build time: ~1m52s.

Note: The task spec used `backend/Anela.Heblo.sln` but the solution file lives at the worktree root (`Anela.Heblo.sln`). Build was run with the correct path.

### Step 2: grep for old namespace (RefreshTask)
PASS — `grep -r "BackgroundJobs\.Contracts\.RefreshTask"` found zero matches in source files. (Exit code 1 = no matches.)

### Step 3: grep for BackgroundJobs.Contracts
PASS (with note) — The pattern `BackgroundJobs\.Contracts` matches legitimately in source files under `Features/BackgroundJobs/Contracts/` (the existing RecurringJobs module: `RecurringJobDto`, `UpdateJobCronRequestBody`, `UpdateJobStatusRequestBody`) and their consumers (`GetRecurringJobHandler`, `GetRecurringJobsListHandler`, controller, tests, mapping profile). These are all **existing** BackgroundJobs contracts, not the misplaced RefreshTask DTOs that this feature moved. The new RefreshTask DTOs now live under `Features/BackgroundRefresh/Contracts/`, which is the correct location. The grep also hits binary `.dll`/`.pdb` files in `bin/` and `obj/` directories, which are compiled artifacts.

No source-level references to the old wrong namespace exist.

### Step 4: npm run build
PASS — `Compiled successfully.` Production build completed without TypeScript errors. Bundle: 1.25 MB gzipped JS, 20.93 kB CSS.

Note: `node_modules` were not present and had to be installed with `npm install --legacy-peer-deps` first (peer dependency conflicts exist but are pre-existing).

### Step 5: npm run lint
FAIL (pre-existing) — ESLint exited with code 1 reporting 161 problems (146 errors, 15 warnings). **None of the errors are in files changed by this branch.** All errors are in test files across unrelated modules (OrgChart, FinancialOverview, Terminal, etc.) using `testing-library` rule violations (`no-container`, `no-node-access`, `no-wait-for-multiple-assertions`, etc.). The frontend files changed by feat-3385 (`BackgroundTasksCard.tsx`, `BackgroundTasksTile.tsx`, `BackgroundTasks.tsx`, `RecurringJobsPage.tsx`) have zero lint errors.

## Files created/modified
None.

## Status
DONE_WITH_CONCERNS

Concern: `npm run lint` fails with 146 pre-existing errors unrelated to this branch. The lint baseline was already broken before this PR. This branch introduces no new lint errors.
