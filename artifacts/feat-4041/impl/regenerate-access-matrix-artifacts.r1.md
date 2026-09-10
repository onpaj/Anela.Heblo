# Implementation: regenerate-access-matrix-artifacts

## What was implemented
Ran the `Anela.Heblo.AccessMatrixGen` code generator against the already-updated `access-matrix.json` to regenerate the derived access-matrix artifacts, verified only the two expected new menu-path entries were added with no unrelated changes, and committed the regenerated files.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs` — added two new `MenuPath` entries: `/automation/invoice-import-statistics` and `/finance/bank-statements`, both mapped to `Feature.Finance_MarginAnalysis` with `AccessLevel.Read`.
- `frontend/src/auth/accessMatrix.generated.ts` — added two new `ACCESS_ROUTES` keys: `/automation/invoice-import-statistics` and `/finance/bank-statements`, both requiring permission `finance.margin_analysis.read`.
- `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs` — regenerated, no diff (unchanged).
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` — regenerated, no diff (unchanged).
- `access-matrix-entra.generated.json` — regenerated, no diff (unchanged).

## Tests
N/A — no test suite run; generator output was verified via `git diff` inspection per the task's step-by-step checklist (grep for the two new entries, diff-stat checks confirming the three unchanged files, and a full diff review confirming only additive `+` lines in the two changed files).

## How to verify
1. `dotnet run --project backend/tools/Anela.Heblo.AccessMatrixGen -- access-matrix.json backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs frontend/src/auth/accessMatrix.generated.ts access-matrix-entra.generated.json` — exits 0, no exception.
2. `git show 32dc0a5` — confirm only the two expected files changed, each with exactly two added lines.

## Notes
The generator ran successfully on the first attempt (exit code 0, no exceptions) — no MSBuild/VBCSCompiler node-reuse hang was encountered, so the fallback workaround (`MSBUILDDISABLENODEREUSE=1` etc.) was not needed. An unrelated pre-existing modification to `artifacts/feat-4041/state.json` was present in the working tree before this task started; it was left untouched and unstaged since it is outside this task's file list.

## PR Summary
Regenerated the access-matrix derived artifacts from the updated `access-matrix.json`, adding the `/automation/invoice-import-statistics` and `/finance/bank-statements` menu-path entries to both the backend `AccessMatrix.generated.cs` and the frontend `accessMatrix.generated.ts`, with no other changes.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs` — added two `MenuPath` entries for the new routes.
- `frontend/src/auth/accessMatrix.generated.ts` — added two `ACCESS_ROUTES` entries for the new routes.

## Status
DONE
