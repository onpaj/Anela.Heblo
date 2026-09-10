# Design: Remove `GetCacheStatus()` from `IFinancialAnalysisService`

## Component Design

No new or restructured components. This change narrows the visibility of one existing member on one existing class/interface pair within the `FinancialOverview` module.

- **`IFinancialAnalysisService`** (`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/IFinancialAnalysisService.cs`) — drops the `GetCacheStatus()` declaration (and its XML doc comment). Retains exactly its other three members: `GetFinancialOverviewAsync`, `RefreshFinancialDataAsync`, `GetFinancialComparisonAsync`. Consumers (MediatR handlers for `GetFinancialOverview` and `GetFinancialComparison`, and the background refresh task) are unaffected since none call `GetCacheStatus()` through the interface.
- **`FinancialAnalysisService`** (`.../Services/FinancialAnalysisService.cs`) — `GetCacheStatus()` changes from `public` to `private`. Body, computation logic, and both existing internal self-calls (`this`-implicit, inside `GetFinancialOverviewAsync`) are unchanged. No longer implements an interface member, so its XML doc comment is kept as-is per spec (it documents real internal behavior, not just a copy of interface doc text).

No changes to `FinancialOverviewController`, `FinancialOverviewModule` DI registration/wiring, or `FinancialAnalysisCacheStatus`.

## Data Schemas

No schema, database, API request/response, or event payload changes. `FinancialAnalysisCacheStatus` (`LastRefresh`, `CachedMonthsCount`, `CachedStockMonthsCount`) is unchanged and remains internal to `FinancialAnalysisService` as the now-private method's return type — it was never serialized over HTTP or exposed via the OpenAPI-generated client, and continues not to be.
