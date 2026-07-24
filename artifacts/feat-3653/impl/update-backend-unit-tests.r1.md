# Implementation: update-backend-unit-tests

## What was implemented
Updated `GetBankStatementListHandlerTests.cs` and `GetBankStatementListRequestValidatorTests` to construct `GetBankStatementListRequest` with typed `DateTime` values instead of string literals, deleted the three tests that exercised the now-impossible "unparseable date string" scenario (`Handle_IgnoresUnparseableDateStrings`, `Validate_RejectsUnparseableDateFrom`, `Validate_RejectsUnparseableDateTo`), and added a new integration test `GetBankStatements_WithInvalidDateFromQueryParam_ReturnsBadRequest` to `BankStatementImportIntegrationTests.cs` covering the new ASP.NET Core model-binding rejection path for malformed date query params (per the task context's exact specification).

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs` — retyped date literals to `DateTime`, deleted 3 now-unreachable tests.
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs` — added `GetBankStatements_WithInvalidDateFromQueryParam_ReturnsBadRequest`.

## Tests
- `GetBankStatementListHandlerTests` (2 tests) and `GetBankStatementListRequestValidatorTests` (5 tests) — 7 tests total, all passing.
- `BankStatementImportIntegrationTests` — new test added; full class passes.

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetBankStatementList|FullyQualifiedName~BankStatementImportIntegrationTests"
```

## Notes
- `dotnet build` (whole solution): succeeded, 0 errors (1 pre-existing, unrelated `MSB3073 AccessMatrixGen` warning — the access-matrix generator tool intermittently fails to parse its input JSON in this environment; this is a post-build codegen step unrelated to the Bank change and does not fail the build).
- `dotnet format --verify-no-changes`: clean, exit code 0.
- `dotnet test` (whole solution): 76 pre-existing failures, all `System.ArgumentException: Docker is either not running or misconfigured` from Testcontainers-based integration tests (Leaflet, Article persistence, `BankStatementImportRepositoryIntegrationTests`) — this sandbox has no Docker daemon running (`docker info`/`docker ps` confirm no daemon socket). These failures are pre-existing and unrelated to this change; they are not part of the Bank date-retype scope and would pass in an environment with Docker available (e.g. CI).
- Targeted verification of exactly the classes this task touches (`GetBankStatementListHandlerTests`, `GetBankStatementListRequestValidatorTests`, `BankStatementImportIntegrationTests`) passes cleanly: 17/17, 0 failures.

## Status
DONE
