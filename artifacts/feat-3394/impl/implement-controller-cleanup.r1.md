# Implementation: implement-controller-cleanup

## What was implemented
Removed inline try/catch blocks from `ImportStatements` and `GetBankStatements` in `BankStatementsController`. Exception handling now delegates to the global `ArgumentExceptionHandler` and `ValidationExceptionHandler`.

## Files created/modified
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — Removed `try/catch (ArgumentException)/catch (Exception)` from `ImportStatements`; removed `try/catch (FluentValidation.ValidationException)/catch (Exception)` from `GetBankStatements`. Retained `_logger.LogInformation(...)` calls in both methods.

## How to verify
- `BankStatementsController` contains no `catch` blocks
- `_logger.LogInformation(...)` still present in `ImportStatements` and `GetBankStatements`
- `dotnet build` succeeds (verified: 0 errors)
- `dotnet format --verify-no-changes` passes (verified: exit code 0)

## Status
DONE
