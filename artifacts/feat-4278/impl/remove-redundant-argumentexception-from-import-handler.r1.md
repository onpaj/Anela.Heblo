# Implementation: remove-redundant-argumentexception-from-import-handler

## What was implemented
Removed the now-redundant manual `ArgumentException` guard in `ImportBankStatementHandler.Handle` for an unknown bank account. That responsibility now belongs to `ImportBankStatementRequestValidator` (added in the `add-import-bank-statement-validator` task and wired into the pipeline in `register-import-bank-statement-validator-in-di`), which runs before the handler and throws `FluentValidation.ValidationException` on an unknown account name — mapped to an HTTP 400 `ProblemDetails` response by `ValidationExceptionHandler`.

The `SingleOrDefault` + null-check + logging + `throw new ArgumentException(...)` block was replaced with a direct `Single(a => a.Name == request.AccountName)`, since the validator already guarantees the account exists by the time the handler runs.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` — replaced the `SingleOrDefault`/null-check/`ArgumentException` block (lines 50-61) with a single `Single(a => a.Name == request.AccountName)` call. No other logic in `Handle`, `ProcessStatementAsync`, `InsertNewAsync`, or `UpsertExistingAsync` changed.
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` — removed `Handle_WithUnknownAccount_ThrowsArgumentException`, which asserted the now-removed handler-level exception. That behavior is already covered by `ImportBankStatementRequestValidatorTests.AccountName_Unknown_ShouldHaveValidationError` from the earlier task. All other tests in the file (`Constructor_WithNullFactory_ThrowsArgumentNullException`, `Handle_WithValidAccount_ResolvesClientViaFactory`, and the rest) were left untouched.

## Tests
- `ImportBankStatementHandlerTests` — 13 tests, all passing after the change (the obsolete test was removed, not replaced).
- Checked `BankStatementsControllerTests.cs` and `BankStatementImportIntegrationTests.cs` for any lingering assertion of the old 500/`ArgumentException` behavior (`grep -n "ArgumentException\|UNKNOWN"`) — no matches, so no further changes were needed there.

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportBankStatementHandlerTests"
# Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13

dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank"
# Failed: 8, Passed: 123, Skipped: 0, Total: 131 -- all 8 failures are in
# BankStatementImportRepositoryIntegrationTests and fail with
# "Docker is either not running or misconfigured" (Testcontainers/PostgresSharedContainerFixture).
# This is a pre-existing sandbox limitation (no Docker daemon available), unrelated to this
# change -- none of the failing tests touch ImportBankStatementHandler or account resolution.
```

## Notes
No deviations from the task spec. The account-not-found behavior visible to API consumers changes from an unhandled `ArgumentException` (mapped to a generic 500) to a `FluentValidation.ValidationException` mapped to a 400 `ProblemDetails` response by the existing `ValidationExceptionHandler` -- this was already the intended end state from the earlier tasks in this feature, and no test asserts the old 500/`ArgumentException` behavior at the controller/integration level.

## PR Summary
Removed the last vestige of manual "unknown account" validation from `ImportBankStatementHandler`. With `ImportBankStatementRequestValidator` now registered in the MediatR pipeline (prior task), the handler no longer needs to guard against an unknown account name itself -- `Single(...)` is safe because validation has already rejected the request otherwise. Deleted the handler-level test that asserted the old `ArgumentException` behavior, since it's superseded by the validator's own test coverage.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` — replaced manual null-check/`ArgumentException` with `Single(...)`
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` — removed the obsolete `Handle_WithUnknownAccount_ThrowsArgumentException` test

## Status
DONE
