### task: validate-and-run-tests

**Files:** none changed in this task (validation only).

This task depends on all three previous tasks being committed. It runs the pre-existing bidirectional consistency test plus the repo's standard validation commands, and fixes anything they reveal.

- [ ] **Step 1: Run the pre-existing consistency test**

From `frontend/`, run:
```bash
cd frontend
CI=true npx react-scripts test src/auth/__tests__/accessMatrixConsistency.test.ts --watchAll=false
```
Expected output: `Tests: 3 passed, 3 total` (the three `it(...)` blocks in `frontend/src/auth/__tests__/accessMatrixConsistency.test.ts`: "every guard() call in App.tsx has an ACCESS_ROUTES entry", "every non-virtual ACCESS_ROUTES key is guarded in App.tsx", "at least one guarded route exists").

If either of the first two tests fails:
- "App.tsx guards routes not present in access-matrix.json: ..." → the `regenerate-access-matrix-artifacts` task did not actually produce the expected `ACCESS_ROUTES` keys; re-run that task's Step 1-3.
- "access-matrix.json declares routes not guarded in App.tsx: ..." → the `guard-routes-in-app-tsx` task's edits did not take effect (e.g. a typo in the path string passed to `guard(...)` that doesn't exactly match the `access-matrix.json` path); re-check Steps 2-3 of that task for an exact string match.

- [ ] **Step 2: Run the full frontend build**

From `frontend/`, run:
```bash
npm run build
```
Expected output: build completes successfully (`Compiled successfully.` or equivalent, exit code `0`), no TypeScript errors. This also implicitly re-verifies `accessMatrix.generated.ts` is syntactically valid TypeScript.

- [ ] **Step 3: Run the full frontend lint**

From `frontend/`, run:
```bash
npm run lint
```
Expected output: exit code `0`, no new lint errors (the only files changed — `App.tsx`, `accessMatrix.generated.ts` — contain no new lint-triggering patterns; `accessMatrix.generated.ts` is a generated file matching the existing lint-clean pattern).

- [ ] **Step 4: Run the full frontend test suite**

From `frontend/`, run:
```bash
CI=true npm test -- --watchAll=false
```
Expected output: all suites pass, including `src/auth/__tests__/accessMatrixConsistency.test.ts` (from Step 1) and every other existing test — no test file references `/finance/bank-statements` or `/automation/invoice-import-statistics` in a way that assumed the old unguarded behavior (none is expected per the spec's investigation, but this step is the safety net).

- [ ] **Step 5: Build the backend to confirm the regenerated C# artifacts compile**

From the repo root, run:
```bash
dotnet build backend/src/Anela.Heblo.API
```
Expected output: `Build succeeded.`, exit code `0`. This compiles `AccessMatrix.generated.cs` (which now references two additional `MenuPath(...)` calls, both using the pre-existing `Feature.Finance_MarginAnalysis` enum member, so no new symbol is required).

If this hangs, apply the same workaround as `regenerate-access-matrix-artifacts` Step 1:
```bash
dotnet build-server shutdown
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 \
  dotnet build backend/src/Anela.Heblo.API -nodeReuse:false -p:UseSharedCompilation=false
```

- [ ] **Step 6: Run `dotnet format` to confirm no formatting drift**

From the repo root, run:
```bash
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --verify-no-changes
```
Expected output: exit code `0`, no files listed as needing formatting. (The only backend files touched are `*.generated.cs`, produced verbatim by the generator's existing string-templating logic, which already matches the project's formatting conventions — this step confirms that remains true.)

- [ ] **Step 7: Final full-diff review across all changed files**

Run (the repo's base branch is `main`):
```bash
git diff origin/main --stat
```
Expected output: exactly four files listed, each with a small additive line count, e.g.:
```
access-matrix.json                                                            | 2 ++
backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs | 2 ++
frontend/src/App.tsx                                                          | 2 +-
frontend/src/auth/accessMatrix.generated.ts                                   | 2 ++
```
Confirm both of the following explicitly:
```bash
git diff origin/main --stat -- access-matrix-entra.generated.json backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs
```
Expected output: empty (no output at all) — these three files must show zero diff from `origin/main`, per FR-2's and FR-4's acceptance criteria (no new `Feature` enum value or role is introduced). If either command shows anything unexpected (a file missing from the four-file list, or content appearing in the three files that should be untouched), stop and investigate before considering this task complete.

- [ ] **Step 8: No commit in this task**

This task is validation-only — Steps 1-7 confirm the three prior commits (`add-access-matrix-entries`, `regenerate-access-matrix-artifacts`, `guard-routes-in-app-tsx`) together satisfy every FR and NFR in `spec.r1.md`. If any step above required a code fix, go back to the relevant earlier task, amend that fix there (new commit, not `--amend`, per repo convention), and re-run this task's steps from Step 1.
