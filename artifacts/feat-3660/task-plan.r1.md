# Task Plan: Move `IMarketingTransactionSource` and `MarketingTransaction` from Domain to Application/Contracts

## Overview
Relocate two Domain types (`IMarketingTransactionSource`, `MarketingTransaction`) that are actually an adapter port and its DTO into `Anela.Heblo.Application/Features/MarketingInvoices/Contracts/`, updating namespace/using declarations across all adapter, Application, and test consumers. Pure structural refactor, no behavior change, no `.csproj` edits expected.

### task: move-marketing-transaction-source-to-application-contracts
## Goal
Move `IMarketingTransactionSource` and `MarketingTransaction` from `Anela.Heblo.Domain.Features.MarketingInvoices` to `Anela.Heblo.Application.Features.MarketingInvoices.Contracts`, and fix every consumer's namespace/using so the solution builds cleanly with no behavioral change. `ImportedMarketingTransaction` and `IImportedMarketingTransactionRepository` must remain untouched in Domain.

## Files to change

**Create (new location, namespace `Anela.Heblo.Application.Features.MarketingInvoices.Contracts`):**
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/IMarketingTransactionSource.cs`
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingTransaction.cs`

**Delete (old location):**
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IMarketingTransactionSource.cs`
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs`

**Do not touch (stay in Domain, unchanged):**
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs`
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs`

**Edit — swap Domain using for Contracts using (sole reason for the using in these files):**
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs` (keep unrelated `using Anela.Heblo.Domain.Features.BackgroundJobs;`)
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs` (keep unrelated `using Anela.Heblo.Domain.Features.BackgroundJobs;`)
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/IMarketingInvoiceImportService.cs`
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs` (drop the now-dead Domain using)

**Edit — add Contracts using while keeping Domain using (still needed for `IImportedMarketingTransactionRepository`/`ImportedMarketingTransaction`):**
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

**Verify only, no change expected:**
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Anela.Heblo.Adapters.MetaAds.csproj` — confirm it references `Anela.Heblo.Application` and not `Anela.Heblo.Domain`
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Anela.Heblo.Adapters.GoogleAds.csproj` — same check
- `Application/Features/MarketingInvoices/MarketingInvoicesModule.cs` — confirmed by arch review to reference neither moved type; leave as-is

## Steps
1. Run a repo-wide `git grep -n "IMarketingTransactionSource"` and `git grep -n "MarketingTransaction\b"` (excluding `ImportedMarketingTransaction`) to confirm the file list above is complete before making changes; add any additional hits to scope.
2. Create `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/IMarketingTransactionSource.cs` with namespace `Anela.Heblo.Application.Features.MarketingInvoices.Contracts` and the interface body unchanged (`Platform` property, `GetTransactionsAsync(DateTime from, DateTime to, CancellationToken ct)`).
3. Create `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingTransaction.cs` with namespace `Anela.Heblo.Application.Features.MarketingInvoices.Contracts`, as a plain class (not a record) with all six properties unchanged (`TransactionId`, `Amount`, `TransactionDate`, `Description`, `Currency`, `RawData`).
4. Delete the two old files from `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/`: `IMarketingTransactionSource.cs` and `MarketingTransaction.cs`.
5. Update each of the "swap" files (adapters' `*TransactionSource.cs`, adapters' `*AdapterServiceCollectionExtensions.cs`, `ImportMarketingInvoicesHandler.cs`, `IMarketingInvoiceImportService.cs`, `ImportMarketingInvoicesHandlerTests.cs`): replace `using Anela.Heblo.Domain.Features.MarketingInvoices;` with `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;`, preserving any other unrelated usings already present (e.g. `BackgroundJobs`).
6. Update `MarketingInvoiceImportService.cs` and `MarketingInvoiceImportServiceTests.cs`: add `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;` while keeping the existing `using Anela.Heblo.Domain.Features.MarketingInvoices;` (still required for `IImportedMarketingTransactionRepository`/`ImportedMarketingTransaction`).
7. Check `Anela.Heblo.Adapters.MetaAds.csproj` and `Anela.Heblo.Adapters.GoogleAds.csproj`: confirm each already has `<ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />` and no direct `Anela.Heblo.Domain` reference. No edit expected; only change if build reveals otherwise.
8. Run `dotnet build` on the solution; fix any remaining CS0246/using errors surfaced by files not in the enumerated list.
9. Run the full backend test suite, focusing on `ImportMarketingInvoicesHandlerTests` and `MarketingInvoiceImportServiceTests`, and confirm they pass unmodified in behavior (only using/namespace edits permitted in test files).
10. Run `dotnet format` to ensure formatting compliance.
11. Re-run `git grep -n "Domain.Features.MarketingInvoices"` repo-wide to confirm the only remaining hits are for `ImportedMarketingTransaction` / `IImportedMarketingTransactionRepository`, and `git grep -n "IMarketingTransactionSource\|MarketingTransaction\b"` to confirm no stray references to the old namespace remain.

## Acceptance criteria
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IMarketingTransactionSource.cs` and `MarketingTransaction.cs` no longer exist.
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/IMarketingTransactionSource.cs` and `MarketingTransaction.cs` exist with namespace `Anela.Heblo.Application.Features.MarketingInvoices.Contracts`, member signatures/properties byte-for-byte unchanged aside from namespace.
- `MarketingTransaction` remains a plain class, not a record.
- `dotnet build` succeeds for the whole solution with no errors.
- `dotnet format` reports no changes needed (or has been applied).
- All existing tests pass, including `ImportMarketingInvoicesHandlerTests` and `MarketingInvoiceImportServiceTests`, with no behavioral changes (diffs limited to using/namespace lines).
- `ImportedMarketingTransaction` and `IImportedMarketingTransactionRepository` are unchanged and remain in `Anela.Heblo.Domain.Features.MarketingInvoices`.
- No file outside `Anela.Heblo.Application` retains a `using Anela.Heblo.Domain.Features.MarketingInvoices;` unless it still needs `ImportedMarketingTransaction`/`IImportedMarketingTransactionRepository` from that namespace.
- `Anela.Heblo.Adapters.MetaAds.csproj` and `Anela.Heblo.Adapters.GoogleAds.csproj` reference `Anela.Heblo.Application`; no unnecessary `Anela.Heblo.Domain` reference added or left dangling for these types.
- `MarketingInvoicesModule.cs` is unchanged.
- No changes to any controller, MediatR request/response class, HTTP-exposed DTO, or the generated OpenAPI/TypeScript client.
- A repo-wide search confirms no remaining reference to `IMarketingTransactionSource` or `MarketingTransaction` under the old `Anela.Heblo.Domain.Features.MarketingInvoices` namespace.
