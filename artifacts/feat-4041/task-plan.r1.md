# Permission-gate invoice-import-statistics and bank-statements routes Implementation Plan

**Goal:** Close a frontend authorization gap so that `/automation/invoice-import-statistics` and `/finance/bank-statements` redirect unauthorized users to `/` instead of loading a page whose only data source returns 403.

**Architecture:** `access-matrix.json` (repo root) is the single source of truth for menu-path → feature-permission mappings, compiled by `Anela.Heblo.AccessMatrixGen` into five generated artifacts, one of which (`frontend/src/auth/accessMatrix.generated.ts`) backs the `ACCESS_ROUTES` lookup used by `RequireMenuPath`/`guard(...)` in `App.tsx`. Both new routes are missing an `access-matrix.json` entry AND a `guard(...)` wrapper in `App.tsx`; the fix adds both, wires them to the pre-existing `Finance_MarginAnalysis` (`finance.margin_analysis.read`) feature already required by the backing `AnalyticsController`, and regenerates the five derived artifacts.

**Tech Stack:** .NET 8 (C# source generator tool `Anela.Heblo.AccessMatrixGen`), React + TypeScript (React Router v6, Jest via `react-scripts test`).

---

### task: add-access-matrix-entries

**Files:**
- Modify: `access-matrix.json` (repo root)

Add two new `menuPaths` entries requiring the pre-existing `Finance_MarginAnalysis` feature at `Read` level, matching the permission already enforced by `AnalyticsController`'s class-level `[FeatureAuthorize(Feature.Finance_MarginAnalysis)]` (`backend/src/Anela.Heblo.API/Controllers/AnalyticsController.cs:14`).

- [ ] **Step 1: Confirm the current `menuPaths` entry to anchor on**

Run:
```bash
grep -n '"/analytics/product-margin-summary"' access-matrix.json
```
Expected output (single line, exact text):
```
43:    { "path": "/analytics/product-margin-summary", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] },
```
(Line number may differ slightly depending on git history, but the content must match exactly.)

- [ ] **Step 2: Add the two new `menuPaths` entries**

In `access-matrix.json`, find this exact line inside the `"menuPaths"` array:
```json
    { "path": "/analytics/product-margin-summary", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] },
```
Replace it with these three lines (the original line, unchanged, followed by the two new entries):
```json
    { "path": "/analytics/product-margin-summary", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] },
    { "path": "/automation/invoice-import-statistics", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] },
    { "path": "/finance/bank-statements", "requires": [{ "feature": "Finance_MarginAnalysis", "level": "Read" }] },
```
All three entries require the same feature (`Finance_MarginAnalysis`) at the same level (`Read`), so grouping them together keeps the diff minimal and easy to review. This does not reorder or modify any existing entry — it only inserts two new lines immediately after an existing one.

- [ ] **Step 3: Validate the JSON is well-formed**

Run:
```bash
python3 -c "import json; d = json.load(open('access-matrix.json')); print(len(d['menuPaths']))"
```
Expected output: the previous count of `menuPaths` entries + 2 (no exception raised — a `json.decoder.JSONDecodeError` means a syntax mistake, e.g. a missing/extra comma, was introduced).

- [ ] **Step 4: Confirm no other line in the file changed**

Run:
```bash
git diff access-matrix.json
```
Expected output: a diff showing exactly one `+` line becoming three (i.e., 2 new `+` lines added, 0 lines removed, 0 lines changed) — no other hunk anywhere in the file.

- [ ] **Step 5: Commit**
```bash
git add access-matrix.json
git commit -m "feat(auth): add menu-path permission entries for invoice-import-statistics and bank-statements routes

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Py3c1pTCK95Y4Xion83smx"
```

---

### task: regenerate-access-matrix-artifacts

**Files:**
- Modify (regenerated, do not hand-edit): `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs`
- Modify (regenerated, do not hand-edit): `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs`
- Modify (regenerated, do not hand-edit): `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs`
- Modify (regenerated, do not hand-edit): `frontend/src/auth/accessMatrix.generated.ts`
- Modify (regenerated, do not hand-edit): `access-matrix-entra.generated.json`

This task depends on `add-access-matrix-entries` being committed first. Run the `Anela.Heblo.AccessMatrixGen` tool directly (equivalent to, but faster and more predictable than, a Debug build of `Anela.Heblo.API`, which invokes the same tool via its `GenerateAccessMatrix` MSBuild target).

- [ ] **Step 1: Run the generator**

From the repo root, run:
```bash
dotnet run --project backend/tools/Anela.Heblo.AccessMatrixGen -- \
  access-matrix.json \
  backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs \
  frontend/src/auth/accessMatrix.generated.ts \
  access-matrix-entra.generated.json
```
Expected output: the command exits with code `0` and no exception is printed (a `JsonException`/non-zero exit means `access-matrix.json` has a syntax error from the previous task — go back and fix it before continuing).

**If the command appears to hang** (no output, no CPU activity from the `dotnet` process for more than ~30s): this is a known sandbox issue with stale MSBuild/VBCSCompiler node-reuse servers (`memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md`). Kill the hung process, then retry with:
```bash
dotnet build-server shutdown
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 \
  dotnet run --project backend/tools/Anela.Heblo.AccessMatrixGen -- \
  access-matrix.json \
  backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs \
  frontend/src/auth/accessMatrix.generated.ts \
  access-matrix-entra.generated.json \
  -p:UseSharedCompilation=false
```

- [ ] **Step 2: Verify `AccessMatrix.generated.cs` contains the two new `MenuPath` entries**

Run:
```bash
grep -n "invoice-import-statistics\|finance/bank-statements" backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs
```
Expected output (two lines, exact text — order may follow the source `menuPaths` order from the previous task):
```
new MenuPath("/automation/invoice-import-statistics", new FeaturePermission[] { new FeaturePermission(Feature.Finance_MarginAnalysis, AccessLevel.Read) }),
new MenuPath("/finance/bank-statements", new FeaturePermission[] { new FeaturePermission(Feature.Finance_MarginAnalysis, AccessLevel.Read) }),
```

- [ ] **Step 3: Verify `accessMatrix.generated.ts` contains the two new `ACCESS_ROUTES` keys**

Run:
```bash
grep -n "invoice-import-statistics\|finance/bank-statements" frontend/src/auth/accessMatrix.generated.ts
```
Expected output (two lines, exact text):
```
"/automation/invoice-import-statistics": { permissions: ["finance.margin_analysis.read"] },
"/finance/bank-statements": { permissions: ["finance.margin_analysis.read"] },
```

- [ ] **Step 4: Verify `Feature.generated.cs` and `AccessRoles.generated.cs` are unchanged in content**

Run:
```bash
git diff --stat backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs
```
Expected output: empty (no output at all) — no new `Feature` enum value or role constant is introduced by this change, since both new `menuPaths` entries reference the pre-existing `Finance_MarginAnalysis` feature.

- [ ] **Step 5: Verify `access-matrix-entra.generated.json` is unchanged in content**

Run:
```bash
git diff --stat access-matrix-entra.generated.json
```
Expected output: empty (no output at all) — this file's content is derived only from `features`/`seedGroups`, not `menuPaths`, so it is unaffected by adding menu-path entries for an existing feature.

- [ ] **Step 6: Review the full diff of the two files that did change**

Run:
```bash
git diff backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs frontend/src/auth/accessMatrix.generated.ts
```
Expected output: only additive `+` lines (the two new `MenuPath(...)` lines and the two new `ACCESS_ROUTES` keys) — no existing line removed, reordered, or modified.

- [ ] **Step 7: Commit**
```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs \
        backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs \
        backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs \
        frontend/src/auth/accessMatrix.generated.ts \
        access-matrix-entra.generated.json
git commit -m "chore(auth): regenerate access-matrix artifacts for the two new menu-path entries

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Py3c1pTCK95Y4Xion83smx"
```
If any of Step 4/5's `git diff --stat` commands are non-empty (unexpected changes appeared), do not commit — investigate first; this would indicate `access-matrix.json` was edited incorrectly in the previous task (e.g. a `features` or `seedGroups` entry was accidentally touched).

---

### task: guard-routes-in-app-tsx

**Files:**
- Modify: `frontend/src/App.tsx:415` (the `/finance/bank-statements` route)
- Modify: `frontend/src/App.tsx:445` (the `/automation/invoice-import-statistics` route)

This task depends on `regenerate-access-matrix-artifacts` being committed first (the `ACCESS_ROUTES` entries must exist before wrapping the routes, otherwise `RequireMenuPath` would redirect every user, including authorized ones, and the consistency test in the next task would still fail on the "every guard() has an ACCESS_ROUTES entry" check being satisfied but with a temporarily broken runtime).

Both routes already import their components (no import changes needed):
```
18:import BankStatementImportPage from "./pages/customer/BankStatementImportPage";
32:import InvoiceImportStatistics from "./components/pages/automation/InvoiceImportStatistics";
```
The `guard(path, element)` helper already exists at `App.tsx:292`:
```tsx
const guard = (path: string, element: React.ReactNode) => (
  <RequireMenuPath path={path}>{element}</RequireMenuPath>
);
```

- [ ] **Step 1: Confirm the two current bare routes**

Run:
```bash
grep -n 'finance/bank-statements"\|invoice-import-statistics"' frontend/src/App.tsx
```
Expected output (exact text):
```
415:                        <Route path="/finance/bank-statements" element={<BankStatementImportPage />} />
445:                        <Route path="/automation/invoice-import-statistics" element={<InvoiceImportStatistics />} />
```

- [ ] **Step 2: Wrap the `/finance/bank-statements` route in `guard(...)`**

In `frontend/src/App.tsx`, find this exact line:
```tsx
                        <Route path="/finance/bank-statements" element={<BankStatementImportPage />} />
```
Replace it with:
```tsx
                        <Route path="/finance/bank-statements" element={guard("/finance/bank-statements", <BankStatementImportPage />)} />
```

- [ ] **Step 3: Wrap the `/automation/invoice-import-statistics` route in `guard(...)`**

In `frontend/src/App.tsx`, find this exact line:
```tsx
                        <Route path="/automation/invoice-import-statistics" element={<InvoiceImportStatistics />} />
```
Replace it with:
```tsx
                        <Route path="/automation/invoice-import-statistics" element={guard("/automation/invoice-import-statistics", <InvoiceImportStatistics />)} />
```

- [ ] **Step 4: Verify the diff touches only these two lines**

Run:
```bash
git diff frontend/src/App.tsx
```
Expected output: exactly two changed lines (one `-`/`+` pair each), matching Steps 2 and 3 above — no other route, import, or the `guard()` definition itself is touched.

- [ ] **Step 5: Commit**
```bash
git add frontend/src/App.tsx
git commit -m "fix(auth): wrap invoice-import-statistics and bank-statements routes in guard()

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Py3c1pTCK95Y4Xion83smx"
```

---

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
