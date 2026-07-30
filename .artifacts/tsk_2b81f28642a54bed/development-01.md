# Development: consolidate `ImportBankStatementResponse` into `BankStatementImportResultDto`

Implemented exactly per `plan-01.md` / `design-01.md` (confirmed unchanged by `architecture-01.md`).

## Changes

**`backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementRequest.cs`**
- `IRequest<ImportBankStatementResponse>` → `IRequest<BankStatementImportResultDto>`.

**`backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`**
- `IRequestHandler<ImportBankStatementRequest, ImportBankStatementResponse>` → `IRequestHandler<ImportBankStatementRequest, BankStatementImportResultDto>`.
- `Handle` return type `Task<ImportBankStatementResponse>` → `Task<BankStatementImportResultDto>`.
- Final `return new ImportBankStatementResponse { ... }` → `return new BankStatementImportResultDto { ... }` (same four field assignments, unchanged).

**`backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementResponse.cs`**
- Deleted (no longer referenced anywhere).

**`backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`**
- `ImportStatements` action collapsed from mediator-send + 8-line manual field mapping into:
  ```csharp
  var importRequest = new ImportBankStatementRequest(request.AccountName, request.DateFrom, request.DateTo);
  var response = await _mediator.Send(importRequest);
  return Ok(response);
  ```
  Action signature (`ActionResult<BankStatementImportResultDto>`, route, `[FromBody]` param) unchanged — no OpenAPI/HTTP contract diff.

**`backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportJobBase.cs`**
- No change needed, as anticipated by the design — it already only reads `response.HasErrors`, `SuccessCount`, `ErrorCount`, `SkippedCount`, all present on `BankStatementImportResultDto`.

## Test updates (mechanical, no behavioral change)

**`backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/BankImportJobBaseTests.cs`**
- Added `using Anela.Heblo.Application.Features.Bank.Contracts;`.
- All `new ImportBankStatementResponse(...)` → `new BankStatementImportResultDto(...)`.
- All `Callback<IRequest<ImportBankStatementResponse>, CancellationToken>` → `Callback<IRequest<BankStatementImportResultDto>, CancellationToken>`.

**`backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs`**
- Added `using Anela.Heblo.Application.Features.Bank.Contracts;`.
- All `JsonSerializer.Deserialize<ImportBankStatementResponse>(...)` → `JsonSerializer.Deserialize<BankStatementImportResultDto>(...)`.

**`backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`**
- No change needed — it only reads fields common to both types.

No frontend changes; no OpenAPI/TypeScript client regeneration needed (response DTO's shape and name on the wire are unchanged).

## Verification performed

- `dotnet build Anela.Heblo.sln` (from repo root, via `~/.dotnet/dotnet` — not on default PATH on this machine) — **0 errors**, 250 pre-existing warnings (none introduced by this change).
- `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Bank" --no-build` — **337 passed, 0 failed** (covers `ImportBankStatementHandlerTests`, `BankImportJobBaseTests`, `BankStatementImportIntegrationTests`, and all other Bank-module tests).
- `dotnet format ../Anela.Heblo.sln --verify-no-changes --include <changed files>` — clean, no formatting changes required.

## How to verify

```bash
export PATH="$HOME/.dotnet:$PATH"   # if dotnet isn't already on PATH
cd backend
dotnet build ../Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Bank"
```
