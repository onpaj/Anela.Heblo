# Issue #3660: [arch-review] MarketingInvoices: IMarketingTransactionSource and MarketingTransaction belong in Application/Contracts, not Domain

## Module
MarketingInvoices

## Finding
`IMarketingTransactionSource` (`backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IMarketingTransactionSource.cs`) and `MarketingTransaction` (`backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs`) live in the Domain layer, but neither is a domain concept:

- **`IMarketingTransactionSource`** is an outbound port for external ad-platform adapters (Meta, Google). It abstracts HTTP/SDK calls to third-party APIs. Domain interfaces should represent repository contracts or domain services — not adapter ports for external integrations.
- **`MarketingTransaction`** is a data carrier returned by that interface. It contains `RawData` (raw JSON from an external API), has no invariants, no validation, and no behavior. It is a DTO, not a domain type.

The Domain layer currently contains four things: `ImportedMarketingTransaction` (the persisted entity — correct), `IImportedMarketingTransactionRepository` (repository interface — correct), and the two items above which don't belong there.

The only reason they appear to be in Domain is that `MarketingTransaction` needs a location that both the adapter projects and the Application layer can see without a circular reference. Domain is the lowest-common denominator. That constraint disappears if both types are moved together to Application.

## Why it matters
Per `docs/architecture/development_guidelines.md`, Domain should contain only entities, value objects, domain service interfaces, and repository contracts. Per `docs/architecture/filesystem.md`, the canonical home for feature-level contracts consumed by Application services is `Application/Features/{Feature}/Contracts/`.

Placing an adapter port in Domain means the project's cleanest, most stable layer now knows about "ad-platform source" semantics and raw API payloads. Domain is supposed to be the innermost ring; it should have zero knowledge of external integrations.

The adapters (Meta, Google) currently depend on Domain for `IMarketingTransactionSource`. After the move the dependency direction becomes `Adapters → Application → Domain`, which is still correct Clean Architecture and more accurately reflects that this is an application-level integration concern.

## Suggested fix
1. Move `IMarketingTransactionSource` → `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/IMarketingTransactionSource.cs`
2. Move `MarketingTransaction` → `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingTransaction.cs`
3. Update adapter `.csproj` files (`Anela.Heblo.Adapters.MetaAds`, `Anela.Heblo.Adapters.GoogleAds`) to reference `Anela.Heblo.Application` for these two types (they may already reference it for `ImportMarketingInvoicesRequest`/`ImportMarketingInvoicesResponse` — check first)
4. Fix `namespace` declarations and `using` directives in all affected files (adapters, `ImportMarketingInvoicesHandler.cs`, `MarketingInvoiceImportService.cs`, `IMarketingInvoiceImportService.cs`)

No behavioral change — this is a namespace/project placement correction only.

---
_Filed by daily arch-review routine on 2026-07-15._
