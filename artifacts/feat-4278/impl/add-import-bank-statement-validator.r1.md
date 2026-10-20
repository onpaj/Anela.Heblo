# Implementation: add-import-bank-statement-validator

## What was implemented

Added a FluentValidation validator for `ImportBankStatementRequest` so that
an unknown `AccountName` (or an invalid `DateFrom`/`DateTo` range) is
rejected as a 400 by the MediatR validation pipeline instead of the handler
throwing a bare `ArgumentException` that surfaces as a 500.

The validator:
- Requires `AccountName` to be non-empty.
- Requires `AccountName` to match one of the configured `BankAccountSettings.Accounts` entries.
- Requires `DateFrom` to not be later than `DateTo`.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Bank/Validators/ImportBankStatementRequestValidator.cs` — new `AbstractValidator<ImportBankStatementRequest>`, following the same pattern as the existing `GetBankStatementListRequestValidator` in the same folder. Injects `IOptions<BankAccountSettings>` to validate `AccountName` against the configured account list.
- `backend/test/Anela.Heblo.Tests/Features/Bank/Validators/ImportBankStatementRequestValidatorTests.cs` — new xUnit test class covering: known account name (no error), unknown account name (error), empty account name (error), `DateFrom` before/equal to `DateTo` (no error), `DateFrom` after `DateTo` (error, with exact message assertion).

## Deviation from the task-context's suggested implementation

The task-context's Step 3 code sample chained the `AccountName` rules as:

```csharp
RuleFor(x => x.AccountName)
    .NotEmpty().WithMessage("AccountName is required")
    .Must(name => settings.Accounts.Any(a => a.Name == name))
    .WithMessage(...)
    .When(x => !string.IsNullOrEmpty(x.AccountName));
```

Running the given tests against this verbatim, `AccountName_Empty_ShouldHaveValidationError` **failed**: in FluentValidation, a trailing `.When(...)` on a rule chain applies to the *whole chain* by default (not just the last `Must`), so gating on `!string.IsNullOrEmpty(x.AccountName)` also suppressed the preceding `NotEmpty()` check — an empty `AccountName` produced no validation error at all.

Fixed by splitting into two separate `RuleFor(x => x.AccountName)` chains — one unconditional `NotEmpty()`, and a second one with the `Must(...)` + `.When(...)` guard (so the "known account" check only runs once we already know the name isn't empty, avoiding a `null`/empty entry in the error message). This is a pre-existing FluentValidation composition gotcha, not a scope change — the resulting behavior matches the task's acceptance criteria and all 6 given tests exactly as written.

## Tests

`backend/test/Anela.Heblo.Tests/Features/Bank/Validators/ImportBankStatementRequestValidatorTests.cs` — 6 tests, all passing:
- `AccountName_Known_ShouldNotHaveValidationError`
- `AccountName_Unknown_ShouldHaveValidationError`
- `AccountName_Empty_ShouldHaveValidationError`
- `DateFrom_BeforeDateTo_ShouldNotHaveValidationError`
- `DateFrom_EqualToDateTo_ShouldNotHaveValidationError`
- `DateFrom_AfterDateTo_ShouldHaveValidationError`

## How to verify

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportBankStatementRequestValidatorTests"
```

Result: `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`. Full solution build: 0 errors (pre-existing warnings only, unrelated to this change). `dotnet format Anela.Heblo.sln --include <the two new files> --verify-no-changes` reports no formatting issues.

## Notes

This task only adds the validator class and its tests. It is **not** yet
wired into DI (no `AbstractValidator<>` registration change) and the
handler's redundant `ArgumentException` guard in `ImportBankStatementHandler`
is still in place — those are separate tasks
(`register-import-bank-statement-validator-in-di` and
`remove-redundant-argumentexception-from-import-handler`) in this feature's
task plan, deliberately left untouched here per the surgical-changes rule.

## PR Summary

Added `ImportBankStatementRequestValidator`, a FluentValidation validator for
`ImportBankStatementRequest`, so an unknown `AccountName` or an invalid
`DateFrom`/`DateTo` range is rejected as a validation error (ultimately a 400)
instead of the handler throwing a raw `ArgumentException` (a 500). Includes a
full xUnit test suite covering all the request's validation rules.

While implementing per the task's own step-by-step spec, found that its
verbatim `RuleFor` chain had a FluentValidation composition bug: a trailing
`.When(...)` on a chained rule set applies to every rule in that chain by
default, so the sample code silently disabled the `NotEmpty()` check
whenever `AccountName` was empty. Split the empty-check and the
known-account-check into two separate `RuleFor` calls so both fire
independently, matching the task's own acceptance tests (specifically
`AccountName_Empty_ShouldHaveValidationError`).

### Changes
- `backend/src/Anela.Heblo.Application/Features/Bank/Validators/ImportBankStatementRequestValidator.cs` — new validator
- `backend/test/Anela.Heblo.Tests/Features/Bank/Validators/ImportBankStatementRequestValidatorTests.cs` — new tests

## Status
DONE
