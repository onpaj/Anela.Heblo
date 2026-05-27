## Module
MarketingInvoices

## Finding
Both `MetaAdsInvoiceImportJob` and `GoogleAdsInvoiceImportJob` violate two related principles:

**1. `MarketingInvoiceImportService` is manually constructed via `new` rather than injected.**

- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsInvoiceImportJob.cs:54`
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsInvoiceImportJob.cs:54`

```csharp
var service = new MarketingInvoiceImportService(_source, _repository, _importLogger);
```

The service is not registered with DI (`MarketingInvoicesModule` only registers `IImportedMarketingTransactionRepository`), so jobs construct it themselves. This means the DI container does not manage its lifetime, and any future decorator, middleware, or registration change is invisible to the jobs.

The established pattern for recurring jobs in this project (see `DailyConsumptionJob`, `KnowledgeBaseIngestionJob`) is to dispatch a MediatR command rather than construct application services directly.

**2. Both jobs inject the concrete source type instead of the `IMarketingTransactionSource` interface.**

- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsInvoiceImportJob.cs:10,28` — injects `MetaAdsTransactionSource` directly
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsInvoiceImportJob.cs:10,26` — injects `GoogleAdsTransactionSource` directly

Both concrete types implement `IMarketingTransactionSource`, which is defined precisely to allow the job logic to be decoupled from the platform implementation.

## Why it matters
- The DI bypass means the service's lifetime is mismatched (scoped `IImportedMarketingTransactionRepository` is passed into a manually-constructed service inside a scoped job — this happens to work today but is fragile).
- Future cross-cutting concerns (e.g. observability, retry wrapping) added to `MarketingInvoiceImportService` via DI decorators will not apply to these jobs.
- Injecting concrete types instead of the interface means the job cannot be tested by substituting a mock source; it couples the adapter to the concrete class.
- Inconsistency with every other job in the project (all use `IMediator`).

## Suggested fix
**Option A (preferred — aligns with project pattern):** Introduce an `ImportMarketingInvoicesCommand` MediatR handler in `Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/`. Register `MarketingInvoiceImportService` with DI. Each job dispatches the command via `IMediator`, passing only the platform name or date range; the handler resolves the right `IMarketingTransactionSource` implementation.

**Option B (minimal):** Register `MarketingInvoiceImportService` with DI, make each job inject it alongside `IMarketingTransactionSource` (the interface), and remove the manual `new`.

Either fix eliminates the concrete-type dependency by changing constructor parameters from `MetaAdsTransactionSource`/`GoogleAdsTransactionSource` to `IMarketingTransactionSource`.

---
_Filed by daily arch-review routine on 2026-05-18._