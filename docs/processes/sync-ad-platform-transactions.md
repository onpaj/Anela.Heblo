---
process: sync-ad-platform-transactions
kind: sync
module: marketing-invoices
summary: Twice-daily pull of Google Ads account budgets and Meta Ads billing transactions into ImportedMarketingTransactions — a table nothing in Heblo reads, empty in production, with both jobs disabled.
owns:
  - backend/src/Anela.Heblo.Application/Features/MarketingInvoices/**
  - backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/**
  - backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/**
  - backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/**
  - backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/**
verified_at: "7f5ce8509"
related: []
---

# Ad-platform billing transactions import (Google Ads + Meta Ads)

## Purpose
Intended to record what Anela is billed by its two big ad platforms — Google Ads and Meta
(Facebook/Instagram) — straight from the platforms' APIs, so ad spend could be matched against
the received invoices (přijaté faktury) in Flexi without waiting for the paper invoice.

**In practice nobody uses it.** No page, no API endpoint, no MCP tool, no report and no other
module reads the target table `public."ImportedMarketingTransactions"`; it is empty in production
and both jobs are switched off (see Runtime facts). Ad spend that Anela actually looks at comes
from Flexi instead:
- the Marketing Performance module (received invoices filtered by supplier DIČ), and
- `sync-flexi-analytics` → Metabase view `flexi_raw.v_ad_spend_monthly` (general ledger).

If someone asks "how much did we spend on Meta/Google ads", answer from those, not from this
process.

## Trigger
Two Hangfire recurring jobs, category **Finance** on the Recurring Jobs admin page, time zone
Europe/Prague, `DefaultIsEnabled = true` (only seeds the first DB row — the admin toggle wins):

| Job id | Cron | Platform | Window |
|---|---|---|---|
| `meta-ads-invoice-import` | `0 6,18 * * *` (06:00, 18:00) | `MetaAds` | now(UTC) − 7 days → now(UTC) |
| `google-ads-invoice-import` | `15 6,18 * * *` (06:15, 18:15) | `GoogleAds` | now(UTC) − 7 days → now(UTC) |

- Each job first asks `IRecurringJobStatusChecker.IsJobEnabledAsync(jobName)`; when the job is
  disabled in `RecurringJobConfigurations` it logs "disabled. Skipping execution." and returns —
  Hangfire still records the run as **Succeeded**. (Missing config row or a DB error → the job runs.)
- No config gate: both adapters are registered unconditionally in `Program.cs`
  (`AddMetaAdsAdapter`, `AddGoogleAdsAdapter`), whether or not credentials exist.
- Exceptions are rethrown, and the job classes have no `[AutomaticRetry]` override, so Hangfire's
  default retry policy (10 attempts with back-off) applies to a failed run.
- Can be triggered by hand from the Recurring Jobs admin page. There is no other entry point.

## Data flow
1. Job builds `ImportMarketingInvoicesRequest { Platform, From = UtcNow − 7 d, To = UtcNow }` and
   sends it through MediatR.
2. `ImportMarketingInvoicesHandler` picks the single `IMarketingTransactionSource` whose
   `Platform` matches (`"MetaAds"` / `"GoogleAds"`); none → `ArgumentException`, more than one →
   `InvalidOperationException`.
3. The source fetches from the platform:
   - **Meta** (`MetaAdsTransactionSource`): `GET https://graph.facebook.com/{ApiVersion}/{AccountId}/transactions`
     `?fields=id,time,amount,currency,payment_type&time_range={"since":"yyyy-MM-dd","until":"yyyy-MM-dd"}`
     (dates in UTC), `Authorization: Bearer {AccessToken}`, follows `paging.next` until exhausted.
   - **Google** (`GoogleAdsTransactionSource` → `SdkAccountBudgetFetcher`): Google Ads API **v18**
     `GoogleAdsService.SearchStream` on customer `CustomerId` (dashes stripped), GAQL:
     `SELECT account_budget.id, account_budget.name, account_budget.approved_start_date_time,
     account_budget.amount_served_micros, customer.currency_code FROM account_budget
     WHERE account_budget.status = 'APPROVED' AND approved_start_date_time` between
     `from 00:00:00` and `to 23:59:59`.
4. `MarketingInvoiceImportService.ImportAsync` validates and de-duplicates each row (see Logic),
   stages new rows and saves them in **one** `SaveChangesAsync`.
5. Target: `public."ImportedMarketingTransactions"` in the main Heblo DB. The job logs
   `Imported / Skipped / Failed` counts; nothing else is notified.

## Logic & formulas
**Mapping to `ImportedMarketingTransactions`:**

| Column | Meta Ads | Google Ads |
|---|---|---|
| `Platform` | `"MetaAds"` | `"GoogleAds"` |
| `TransactionId` | Graph transaction `id` | `account_budget.id` |
| `Amount` (numeric 18,2) | `amount / 100` (assumes the API returns minor units, e.g. haléře/cents) | `amount_served_micros / 1 000 000` |
| `Currency` (3 chars) | `currency` | `customer.currency_code` |
| `TransactionDate` | `time` (Unix seconds) → UTC | `approved_start_date_time` parsed → `.ToUniversalTime()` |
| `Description` | `payment_type` | budget `name`, or `"Google Ads billing period"` if empty |
| `RawData` (text) | the transaction item serialised to JSON | the raw budget row serialised to JSON |
| `ImportedAt` | `DateTime.UtcNow` | `DateTime.UtcNow` |

Both dates are stored as UTC in `timestamp without time zone` columns.

**Filters & dedup (per run):**
- Meta: rows whose `time` falls outside the exact `From`–`To` instant range are dropped (the API
  `time_range` is day-granular, so the edges are trimmed in code).
- Empty `Currency` → row counted as **Failed**, not stored.
- Same `TransactionId` twice in one run → **Skipped**.
- Row already in the table (`Platform` + `TransactionId`, one `AnyAsync` query per row) →
  **Skipped**. The unique index `IX_ImportedMarketingTransactions_Platform_TransactionId`
  backs this. Existing rows are **never updated**.
- Any other per-row exception → **Failed**, the loop continues. If the final `SaveChangesAsync`
  throws, the whole batch is lost and the exception propagates (job fails, Hangfire retries).

Amounts are whatever the platform reports — no VAT handling, no currency conversion.

**Resilience:** Meta retries only HTTP 429 (Polly: 3 retries, exponential from 2 s, jitter);
every other non-2xx fails the run. Google relies on the SDK's own behaviour; the
`GoogleAdsClient` is cached and rebuilt when any `GoogleAds:*` setting changes.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `MetaAds:AccountId` | `act_XXXXXXXXX` (placeholder) | Meta ad account, form `act_<number>` |
| `MetaAds:AccessToken` | *(no key in appsettings — empty)* | Meta Business Manager system-user token; secret (Key Vault) |
| `MetaAds:ApiVersion` | `v21.0` | Graph API version in the URL |
| `GoogleAds:CustomerId` | `XXX-XXX-XXXX` (placeholder) | Google Ads customer id; also used as `LoginCustomerId` |
| `GoogleAds:DeveloperToken` | `-- stored in secrets.json --` | Google Ads API developer token; secret |
| `GoogleAds:OAuth2ClientId` / `OAuth2ClientSecret` / `OAuth2RefreshToken` | `-- stored in secrets.json --` | OAuth2 credentials; secrets |
| `RecurringJobConfigurations` row per job id | seeded `IsEnabled = true`, cron from metadata | Admin-owned enable flag and cron (DB wins over code) |

## Runtime facts
- `public."ImportedMarketingTransactions"` is **empty** in production (`Heblo_V3`); the owner
  confirmed the direct-API approach "doesn't work" and that ad spend must come from Flexi received
  invoices — agent memory `gotcha_issued_invoice_price_is_with_vat` — 2026-09-16.
- In production both `google-ads-invoice-import` and `meta-ads-invoice-import` have
  `IsEnabled = false` in `RecurringJobConfigurations`, yet Hangfire (`hangfire_heblo`) shows
  `Succeeded` runs — they fire on schedule, see the flag and return immediately — agent memory
  `gotcha_adapter_recurring_job_needs_concrete_registration` — 2026-09-22.
- Meta, Google and S-klik ad spend in the Flexi general ledger (`v_ad_spend_monthly`) matches the
  Marketing Performance figures for Meta and Google to the haléř (S-klik does not) — agent memory
  `gotcha_sklik_ad_cost_negative_in_heblo` — 2026-09 (cross-check of 2026-01..2026-09).
- No `GoogleAds*` secrets existed in `kv-heblo-stg` or `kv-heblo-prod`, and no `GoogleAds*` App Settings on `heblo` / `heblo-test`, until 2026-10-07, so the Google billing job could never authenticate. On 2026-10-07 `GoogleAds--CustomerId`, `--OAuth2ClientId`, `--OAuth2ClientSecret`, `--OAuth2RefreshToken` and `--HebloUserEmail` were set in both vaults (for the MarketingAds read source); `GoogleAds--DeveloperToken` is still absent. The job returns early while disabled (it is disabled in prod); once enabled it has no is-configured gate and a failure in the V18 call is logged and rethrown. How the V18 path behaves with the new secrets and an empty developer token is unverified — code read of `GoogleAdsInvoiceImportJob` / `SdkAccountBudgetFetcher`, Google Ads access spike (`docs/integrations/google-ads-api.md` §10–11) — 2026-10-07.
- Google sunset Ads API developer tokens on 2026-09-09 (access now follows the OAuth client's Cloud project); `GoogleAds:DeveloperToken` is no longer an access gate — Google Ads API docs — 2026-10-07.

## Known quirks
- **Dead data.** Nothing reads the table (no query, endpoint, page or MCP tool references
  `ImportedMarketingTransactions` outside this module). Do not build new features on it — memory
  note 2026-09-16.
- **"Invoice import" is a misnomer.** The job names and the `Finance` category suggest invoices;
  neither platform call returns invoices. Meta returns billing *transactions* (card charges /
  threshold payments); Google returns *account budgets*.
- **Google amounts are frozen at first sight.** `amount_served_micros` is the running total
  spent against an account budget, but a budget is imported once (dedup by id) and never updated.
  Because only budgets whose `approved_start_date_time` is within the last 7 days are queried,
  the stored amount is typically close to zero. Budgets approved earlier are never imported at
  all, and accounts without account-budget (monthly invoicing) billing return nothing.
- **Google Ads API version pinned to v18** (`Services.V18.GoogleAdsService`). V18 is sunset, and SDK 21.1.0 contains nothing newer than V18, so the Google billing import cannot work without rewriting the fetcher. The MarketingAds backbone (`sync-google-ads-campaign-data`) uses REST v25 instead and does not touch this path.
- **Meta amount scale is assumed.** `amount / 100` presumes minor currency units; not verified
  against a real response in the repo (tests use synthetic data).
- **Enabling it without secrets fails loudly.** With placeholders (`act_XXXXXXXXX`, empty token)
  the Meta call gets a 4xx → `HttpRequestException` → the run fails and Hangfire retries it ~10
  times. The placeholders are non-blank, so no "is configured" check would catch them either.
- **Historical currency backfill.** Migration `20260525152436_AddCurrencyDescriptionRawDataToImportedMarketingTransactions`
  set `Currency = 'CZK'` on all pre-existing rows; irrelevant while the table is empty.
- **One query per row** for the dedup check (N+1) — harmless at these volumes.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsInvoiceImportJob.cs` / `.../GoogleAds/GoogleAdsInvoiceImportJob.cs` — job ids, cron, 7-day window, enable check
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs` — Graph API call, paging, 429 retry, mapping
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/SdkAccountBudgetFetcher.cs` — GAQL query, client caching
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs` — micros → amount mapping
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesHandler.cs` — source selection by platform
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — validation, dedup, single save
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs` — table, columns, unique index
