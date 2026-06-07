All 7 tasks complete. Let me write the output summary.

---

# Implementation: GetBankStatement by Id — Dedicated MediatR Handler

## What was implemented

Replaced the controller-level workaround in `BankStatementsController.GetBankStatement(int id)` with a purpose-built MediatR handler `GetBankStatementByIdHandler`. The handler calls `IBankStatementImportRepository.GetByIdAsync` directly and returns `BankStatementImportDto?` (reusing the existing list-item DTO — no new response type). The controller became a thin dispatcher that maps `null → 404 + message body` or `value → 200 OK`.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdRequest.cs` — new MediatR request class, `IRequest<BankStatementImportDto?>`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdHandler.cs` — new handler; calls repo, maps via AutoMapper/BankMappingProfile, returns null if not found
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — `GetBankStatement` body replaced with thin MediatR dispatch; added `CancellationToken`, removed `try/catch`, preserved 404 message body
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementByIdHandlerTests.cs` — 4 unit tests for handler (found, null, repo-called-once, projection-equality)
- `backend/test/Anela.Heblo.Tests/Controllers/BankStatementsControllerTests.cs` — 3 new controller unit tests (dispatch + 200, 404 with message body, no list request)
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs` — 2 new integration tests (200 with DTO fields, 404 with message field)

## Tests

- `GetBankStatementByIdHandlerTests.cs` — 4 unit tests covering handler logic (all pass)
- `BankStatementsControllerTests.cs` — 6 tests total (3 existing + 3 new, all pass)
- `BankStatementImportIntegrationTests.cs` — 9 tests total (7 existing + 2 new end-to-end, all pass)

## How to verify

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Bank"
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BankStatementsControllerTests"
```

## Notes

- **CancellationToken not forwarded to repo** (by design): `IBankStatementImportRepository.GetByIdAsync(int id)` has no CT parameter; FR-5 forbids interface changes. The handler's `Handle()` still accepts CT (MediatR contract) — it's simply not passed to the repo.
- **No new response DTO**: `BankStatementImportDto` is reused directly (arch review decision 1), keeping the OpenAPI/TypeScript surface unchanged.
- **Test file cleanup**: The implementer for Task 3 added a `using Microsoft.Extensions.Logging.Abstractions;` to the test file to fix a MapperConfiguration constructor issue — reviewed and confirmed as a valid infrastructure fix (no test logic was changed).
- **130 pre-existing frontend lint errors** remain but none were introduced by this feature.
- Repository files (`IBankStatementImportRepository.cs`, `BankStatementImportRepository.cs`) confirmed untouched via `git diff main`.

## PR Summary

Replaced the `GetBankStatement(int id)` list-handler workaround with a dedicated `GetBankStatementByIdHandler` that calls `IBankStatementImportRepository.GetByIdAsync` directly, restoring the "thin controllers, business logic in handlers" invariant required by the project's architecture rules.

The HTTP contract is unchanged: same route, same response shape (`BankStatementImportDto`), same 200/404 status codes, same 404 message body. No migrations, no new NuGet packages, no configuration changes.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdRequest.cs` — new MediatR request contract
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdHandler.cs` — new handler using IMapper + IBankStatementImportRepository
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — thin dispatcher replacing the list-handler workaround
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementByIdHandlerTests.cs` — 4 handler unit tests
- `backend/test/Anela.Heblo.Tests/Controllers/BankStatementsControllerTests.cs` — 3 new controller tests
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs` — 2 end-to-end HTTP tests

## Status

DONE