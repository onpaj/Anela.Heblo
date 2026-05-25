# Specification: Refactor MarketingInvoices Import Jobs to Use MediatR and DI

## Summary
`MetaAdsInvoiceImportJob` and `GoogleAdsInvoiceImportJob` manually construct `MarketingInvoiceImportService` via `new` and inject concrete transaction-source types instead of the `IMarketingTransactionSource` abstraction. This refactor introduces an `ImportMarketingInvoicesCommand` MediatR use case in the Application layer, registers all import dependencies with DI, and reduces both jobs to thin dispatchers — aligning them with every other recurring job in the project (`DailyConsumptionJob`, `KnowledgeBaseIngestionJob`).

## Background
The project uses Clean Architecture with Vertical Slice organization, MediatR commands for use cases, and a constructor-injection DI policy. Recurring jobs (`IRecurringJob`) are expected to be thin: check whether the job is enabled, dispatch a MediatR request, log the outcome.

The two marketing-invoice import jobs deviate from this pattern in two ways:

1. **Manual service construction.** Both jobs call `new MarketingInvoiceImportService(_source, _repository, _importLogger)` (`MetaAdsInvoiceImportJob.cs:54`, `GoogleAdsInvoiceImportJob.cs:54`). The service is never registered with DI — `MarketingInvoicesModule` only registers `IImportedMarketingTransactionRepository`. As a result, the DI container does not manage the service's lifetime, and any future decorator, middleware, or registration change applied to the service is invisible to the jobs.

2. **Concrete-type injection.** Both jobs inject the concrete `MetaAdsTransactionSource` / `GoogleAdsTransactionSource` instead of the `IMarketingTransactionSource` interface that exists precisely to decouple the import logic from the platform implementation. This couples each job to its concrete adapter class and prevents substituting a mock source in tests.

Both concrete sources already implement `IMarketingTransactionSource` and expose a `Platform` string property (`"MetaAds"`, `"GoogleAds"`). `IMarketingTransactionSource` lives in the Domain layer, so a handler in the Application layer can depend on it without violating Clean Architecture layering.

This finding was filed by the daily arch-review routine on 2026-05-18. The brief recommends **Option A** (MediatR command) as the preferred fix; this specification adopts Option A.

## Functional Requirements

### FR-1: Introduce `ImportMarketingInvoicesCommand` MediatR use case
New slice under `Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/` with `ImportMarketingInvoicesRequest` (`Platform`, `From`, `To`), `ImportMarketingInvoicesResponse : BaseResponse` (`Platform`, `Imported`, `Skipped`, `Failed`), and `ImportMarketingInvoicesHandler`. The handler injects `IEnumerable<IMarketingTransactionSource>`, selects the source matching `request.Platform`, runs the import, and maps the result. Fails fast on unknown or duplicate platforms.

### FR-2: Register `MarketingInvoiceImportService` with DI
Register the service as scoped in `MarketingInvoicesModule` (matches the scoped repository). Eliminate every `new MarketingInvoiceImportService(...)`.

### FR-3: Register transaction sources against the `IMarketingTransactionSource` interface
Update both adapter DI extensions so each source resolves as `IMarketingTransactionSource` (sharing the same scoped instance as its concrete registration), enabling FR-1's enumerable injection.

### FR-4 / FR-5: Reduce both jobs to MediatR dispatchers
`MetaAdsInvoiceImportJob` and `GoogleAdsInvoiceImportJob` inject only `IMediator`, `IRecurringJobStatusChecker`, and their logger. They keep the enabled-check, 7-day UTC window, dispatch the command, log counts, and preserve catch-log-rethrow. Job metadata (cron, names) is unchanged.

### FR-6: Test coverage
Existing `MarketingInvoiceImportServiceTests` stay green; add handler tests (source selection, mapping, unknown/duplicate platform) and job tests (disabled short-circuit, correct request dispatched, exception logged + rethrown). 80% coverage on changed files.

## Key decisions
- **Option A chosen** over Option B — aligns with the project-wide MediatR job pattern.
- **No `IMarketingInvoiceImportService` interface** — registered by concrete type (YAGNI; a decorator can be added later).
- **Two jobs retained** — separate cron schedules and enable/disable toggles.
- Behavior-neutral: no API/UI changes, no DB migration, Hangfire retry semantics unchanged.

The full specification is written to `artifacts/feat-arch-review-marketinginvoices-import-job/spec.md`.

## Status: COMPLETE