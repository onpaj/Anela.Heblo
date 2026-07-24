# Design: Move AnalyticsRepository out of Persistence

No UI surface — this is a pure backend file relocation with no behavior, contract, or API change. UX/UI section omitted.

## Component design

No new components are introduced. Three existing files change identity (path/namespace) or content (using/comment); nothing else in the dependency graph is touched.

### 1. `AnalyticsRepository` (relocated)

**Current:** `backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs`, namespace `Anela.Heblo.Persistence.Features.Analytics`.

**New:** `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs`, namespace `Anela.Heblo.Application.Features.Analytics.Infrastructure`.

Responsibility is unchanged: a `sealed class AnalyticsRepository : IAnalyticsRepository` that adapts three Analytics-owned source interfaces (`IAnalyticsProductSource`, `IInvoiceImportStatisticsSource`, `IBankStatementStatisticsSource`, all from `Anela.Heblo.Domain.Features.Analytics`) to the `IAnalyticsRepository` contract. All four members, their signatures, XML doc comments, and bodies are byte-for-byte identical except for the `namespace` line — this is confirmed precedented placement: `Application/Features/{Feature}/Infrastructure/` already holds 20+ analogous non-DB adapters (e.g. `Invoices/Infrastructure/InvoiceConsumptionSourceAdapter`, `Bank/Infrastructure/Jobs/*ImportJob`, `Catalog/Infrastructure/*`).

`IAnalyticsRepository` itself (Domain layer) and the three source interfaces are untouched — their contracts, namespaces, and implementations are out of scope.

### 2. `AnalyticsModule.cs` (registration site)

`backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` changes in exactly two spots:

- `using Anela.Heblo.Persistence.Features.Analytics;` → `using Anela.Heblo.Application.Features.Analytics.Infrastructure;`
- Comment above the DI line: `// Repository (implementation lives in the Persistence layer)` → `// Repository (implementation lives in Application/Features/Analytics/Infrastructure)`

The registration line itself is unchanged:
```csharp
services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
```
Effect: `AnalyticsModule` (Application layer) no longer imports anything from `Anela.Heblo.Persistence` for this type, closing the layering smell described in the finding.

### 3. `AnalyticsRepositoryTests.cs` (test)

**Current:** `backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs`, `using Anela.Heblo.Persistence.Features.Analytics;`.

**New:** `backend/test/Anela.Heblo.Tests/Features/Analytics/Infrastructure/AnalyticsRepositoryTests.cs`, `using Anela.Heblo.Application.Features.Analytics.Infrastructure;`.

The open question in the plan is resolved by direct evidence: the test tree already mirrors `Infrastructure/` subfolders for every sibling module that has one — confirmed present for Invoices, Bank, Catalog, Purchase, Manufacture, Logistics, Leaflet, FileStorage, Dashboard, KnowledgeBase, BackgroundJobs, CarrierCooling (`backend/test/Anela.Heblo.Tests/Features/*/Infrastructure/`). Analytics currently has no `Infrastructure/` test folder because it has no `Infrastructure/` source folder yet — this move creates the first one on both sides, so the test relocates to match. No test logic, assertions, or mocks change — only the file's directory and its `using` line.

## Data schemas

Not applicable. No DTOs, entities, database schema, request/response shapes, or event payloads are touched by this change. `IAnalyticsRepository`'s method signatures (`StreamProductsWithSalesAsync`, `GetProductAnalysisDataAsync`, `GetInvoiceImportStatisticsAsync`, `GetBankStatementImportStatisticsAsync`) and their DTO parameters/returns (`AnalyticsProduct`, `DailyInvoiceCount`, `DailyBankStatementStatistics`, etc.) are unchanged in shape and location (they remain in `Anela.Heblo.Domain.Features.Analytics`).

## File-level diff summary

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs` | deleted (moved) |
| `backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs` | new (same content, new `namespace`) |
| `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` | `using` swapped; stale comment corrected |
| `backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs` | deleted (moved) |
| `backend/test/Anela.Heblo.Tests/Features/Analytics/Infrastructure/AnalyticsRepositoryTests.cs` | new (same content, new `namespace`/`using`) |

No other file references `Anela.Heblo.Persistence.Features.Analytics` (verified: only `AnalyticsModule.cs` and `AnalyticsRepositoryTests.cs` import it; handlers and `InvoiceImportStatisticsTile` depend solely on `IAnalyticsRepository` from `Anela.Heblo.Domain.Features.Analytics`, unaffected).
