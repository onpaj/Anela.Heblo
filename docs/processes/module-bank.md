---
process: module-bank
kind: module
module: bank
summary: Loads payout statements from the Comgate and Shoptet Pay payment gateways into FlexiBee bank accounts every morning and keeps an audit log of every statement imported.
owns: []
verified_at: "5e993f9e2"
related:
  - feed-bank-statements
---

# Bank (bank statement import)

## Purpose
E-shop card and online payments are collected by payment gateways — **Comgate** (CZK and EUR)
and **Shoptet Pay** (CZK) — which pay the money out to Anela's bank account in batches. For the
accountant to match each payout to the orders it covers, the gateway's itemised statement
(bankovní výpis, ABO file) has to be in the matching bank account in FlexiBee. This module
fetches those statements daily and loads them into FlexiBee automatically, and records what was
imported so failures are visible. It does not read or compute any amounts itself.

## Users & screens
- **Bankovní import** — `/customer/bank-statements-overview` (menu *Bankovní výpisy*, permission
  `Customer_BankStatements` "Bankovní výpisy"). Tab *Statistiky*: daily chart of imported
  statements. Tab *Import*: table of all imported statements (filter by id, transfer id, account,
  statement/import date, date range, errors only; sorting) and the *Import* button that imports
  one chosen day for one chosen account.
- **Bank statement import chart** — `/finance/bank-statements` (permission `Finance_MarginAnalysis`):
  number of statements or items per day, by import date or statement date, last 30 days by default.
- Dashboard tile `bankstatementimportstatistics` (links to `/finance/bank-statements`).
- No MCP tools.

## Processes
- `feed-bank-statements` — Comgate CZK/EUR and Shoptet Pay CZK statements → FlexiBee bank
  accounts, with per-account watermark; Hangfire `daily-comgate-czk-import` (04:30),
  `daily-comgate-eur-import` (04:40), `daily-shoptetpay-czk-import` (04:50), plus the manual
  *Import* button (`POST /api/bank-statements/import`).

Plain reads (no doc needed): statement list `GET /api/bank-statements`, detail
`GET /api/bank-statements/{id}`, configured accounts `GET /api/bank-statements/accounts`
(from `BankAccounts` config), and the daily counts behind the charts
(`GET /api/analytics/bank-statement-import-statistics`, served by the Analytics module through
`IBankStatementStatisticsSource`, which Bank implements: count of `BankStatements` rows and sum
of `ItemCount` per calendar day, gaps filled with zeros).

## Data owned
- `public."BankStatements"` — one row per gateway statement (payout) Heblo tried to import:
  `TransferId` (gateway id, globally unique), `StatementDate`, `ImportDate` (UTC, last attempt),
  `Account` (config name), `Currency`, `ItemCount` (ABO transaction lines), `ImportResult`
  (`OK` or the error text).
- `public."BankImportStates"` — one row per account name: `LastValidImportDate` (watermark,
  inclusive), last run start/finish, `LastRunStatus` (`OK`/`ERROR`), `LastErrorMessage`,
  `ConsecutiveFailureCount`.

## External systems
- **Comgate** (read): `POST payments.comgate.cz/v1.0/transferList` (payouts per day) and
  `GET …/v1.0/aboSingleTransfer` (ABO file); merchant id + secret in the query string.
- **Shoptet Pay** (read): `GET api.shoptetpay.com/v1/reports/payout` (payout reports) and
  `GET /v1/reports/payout/{id}/abo`; Bearer token. Separate from the Shoptet e-shop REST API.
- **FlexiBee / ABRA Flexi** (write): statement load into bank account (`bankovni-ucet`) with the
  account's `FlexiBeeId`, via `Rem.FlexiBeeSDK` `IBankAccountClient.ImportStatement`.

## Dependencies
- Reads no other Heblo module.
- Read by **Analytics** (`AnalyticsRepository` → `IBankStatementStatisticsSource`) for the import
  statistics chart and dashboard tile.
- Uses the shared ABO parser `Anela.Heblo.Xcc/Abo/AboFile.cs` (item counting only) and the
  Background Jobs module (`IRecurringJobStatusChecker` enable/disable).

## Known quirks
- Repo `appsettings.json` names the Comgate accounts `AccountCZK`/`AccountEUR`, while the jobs
  use `ComgateCZK`/`ComgateEUR`; without the production override both Comgate jobs fail
  validation. See `feed-bank-statements`.
- A failed statement older than 14 days (`BankImportWatermark:MaxBackfillDays`) is never retried
  automatically; it needs a manual import of that day.
- Watermark health (`ConsecutiveFailureCount`, `LastErrorMessage`) has no screen and no alert.
- The two screens use different permissions: the chart page needs `Finance_MarginAnalysis`,
  the list/import page `Customer_BankStatements`; the menu item sits under Customer.
- `docs/features/comgate.md` is outdated (old crons 09:00/09:10, page `/finance/comgate`, no
  Shoptet Pay).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` — DI, options, validators, Analytics adapter
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` — the import
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportJobBase.cs` — scheduled jobs
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs` — daily counts for Analytics
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — API
- `frontend/src/pages/customer/BankStatementsOverviewPage.tsx`, `frontend/src/components/customer/tabs/ImportTab.tsx` — list and manual import UI
- `frontend/src/pages/customer/BankStatementImportPage.tsx` — chart page
