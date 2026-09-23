# Implementation: register-import-bank-statement-validator-in-di

## What was implemented

Registered the `ImportBankStatementRequestValidator` (added in the prior task) into the
MediatR validation pipeline in `BankModule.AddBankModule`, following the exact pattern
already used for `GetBankStatementListRequestValidator` two lines above it. Before this
change, the validator class existed but was never resolved by DI, so it had no effect at
runtime — a request with an unknown `AccountName` would still reach
`ImportBankStatementHandler.Handle` and hit its manual `ArgumentException` throw.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` —
  - Added `using Anela.Heblo.Application.Features.Bank.Contracts;` and
    `using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;`
    (both namespaces were previously unreferenced in this file; `Validators` was already
    imported).
  - Added `services.AddScoped<IValidator<ImportBankStatementRequest>, ImportBankStatementRequestValidator>();`
    and `services.AddScoped<IPipelineBehavior<ImportBankStatementRequest, BankStatementImportResultDto>, ValidationBehavior<ImportBankStatementRequest, BankStatementImportResultDto>>();`
    directly below the existing `GetBankStatementListRequest` registrations, matching the
    task-context's exact specified diff.

## Tests

No new tests were written for this task — it is a pure DI wiring change with no new logic
(the task-context specifies only a build-and-commit verification step, not new tests). The
validator's own behavior is already fully covered by
`ImportBankStatementRequestValidatorTests` from the prior task.

I did, however, run the existing Bank test suite to confirm the new registration resolves
cleanly and introduces no regressions — see "How to verify" below.

## How to verify

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Bank"
```

Results:
- `dotnet build`: Build succeeded, 0 errors (123 pre-existing warnings across the solution,
  none in the touched file, none related to this change).
- `dotnet format --verify-no-changes`: no formatting issues.
- `dotnet test --filter "FullyQualifiedName~Bank"`: 339 passed, 12 failed, 351 total.
  All 12 failures are in `BankStatementImportRepositoryIntegrationTests` and fail with
  `System.ArgumentException: Docker is either not running or misconfigured` from
  `Testcontainers.PostgreSql` — a pre-existing sandbox limitation (no Docker daemon
  available in this environment), unrelated to this change; these tests don't exercise
  `BankModule` DI registration at all.

  Critically, `BankStatementImportIntegrationTests` (which spins up a real
  `WebApplicationFactory<Program>` and therefore fully builds the DI container, including
  `BankModule.AddBankModule`) passed all its tests, including
  `ImportBankStatement_WithInvalidAccount_ReturnsBadRequest`. Had the new DI registration
  been wrong (e.g. wrong generic parameters, unresolvable dependency), ASP.NET Core's
  service-provider validation would have thrown at host startup for this test class — it
  did not, confirming the two new registrations resolve cleanly end-to-end.

## Notes

Scope was kept exactly to the task-context's Step 1–2 diff: no other lines in
`BankModule.cs` were touched, and the handler's redundant `ArgumentException` guard
(`remove-redundant-argumentexception-from-import-handler`, the next task) was
deliberately left untouched here.

## PR Summary

Wired `ImportBankStatementRequestValidator` (added in a prior task) into the MediatR
pipeline in `BankModule.AddBankModule`, registering it as `IValidator<ImportBankStatementRequest>`
plus its matching `ValidationBehavior<ImportBankStatementRequest, BankStatementImportResultDto>`
pipeline behavior — the same pattern already used for `GetBankStatementListRequest`. Until
this change the validator class existed but was inert (never resolved by DI), so an
unknown `AccountName` still reached the handler's manual `ArgumentException` throw instead
of being rejected by validation.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` — added the two
  `using` statements and the two `services.AddScoped<...>` registrations for
  `ImportBankStatementRequestValidator` and its `ValidationBehavior`.

## Status
DONE
