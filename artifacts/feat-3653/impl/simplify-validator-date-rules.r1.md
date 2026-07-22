# Implementation: simplify-validator-date-rules

## What was implemented

Rewrote `GetBankStatementListRequestValidator` to work with the now-`DateTime?`-typed `DateFrom`/`DateTo` fields on `GetBankStatementListRequest` (retyped in tasks 1 and 2 of this plan). Removed the `BeParseableDate` helper and the two `.Must(BeParseableDate)` rules (unparseable date strings can no longer reach the validator — ASP.NET Core model binding now rejects them before this point). Removed the `DateFromIsNotLaterThanDateTo` private method and replaced the date-range check with a single inline `RuleFor(x => x.DateFrom).Must(...)` lambda that compares the two nullable `DateTime` values directly, short-circuiting to valid when either side is null. `TransferId`/`Account` rules were left untouched. The final file content matches the task context's specified replacement exactly.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs` — replaced string-parsing date validation rules with direct `DateTime?` comparison; removed `BeParseableDate` and `DateFromIsNotLaterThanDateTo` helper methods.

## Tests
N/A - covered by later task (`update-backend-unit-tests`). The `Anela.Heblo.Tests` project is intentionally out of scope for this task's build verification (per task context Step 2 note) and remains broken until that later task updates the tests to use `DateTime?` instead of strings.

## How to verify
1. `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` from the repo root — should succeed with 0 errors.
2. Inspect the validator file to confirm it matches the "Replace it entirely with" block in the task context (lines 76-110 of the task-context file).
3. Confirm `git log -1 --stat` on this branch shows only the validator file changed, with the exact commit message from the task context.

## Notes

`dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` was run twice:
- First run: full output showed 155 warnings (all pre-existing, unrelated to this change — nullable-reference warnings, obsolete-member warnings, etc.) and **0 Errors**, confirming the `Application` and `API` projects now compile end-to-end with the new `DateTime?` types.
- Second run (piped to a log file to check the process exit code precisely): exit code `0`, log contains `Build succeeded.` with no `error ` lines.

One point worth flagging: the first build run's tail included an MSB3073 **warning** (not error) that the post-build `AccessMatrixGen` code-generation tool "exited with code 134" while regenerating `accessMatrix.generated.ts` / `AccessRoles.generated.cs`. This is a pre-existing post-build step unrelated to the Bank/validator change (it runs for every build of the API project) and did not cause the build to fail — MSBuild still reported `Build succeeded` with 0 errors and exit code 0. No investigation or fix was needed within the scope of this task since it does not affect the validator/DateTime retype work, doesn't block the build, and isn't mentioned as in-scope by the task context. Flagging it here in case it's relevant to a later task or pipeline stage.

No deviations from the task context — the validator was replaced with exactly the code specified in Step 1.

## Status
DONE
