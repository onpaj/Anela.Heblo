---
process: module-marketing-invoices
kind: module
module: marketing-invoices
summary: Background-only import of Google Ads and Meta Ads billing data into a staging table that nothing reads; disabled and empty in production.
owns: []
verified_at: "5e993f9e2"
related: []
---

# Marketing Invoices (ad-platform billing import)

## Purpose
Built to capture what Anela pays Google Ads and Meta (Facebook/Instagram) for advertising directly
from the platforms' APIs, as a basis for checking ad-platform invoices. The data was never put to
use: there is no screen, report or consumer, the production table is empty and both import jobs
are switched off. Anela's real ad-spend figures come from Flexi — the Marketing Performance module
(received invoices by supplier DIČ) and the Metabase view `v_ad_spend_monthly`
(`sync-flexi-analytics`).

Treat this module as dormant infrastructure, not as a source of truth.

## Users & screens
None. No frontend route, no API controller, no MCP tool. The only visible trace is two jobs in
the **Finance** group of the Recurring Jobs admin page (`meta-ads-invoice-import`,
`google-ads-invoice-import`), where an admin can enable, disable, reschedule or trigger them.

## Processes
- `sync-ad-platform-transactions` — Meta Ads Graph `/transactions` and Google Ads `account_budget`
  → `ImportedMarketingTransactions`, de-duplicated by platform + transaction id; Hangfire
  `meta-ads-invoice-import` (`0 6,18 * * *`) and `google-ads-invoice-import` (`15 6,18 * * *`),
  7-day lookback each.

No user actions; no plain CRUD.

## Data owned
- `public."ImportedMarketingTransactions"` — one row per platform billing item (Meta transaction or
  Google account budget): `Platform`, `TransactionId` (unique together), `Amount` numeric(18,2),
  `Currency`, `TransactionDate` (UTC), `ImportedAt`, `Description`, `RawData` (source JSON).
  Insert-only; rows are never updated or deleted.

## External systems
- **Meta Graph API** (read) — `GET graph.facebook.com/{MetaAds:ApiVersion}/{MetaAds:AccountId}/transactions`,
  bearer token `MetaAds:AccessToken`.
- **Google Ads API v18** (read) — `GoogleAdsService.SearchStream` GAQL over `account_budget`,
  credentials `GoogleAds:*` (developer token + OAuth2 refresh token).

Nothing is written to any external system.

## Dependencies
- Reads: Background Jobs (`RecurringJobConfigurations` enable flag via `IRecurringJobStatusChecker`).
- Read by: nobody. Marketing Performance and the Flexi analytics/Metabase reporting compute ad
  spend independently from Flexi.

## Known quirks
- Dead feature: table empty in prod, the owner confirmed the direct-API approach "doesn't work"
  (agent memory, 2026-09-16); both jobs `IsEnabled = false` in prod yet still fire and show
  `Succeeded` because they skip themselves inside `ExecuteAsync` (agent memory, 2026-09-22).
- Google imports account budgets, not invoices; the served amount is frozen at first import and
  only budgets approved in the last 7 days are ever seen — see `sync-ad-platform-transactions`.
- Adapters are registered with no config gate and only placeholder settings in `appsettings.json`;
  enabling a job without real secrets makes every run fail and retry.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingInvoicesModule.cs` — DI registration (repository + import service)
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs` — Meta source + job registration
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs` — Google source + job registration
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs` — entity
- `backend/src/Anela.Heblo.API/Program.cs` — `AddMetaAdsAdapter` / `AddGoogleAdsAdapter` calls
