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
