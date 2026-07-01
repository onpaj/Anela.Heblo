# Implementation: backend-surface-import-counts

## What was implemented
Extended the bank statement import result contract to surface per-run success/error counts, populated from the already-computed `successCount`/`errorCount` locals in `ImportBankStatementHandler` (counting rule left byte-for-byte identical). Added two new handler tests (mixed success/error run, empty run) following TDD — written first, confirmed failing to compile, then made pass with the production edits. Regenerated the NSwag TypeScript client so the frontend type picks up the new fields.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportResultDto.cs` — added `SuccessCount`, `ErrorCount` (settable), `TotalCount` and `HasErrors` (computed from `Statements`/`ErrorCount`)
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementResponse.cs` — added `SuccessCount`, `ErrorCount` properties
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs:120` — return now assigns `SuccessCount`/`ErrorCount` from the pre-existing `successCount`/`errorCount` locals (lines 113-118 untouched)
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs:52-56` — copies `SuccessCount`/`ErrorCount` from the handler response onto the DTO; `TotalCount`/`HasErrors` remain derived, not assigned
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` — added `using Anela.Heblo.Application.Features.Bank.Contracts;` plus two tests
- `frontend/src/api/generated/api-client.ts` — regenerated via NSwag (`dotnet msbuild -t:GenerateFrontendClientManual` in `backend/src/Anela.Heblo.API`, after `dotnet tool restore`); adds `successCount`, `errorCount`, `totalCount`, `hasErrors` to the `BankStatementImportResultDto`/`IBankStatementImportResultDto` TypeScript types. NSwag correctly emitted the computed `totalCount`/`hasErrors` properties, so the Decision 2 fallback (plain settable auto-properties) was not needed.

## Tests
- `Handle_WithMixedResults_PopulatesSuccessAndErrorCounts` — 2 successful + 1 failed statement import; asserts `Statements.Count == 3`, `SuccessCount == 2`, `ErrorCount == 1`
- `Handle_WithNoStatements_ReturnsZeroCounts` — no statements returned from the bank client; asserts empty `Statements` and both counts are `0`
- Existing `ImportBankStatementHandlerTests` (3 tests) continue to pass unchanged

## How to verify
```
cd backend
dotnet build                                    # succeeds (0 errors); pre-existing GenerateAccessMatrix warning (see Notes) is unrelated and tolerated via ContinueOnError
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -c Release --filter "FullyQualifiedName~ImportBankStatementHandlerTests"
# Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5
cd .. && dotnet format Anela.Heblo.sln --verify-no-changes   # exits 0, no output
```
To confirm the TypeScript client:
```
cd backend/src/Anela.Heblo.API
dotnet tool restore   # once, restores local nswag tool
dotnet build --no-restore --verbosity quiet
dotnet msbuild -t:GenerateFrontendClientManual
grep -n "successCount\|errorCount\|totalCount\|hasErrors" ../../../frontend/src/api/generated/api-client.ts
```

## Notes
- This sandbox has no Docker daemon, so Testcontainers-backed integration tests (`BankStatementImportRepositoryIntegrationTests`, `PhotobankRepositoryGetTagsSqlShapeTests`, `KnowledgeBaseRepositoryIntegrationTests`) fail with "Docker is either not running or misconfigured" — pre-existing environment limitation, unrelated to this change and not touching any of the modified files. Verified separately that all `ImportBankStatementHandlerTests` (5) and `ComgateBankClientTests` (16) unit tests pass (21/21) in Release config.
- Discovered a pre-existing, unrelated build issue: the Debug-only `GenerateAccessMatrix` MSBuild target (`Anela.Heblo.API.csproj:108`) invokes `Anela.Heblo.AccessMatrixGen` with an argument order that doesn't match `Program.cs`'s expected positional args, so the tool crashes with an unhandled `JsonException` on every Debug build (`ContinueOnError="true"` lets the build proceed, but under `dotnet test` in this sandbox the MSBuild worker nodes then stalled in `futex_do_wait` for several minutes after the crash, unrelated to any code touched here). Worked around it for verification by running `dotnet build`/`dotnet test` from the repo root (which completed normally, ~10-40s) and by running Bank-specific test filters with `-c Release` (bypasses the Debug-only target entirely, completes in milliseconds). Did not fix this pre-existing bug since it's out of scope for this task — flagging it here per "notice unrelated issues, don't fix them."
- No deviations from the task spec: DTO/response/handler/controller edits are exactly as specified; `TotalCount`/`HasErrors` stayed computed properties (no fallback needed).

## Status
DONE
