---
process: module-ecomail
kind: module
module: ecomail
summary: Collects Anela's Ecomail newsletter and e-mail automation statistics into Heblo every 6 hours, ready for marketing reporting that has not been built yet.
owns: []
verified_at: "5e993f9e2"
related: [sync-ecomail]
---

# Ecomail (newsletter & automation statistics)

## Purpose
Anela's e-mail marketing runs in **Ecomail** (account `anela`): one-off newsletters
(kampaně / newslettery) and always-on automations (automatizace) such as the abandoned-cart
e-mail ("Opuštěný košík_2025") and the first-purchase e-mail ("První nákup"). Ecomail also
attributes orders and revenue to these e-mails through the Shoptet↔Ecomail connector.

This module copies those statistics into Heblo so marketing can later answer, per month:
how many newsletters went out and to how many people, open / click / unsubscribe rates, how much
revenue Ecomail attributes to newsletters and to each automation, and how that trends over time.
It is an **ingest only**: it stores the data, nobody reads it yet.

## Users & screens
- No Heblo page, API endpoint, dashboard tile or MCP tool reads Ecomail data.
- The only visible surface is the job `ecomail-sync` ("Ecomail Sync", category Marketing) on the
  Recurring Jobs admin page, where it can be enabled/disabled, re-scheduled or run by hand.

## Processes
- `sync-ecomail` — every 6 h (Hangfire `ecomail-sync`, `0 */6 * * *` Europe/Prague): campaigns +
  their lifetime stats, automation list, one daily snapshot of each automation's lifetime counters,
  and per-month automation send/open/click/unsubscribe counts.

No user actions; the module has no CRUD screens and never writes to Ecomail.

## Data owned
All in schema `public` of the main Heblo database (migration
`20260922124454_AddEcomailIngestTables`, applied by hand):
- `EcomailCampaigns` — one row per Ecomail campaign (id = Ecomail id), including drafts, A/B arms
  (`variation`) and SMS; lifetime stats only on sent `email`/`ab` campaigns.
- `EcomailPipelines` — one row per automation (id = Ecomail id): name, list, created/updated.
- `EcomailAutomationSnapshots` — one row per automation per UTC day: what its lifetime counters
  read that day (incl. conversions and revenue). Append-only and irreplaceable; unique on
  (`PipelineId`, `CapturedOn`).
- `EcomailAutomationMonths` — one row per automation per calendar month: unique subscribers who
  were sent / opened / clicked / unsubscribed. Recomputed for the last 2 months, then locked;
  `LastError` marks failed months. Unique on (`PipelineId`, `Year`, `Month`).

## External systems
- **Ecomail REST API v2** (`https://api2.ecomailapp.cz`), read-only, API key from Key Vault
  `Ecomail--ApiKey`: `GET /campaigns`, `/campaigns/{id}/stats`, `/pipelines`,
  `/pipelines/{id}/stats`, `/pipelines/{id}/stats-detail`. Direction: Ecomail → Heblo.
- Indirectly **Shoptet**: Ecomail's conversion numbers come from its Shoptet connector addon, not
  from Heblo.

## Dependencies
- Reads no other Heblo module. Uses the shared BackgroundJobs infrastructure
  (`IRecurringJob`, `RecurringJobSeeder`, `IRecurringJobStatusChecker`).
- No module reads from it yet. Its design deliberately mirrors the MarketingPerformance module
  (sums only, `RecomputeWindowMonths`, `IsLocked`, `LastError`), but the two do not share data —
  Ecomail mailing metrics are not folded into marketing channel costs.

## Known quirks
- **Data with no reader**: four tables filled every 6 hours, no UI, endpoint, MCP tool or Metabase
  grant.
- **Automation monthly conversions exist only as snapshot deltas**, starting the day the job first
  ran; Ecomail cannot provide them retroactively, and missed days are permanent holes.
- **Automation months reach back only ~1 year** (Ecomail `stats-detail` rejects windows older than
  365 days, measured 2026-09-24); history before 2025-10 is unreachable.
- **Newsletter reporting must use `Status = 3 AND CampaignType IN ('email','ab')`** — counting
  `variation` rows double-counts A/B tests, `sms` is not e-mail.
- **Open rates are inflated** by machine prefetches (Gmail image proxy, roughly a third of opens,
  `CLUSTER-B-FINDINGS.md` §8.7, 2026-09-22).
- **An empty API key makes the job a silent no-op** (logged at Information), with the job still
  shown enabled — a missing secret shows up only as missing snapshot days.
- Details and incidents (first run green with zero campaigns, 44 dead failure rows): see
  `sync-ecomail`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Ecomail/EcomailModule.cs` — DI registration (job found by assembly scan)
- `backend/src/Adapters/Anela.Heblo.Adapters.Ecomail/EcomailAdapterServiceCollectionExtensions.cs` — options binding/validation, HTTP client
- `backend/src/Anela.Heblo.Application/Features/Ecomail/Services/EcomailSyncService.cs` — the whole sync
- `backend/src/Anela.Heblo.Domain/Features/Ecomail/` — entities, options, API client contract
- `backend/src/Anela.Heblo.Persistence/Ecomail/` — EF configurations and repository
- `CLUSTER-B-FINDINGS.md` — the investigation and measurements behind the design
