# Implementation: add-cancellationtoken-to-bank-repository-and-call-sites

## What was implemented
Added `CancellationToken cancellationToken = default` to `IBankStatementImportRepository.GetByIdAsync`, `AddAsync`, and `UpdateAsync`, matching the other four methods on the interface. Updated the EF Core implementation (`BankStatementImportRepository`) to forward the token into `FindAsync`/`SaveChangesAsync` while preserving the existing `DbUpdateException` detach-and-rethrow behavior verbatim. Threaded the token through `GetBankStatementByIdHandler.Handle` and through `ImportBankStatementHandler`'s `InsertNewAsync`/`UpsertExistingAsync`/`ProcessStatementAsync` call chain. Updated the two affected Moq-based test files so their `Setup`/`Verify` expressions match the new two-argument overloads.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — added `CancellationToken cancellationToken = default` to `GetByIdAsync`, `AddAsync`, `UpdateAsync`.
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` — forwards the token to `FindAsync(new object[]{id}, cancellationToken)` and `SaveChangesAsync(cancellationToken)` in all three methods; exception handling unchanged.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdHandler.cs` — `GetByIdAsync(request.Id, cancellationToken)`.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` — `InsertNewAsync` gained a `CancellationToken` parameter forwarded to `AddAsync`; `UpsertExistingAsync` forwards its token to `UpdateAsync` and to the fallback `InsertNewAsync`; `ProcessStatementAsync`'s call site passes the token through on both branches.
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementByIdHandlerTests.cs` — `Setup`/`Verify` calls for `GetByIdAsync` updated to include `It.IsAny<CancellationToken>()`.
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` — `Setup`/`Verify` calls for `AddAsync`/`UpdateAsync` updated to include `It.IsAny<CancellationToken>()`.

`backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryIntegrationTests.cs` was left unchanged per the spec's Out-of-Scope section (calls the real repository positionally; resolves to `cancellationToken = default`).

## Tests
- `GetBankStatementByIdHandlerTests` + `ImportBankStatementHandlerTests`: 15/15 passing.
- Full `Anela.Heblo.Tests` suite: 5389 passed, 64 failed, 4 skipped (5457 total). All 64 failures are pre-existing `BankStatementImportRepositoryIntegrationTests`/other `*Integration*`/`*SqlShape*` tests across many modules (Bank, Smartsupp, GridLayouts, KnowledgeBase, Purchase, Photobank, MeetingTasks, Leaflet, Catalog, Article) that require a running Docker daemon for Testcontainers/Postgres — Docker is unavailable in this sandbox. None of the 64 failures are in the two files touched by this change (`GetBankStatementByIdHandlerTests`, `ImportBankStatementHandlerTests`), and none reference `IBankStatementImportRepository`'s new signatures — this is a pre-existing environment limitation, not a regression.
- `dotnet build Anela.Heblo.sln`: succeeds (0 errors).
- `dotnet format Anela.Heblo.sln --verify-no-changes`: no changes needed.

## How to verify
```bash
cd backend
dotnet build ../Anela.Heblo.sln
dotnet format ../Anela.Heblo.sln --verify-no-changes
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetBankStatementByIdHandlerTests|FullyQualifiedName~ImportBankStatementHandlerTests"
```

## Notes
The initial implementation pass introduced a Moq bug: `.Setup(r => r.AddAsync(...)).ReturnsAsync((BankStatementImport b) => b)` and the equivalent for `UpdateAsync` used a single-parameter callback, but Moq requires the `ReturnsAsync` callback to match the number of parameters of the setup's target overload (now 2: `BankStatementImport`, `CancellationToken`). This caused 3 test failures (`Handle_RetriesPreviouslyFailedStatement_ViaUpdateNotAdd`, `Handle_RecordsFailureWatermark_WhenStatementFails`, `Handle_CollapsesDuplicateStatementIdsInResponse_ProcessesOnce`) with `System.ArgumentException: Invalid callback`. Fixed by changing the callbacks to `(BankStatementImport b, CancellationToken _) => b` at the three affected call sites (lines ~164, 221, 315 of `ImportBankStatementHandlerTests.cs`). Re-ran the targeted and full suites afterward to confirm the fix and rule out further regressions.

## PR Summary
`IBankStatementImportRepository.GetByIdAsync`, `AddAsync`, and `UpdateAsync` were the only three methods on the interface without a `CancellationToken`, breaking consistency with the other four methods and preventing `ImportBankStatementHandler`/`GetBankStatementByIdHandler` from propagating MediatR's pipeline cancellation token into these operations.

This change adds `CancellationToken cancellationToken = default` to all three methods on the interface and its EF Core implementation, threading the token through from `FindAsync`/`SaveChangesAsync` up through both handlers' call sites, and updates the two affected Moq-based test files so their argument matchers reflect the new two-argument overloads. Pure plumbing — no behavioral change to import logic, dedup logic, or error handling.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — interface signatures gain `CancellationToken cancellationToken = default`
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` — forwards the token into `FindAsync`/`SaveChangesAsync`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` — threads the token through `InsertNewAsync`/`UpsertExistingAsync`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdHandler.cs` — forwards the handler's token to `GetByIdAsync`
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementByIdHandlerTests.cs` — Moq matchers updated for the two-argument overload
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` — Moq matchers updated for the two-argument overload

## Status
DONE
