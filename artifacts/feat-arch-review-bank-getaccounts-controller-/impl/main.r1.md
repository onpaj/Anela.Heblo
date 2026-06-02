---

# Implementation: Refactor `BankStatementsController.GetAccounts()` to MediatR Use Case

## What was implemented

Moved the in-controller `BankAccountConfiguration → BankAccountDto` mapping logic into a new MediatR use case (`GetBankAccountsHandler`), dropped `IOptions<BankAccountSettings>` from the controller constructor, and preserved the existing HTTP wire contract exactly (JSON array of `BankAccountDto`).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsRequest.cs` — empty MediatR request class
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsResponse.cs` — `BaseResponse` subtype with `List<BankAccountDto> Accounts`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs` — synchronous handler reading `IOptions<BankAccountSettings>`, mapping accounts, logging count
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — refactored `GetAccounts` to `async`/`await _mediator.Send`, dropped `IOptions` and `_bankSettings`, removed two now-unused `using` directives, added new namespace using
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs` — 5 xUnit unit tests for the handler
- `backend/test/Anela.Heblo.Tests/Controllers/BankStatementsControllerTests.cs` — 3 xUnit controller tests guarding the wire shape

## Tests

- **`GetBankAccountsHandlerTests`** (5 tests): null accounts → empty, empty accounts → empty, mapping with `.ToString()` on enums, `ArgumentNullException` on null options (via `?.Value ??`), `ArgumentNullException` on null logger
- **`BankStatementsControllerTests`** (3 tests): dispatch verified via MediatR, bare `IEnumerable<BankAccountDto>` returned (not envelope), empty case

All 8 new tests pass; 34 pre-existing Docker-dependent failures are unrelated.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetBankAccountsHandlerTests|FullyQualifiedName~BankStatementsControllerTests"
dotnet build
```

## Notes

- `Anela.Heblo.Domain.Shared` using was also removed from the controller (it was only referenced by the removed `BankAccountSettings` context) — build confirmed no breakage.
- Handler uses `bankSettings?.Value ?? throw` (null-conditional) rather than the sibling's `bankSettings.Value ?? throw` to properly throw `ArgumentNullException` when passed `null` (improvement noted in arch-review amendment #2).
- 236 pre-existing build warnings exist in the solution; none introduced by this change.

## PR Summary

Refactored `BankStatementsController.GetAccounts()` to delegate to a new `GetBankAccountsHandler` MediatR use case, eliminating the controller's direct dependency on `IOptions<BankAccountSettings>` and aligning it with the other three actions in the same controller. The HTTP contract (route, verb, JSON array shape) is unchanged.

Three new use-case files land under `Application/Features/Bank/UseCases/GetBankAccounts/`. The controller becomes a thin shim: dispatches `GetBankAccountsRequest`, unwraps `response.Accounts`, returns `Ok`. Eight new tests cover handler logic (null/empty/mapping/null-guards) and pin the wire shape so `Ok(response)` vs `Ok(response.Accounts)` regressions surface immediately.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsRequest.cs` — new empty MediatR request
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsResponse.cs` — new `BaseResponse` subtype with `Accounts` list
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs` — new handler: reads settings, maps DTOs, logs count
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — refactored `GetAccounts`, dropped `IOptions` dependency
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs` — 5 handler unit tests
- `backend/test/Anela.Heblo.Tests/Controllers/BankStatementsControllerTests.cs` — 3 controller tests guarding wire shape

## Status
DONE