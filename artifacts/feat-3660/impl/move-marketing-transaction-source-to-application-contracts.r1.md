# Implementation: move-marketing-transaction-source-to-application-contracts

## What was implemented
Moved `IMarketingTransactionSource` and `MarketingTransaction` from the Domain
layer (`Anela.Heblo.Domain.Features.MarketingInvoices`) to the Application
layer (`Anela.Heblo.Application.Features.MarketingInvoices.Contracts`), and
updated every consumer's `using` statements accordingly. No `.csproj` changes
were needed — both adapter projects already referenced
`Anela.Heblo.Application`. `ImportedMarketingTransaction` and
`IImportedMarketingTransactionRepository` were left untouched in Domain.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/IMarketingTransactionSource.cs` — moved from Domain (git rename), namespace updated
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingTransaction.cs` — moved from Domain (git rename), namespace updated
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs` — using updated
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs` — using updated
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs` — using updated
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs` — using updated
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/IMarketingInvoiceImportService.cs` — using updated
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — using updated (kept Domain using for `ImportedMarketingTransaction`/repository)
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesHandler.cs` — using updated
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs` — using updated
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` — using updated (kept Domain using for entity/repository types)

`MarketingInvoicesModule.cs` and `ApplicationDbContext.cs` needed no change —
their `Anela.Heblo.Domain.Features.MarketingInvoices` using statements are for
`ImportedMarketingTransaction`, which correctly stays in Domain.

## Tests
Ran `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingInvoices"`: **14/14 passed**.

## How to verify
1. `dotnet build Anela.Heblo.sln` from the repo root — builds clean (0 errors).
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingInvoices"` — all pass.
3. `grep -rn "Anela.Heblo.Domain.Features.MarketingInvoices" backend/ --include="*.cs"` — every remaining hit references `ImportedMarketingTransaction` or `IImportedMarketingTransactionRepository` (the entity/repository that correctly stays in Domain); no stray references to the moved types remain.

## Notes
No deviations from the plan. No csproj edits were required, as anticipated by
the architecture review.

## PR Summary
Moved `IMarketingTransactionSource` (an outbound port for external ad-platform
adapters) and its `MarketingTransaction` DTO out of the Domain layer into
`Application/Features/MarketingInvoices/Contracts`, since neither is a domain
concept — the interface is an adapter port and the DTO is a raw external-API
data carrier with no domain behavior. This restores Domain as the innermost,
integration-agnostic layer while keeping the dependency direction correct
(`Adapters → Application → Domain`). Purely a namespace/project placement
change — no behavior was altered.

### Changes
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/IMarketingTransactionSource.cs` (new location)
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingTransaction.cs` (new location)
- Updated `using` statements in the Meta/Google Ads adapters, the marketing invoice import service/handler, and their unit tests

## Status
DONE
