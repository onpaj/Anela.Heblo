# Implementation: remove-handler-date-parsing

## What was implemented
Removed the now-redundant manual date parsing from `GetBankStatementListHandler.Handle`. Since `GetBankStatementListRequest`'s `StatementDate`, `ImportDate`, `DateFrom`, and `DateTo` fields were already retyped to `DateTime?` in task 1, the handler no longer needs to call `ParseDateOrNull` on them — the four local variables (`statementDate`, `importDate`, `dateFrom`, `dateTo`) were deleted, the request's date fields are now passed straight into the `BankStatementListFilter` constructor, and the `ParseDateOrNull` private helper method was removed entirely. The `NormalizeNullableString` helper and its two call sites (`trimmedTransferId`, `trimmedAccount`) were left untouched.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs` — removed the 4 `ParseDateOrNull(...)` local variable assignments, removed the `ParseDateOrNull` static helper method, and updated the `BankStatementListFilter` constructor call to use `request.StatementDate`/`request.ImportDate`/`request.DateFrom`/`request.DateTo` directly.

## Tests
N/A - covered by later task. No test files were touched in this task; the change is a pure refactor removing now-dead parsing logic that the retyped `DateTime?` fields make redundant.

## How to verify
1. `git show a9ac285` (or `git diff HEAD~1` on this branch) to confirm the diff matches exactly the before/after code specified in the task context.
2. Per the task context, `dotnet build` was intentionally **not** run — `GetBankStatementListRequestValidator.cs` still calls `BeParseableDate(x.DateFrom)` expecting a `string?` parameter, which will fail to compile (`CS1503`) until task 3 (`simplify-validator-date-rules`) retypes/removes that validation rule. This is expected per the task's "expected build state" step.
3. Once task 3 lands, run `dotnet build` on the solution to confirm the whole chain compiles cleanly.

## Notes
No deviations. The resulting file content matches the "Replace with" block in the task context verbatim.

## Status
DONE
