# Implementation: retype-request-and-controller-date-fields

## What was implemented
Retyped the four date filter fields (`StatementDate`, `ImportDate`, `DateFrom`, `DateTo`) on `GetBankStatementListRequest` from `string?` to `DateTime?`, and retyped the matching `[FromQuery]` parameters on `BankStatementsController.GetBankStatements` from `string?` to `DateTime?`. This moves date parsing/rejection to ASP.NET Core model binding, matching the pattern used elsewhere in the codebase. No other properties, parameter names, defaults, or the request-construction body were changed. `BankStatementListFilter` (domain filter) was left untouched, as instructed — it is already `DateTime?`-typed.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs` — retyped `StatementDate`, `ImportDate`, `DateFrom`, `DateTo` from `string?` to `DateTime?`.
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — retyped the `statementDate`, `importDate`, `dateFrom`, `dateTo` parameters of `GetBankStatements` from `[FromQuery] string?` to `[FromQuery] DateTime?`.

## Tests
N/A - covered by later task (`update-backend-unit-tests`).

## How to verify
- Diff the two files against the "after" snippets in `artifacts/feat-3653/task-context/retype-request-and-controller-date-fields.md` (Step 1 / Step 2) — they match exactly.
- `git show a180711` shows exactly the two intended files changed, 8 insertions / 8 deletions.
- Per the task context, `dotnet build` is expected to fail at this point (`GetBankStatementListHandler.cs` still calls `ParseDateOrNull(request.StatementDate)`, which now mismatches on `DateTime?` vs `string?` — `CS1503`). This was intentionally not attempted, as instructed; the next task (`remove-handler-date-parsing`) resolves it.

## Notes
No deviations from the task context. Ground-truth file contents matched the "current content" snippets in the task file exactly before editing.

## Status
DONE
