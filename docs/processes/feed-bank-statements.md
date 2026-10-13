---
process: feed-bank-statements
kind: feed
module: bank
summary: Downloads daily payout statements (ABO files) from the Comgate (CZK, EUR) and Shoptet Pay (CZK) payment gateways and loads them into the matching FlexiBee bank accounts, logging each statement in BankStatements behind a per-account watermark.
owns:
  - backend/src/Anela.Heblo.Application/Features/Bank/**
  - backend/src/Anela.Heblo.Domain/Features/Bank/**
  - backend/src/Anela.Heblo.Persistence/Features/Bank/**
  - backend/src/Adapters/Anela.Heblo.Adapters.Comgate/**
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPay/**
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPayAdapterServiceCollectionExtensions.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Bank/**
  - backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs
verified_at: "5e993f9e2"
related: []
---

# Payment-gateway statements → FlexiBee bank accounts

## Purpose
Card and online payments on the e-shop are collected by payment gateways, which pay the money
out to Anela's bank account in batches ("převody" / payouts). For accounting to match each
payout to the paid orders, the gateway's itemised statement (bankovní výpis, ABO format) must be
loaded into the corresponding bank account in FlexiBee. This process does that every morning for
three gateway accounts, so the accountant does not have to download and upload the files by hand.

Heblo does not transform the money data: it passes the gateway's ABO file to FlexiBee unchanged.
In Heblo it keeps only an audit log — one row per statement with its date, account, number of
items and the result. Staff look at that log on **Bankovní import** (`/customer/bank-statements-overview`,
tab *Import*), can import a single day by hand there, and see a daily count chart on
`/finance/bank-statements` and on the dashboard tile *bankstatementimportstatistics*.

## Trigger
Three Hangfire recurring jobs (category Finance, time zone Europe/Prague, enabled by default,
each can be disabled/triggered on the Recurring Jobs admin page):

| Job id | Cron | Account name | Import window ends |
|---|---|---|---|
| `daily-comgate-czk-import` | `30 4 * * *` | `ComgateCZK` | yesterday |
| `daily-comgate-eur-import` | `40 4 * * *` | `ComgateEUR` | yesterday |
| `daily-shoptetpay-czk-import` | `50 4 * * *` | `ShoptetPay-CZK` | today |

The jobs have no `[AutomaticRetry]` attribute, so a run that throws is retried by Hangfire's
default policy.

On demand: `POST /api/bank-statements/import` `{accountName, dateFrom, dateTo}` (permission
*Bankovní výpisy* = `Customer_BankStatements`). The UI button *Import* on the Import tab always
sends one day (`dateFrom = dateTo` = chosen date, default yesterday).

## Data flow
1. **Window** (`BankImportJobBase`, jobs only): read `public."BankImportStates"` row for the account.
   - `DateFrom` = `LastValidImportDate` (inclusive — the last good day is always re-read).
   - No row yet (bootstrap): `DateFrom` = latest `StatementDate` of that account in
     `BankStatements`, or the target end if there is none.
   - `DateFrom` later than the target end → clamped to the target end.
   - Lag over `MaxBackfillDays` (14) → `DateFrom` = target end − 14 days, logged as Error; older
     days are **not** imported. Lag over `StaleWarningDays` (3) → Warning only.
2. **Handler** `ImportBankStatementHandler` (jobs and manual import): validator checks that the
   account name exists in `BankAccounts:Accounts`; `BankClientFactory` picks the client by the
   account's `Provider`.
3. **List statements** from the gateway (`IBankClient.GetStatementsAsync`):
   - **Comgate** (`ComgateBankClient`): for each day in the window, `POST
     https://payments.comgate.cz/v1.0/transferList?merchant=…&secret=…&date=yyyy-MM-dd`; keep only
     transfers whose `accountCounterParty` equals the account's `AccountNumber` (this is how CZK
     and EUR are separated). Statement id = Comgate `transferId`, date = `transferDate`.
   - **Shoptet Pay** (`ShoptetPayBankClient`): one `GET {ShoptetPay:BaseUrl}/v1/reports/payout?dateFrom=<DateFrom 00:00>&dateTo=<DateTo+1 day 00:00>&limit=1000`
     with `Authorization: Bearer {ShoptetPay:ApiToken}`. Statement id = report `id`, date =
     report `dateTo`. `AccountNumber` is not used.
4. **Dedup**: collapse duplicate ids within the response, then look up all ids in
   `BankStatements` by `TransferId` (globally, across accounts). Rows already `OK` are skipped
   (`SkippedCount`); rows with an error result are retried and their row updated.
5. **Per statement**, one at a time:
   - Download the ABO file — Comgate `GET …/v1.0/aboSingleTransfer?merchant=…&secret=…&transferId=…&download=true&type=v2`,
     Shoptet Pay `GET /v1/reports/payout/{id}/abo`. `ItemCount` = ABO lines minus the header line.
   - Load it into FlexiBee: `FlexiBankStatementImportService` → `Rem.FlexiBeeSDK`
     `IBankAccountClient.ImportStatement(FlexiBeeId, aboData)` (the SDK's `bankovni-ucet/{id}/nacteni-vypisu`
     call). FlexiBee then creates the bank movements in that bank account (`bankovni-ucet` id = `FlexiBeeId`).
   - Write the outcome to `public."BankStatements"`: insert a new row, or update the existing
     error row (`ImportDate` re-stamped to now).
6. **Watermark**: after all statements, update the `BankImportStates` row — no failed statement
   → `RecordSuccess(DateTo)`: `LastValidImportDate = DateTo`, status `OK`, failure count reset;
   at least one failed → `RecordFailure("N statement(s) failed")`: watermark unchanged, status
   `ERROR`, `ConsecutiveFailureCount + 1`. An exception (gateway list failure, DB error) also
   records a failure and is rethrown, so the Hangfire run fails.

## Logic & formulas
- **Result column** `BankStatements.ImportResult`: `OK` on success; FlexiBee's error message when
  FlexiBee rejects the file (`UNKNOWN_ERROR` if it gave none); `PROCESSING_ERROR: <message>` when
  download/parse/import threw. The API exposes `ErrorType` = `ImportResult` when it is not `OK`.
- **Watermark advances even when the gateway returned zero statements** — "nothing to import" is
  a success. It moves to `DateTo` of the run, i.e. yesterday (Comgate) or today (Shoptet Pay).
- **A failed statement keeps the watermark in place**, so the next runs re-read the same window
  and retry it (and re-skip the `OK` ones), until it succeeds or falls outside the 14-day cap.
- **Dates**: the jobs compute the window from server-local `DateTime.Today`; `StatementDate` is
  stored as the gateway date with UTC kind in a `timestamp without time zone` column; `ImportDate`
  is UTC now.
- **Amounts / VAT / currency**: Heblo reads no amounts. The ABO file carries the gateway's
  payout lines as-is; `Currency` on the row is only the configured account currency.
- **Comgate resilience**: retries HTTP 5xx up to 3× (exponential back-off from 1 s, jitter) and
  a circuit breaker (50 % failures over ≥ 3 calls in 1 min → open 30 s); 5xx after retries or an
  open breaker becomes `PaymentGatewayUnavailableException`. 4xx is not retried. Shoptet Pay has
  no retry policy.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `BankAccounts:Accounts[]` | `AccountCZK` (Comgate, FlexiBeeId 9999), `AccountEUR` (Comgate, 9998), `ShoptetPay-CZK` (ShoptetPay, AccountNumber "", FlexiBeeId 0) — placeholders | Per account: `Name` (must match the job's account name), `Provider` (`Comgate`/`ShoptetPay`), `AccountNumber` (Comgate counter-party filter), `FlexiBeeId` (FlexiBee bank account id), `Currency` |
| `Comgate:MerchantId`, `Comgate:Secret` | placeholders (secrets) | Comgate merchant credentials, sent in the query string |
| `ShoptetPay:ApiToken` | `CONFIGURE_IN_USER_SECRETS` (secret) | Bearer token; validated on start |
| `ShoptetPay:BaseUrl` | `https://api.shoptetpay.com` | Shoptet Pay API base |
| `BankImportWatermark:MaxBackfillDays` | 14 (class default) | Max look-back of a stale watermark; older days are dropped |
| `BankImportWatermark:StaleWarningDays` | 3 (class default) | Lag that triggers a Warning log |
| `FlexiBeeSettings:*` | placeholders (secrets) | FlexiBee server, company, login |

The real account list, account numbers and FlexiBee ids live outside the repo (Key Vault).

## Runtime facts
- A duplicate-key insert on `IX_BankStatements_TransferId` once surfaced inside the watermark
  save (`BankImportStateRepository.UpsertAsync`) instead of the statement insert, because the
  failed insert stayed tracked in the shared DbContext; fixed by detaching on `DbUpdateException`
  and deduplicating globally by `TransferId` — agent memory `gotcha_shared_dbcontext_poison`,
  fix PR #3329 — 2026-06-24.

## Known quirks
- **Repo account names do not match the jobs.** `appsettings.json` names the Comgate accounts
  `AccountCZK` / `AccountEUR`, but the jobs ask for `ComgateCZK` / `ComgateEUR`
  (`BankAccountNames`). With repo config only, both Comgate jobs fail validation
  ("Account name … not found"). Production must override the names; that is not visible in the repo.
- **Older failures are never retried automatically.** Once a failed day is more than 14 days
  behind, the window is clamped and that statement stays in `BankStatements` with an error
  result until someone imports that date manually.
- **Manual import moves the watermark.** A successful manual import sets `LastValidImportDate`
  to the chosen day — possibly *backwards*. Harmless (the next job run re-reads from there and
  skips `OK` rows), but a manual import of an old date makes the next job re-scan up to 14 days.
  A manual import with errors also increments `ConsecutiveFailureCount`.
- **FlexiBee duplicates are not guarded by Heblo.** If FlexiBee accepted a file but the
  `BankStatements` write then failed, or two runs (manual + scheduled) process the same id at the
  same time, the next run sends the same ABO again. Whether FlexiBee rejects a repeated statement
  is not determined from the repo.
- **Shoptet Pay ignores the account.** `GetStatementsAsync` returns every payout report in the
  range regardless of currency (only the header's `Account` holds the currency) and reads at most
  1000 reports without paging. A non-CZK payout would be loaded into the CZK FlexiBee account.
- **Shoptet Pay logs the full raw response body** at Information level on every run.
- **`ConsecutiveFailureCount` / `LastErrorMessage` have no UI and no alert**; a stuck watermark
  is visible only in logs (Error from the 15th day) or as a growing gap in the chart.
- **`docs/features/comgate.md` is outdated**: it describes crons `0 9 * * *` / `10 9 * * *`,
  page `/finance/comgate` and no Shoptet Pay.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportJobBase.cs` — window, bootstrap, backfill cap
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/ComgateCzkImportJob.cs` (+ `ComgateEurImportJob.cs`, `ShoptetPayImportJob.cs`) — job ids, crons, window end
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` — dedup, per-statement import, watermark update
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankImportState.cs` — watermark semantics
- `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateBankClient.cs` — Comgate endpoints, account filter, resilience
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPay/ShoptetPayBankClient.cs` — Shoptet Pay endpoints
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Bank/FlexiBankAccountClient.cs` — FlexiBee statement load
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` — dedup lookup, detach-on-failure
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportConfiguration.cs` — table, unique `TransferId` index
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — manual import and list API
