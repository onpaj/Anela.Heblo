---
process: sync-ga4-aggregates
kind: sync
module: analytics
summary: Nightly pull of aggregated Google Analytics 4 web traffic, landing pages, page views and e-commerce purchase events into Heblo_V3.ga4_agg, read by Metabase through five month-grain views.
owns:
  - backend/src/Adapters/Anela.Heblo.Adapters.GoogleAnalytics/**
  - backend/src/Anela.Heblo.Persistence.Ga4/**
verified_at: "5e993f9e2"
related: [sync-flexi-analytics, sync-shoptet-orders]
---

# GA4 aggregates → ga4_agg (Metabase reporting)

## Purpose
Feeds Anela's website reporting in Metabase (ADR-007: reports live in Metabase, not in the Heblo
UI). It answers questions like "how many visits did the e-shop get this month, compared with last
month and the same month last year?", "which landing pages bring the most visitors?", "which
articles are read most?" and "what share of visits ends in a purchase?". The backlog items are
#7, #8, #9 and #37.

Metabase reads `Heblo_V3` as role `metabase_ro`, which can see only five plain (non-materialized)
views in schema `ga4_agg`: `v_monthly_traffic`, `v_monthly_traffic_by_channel`,
`v_monthly_landing_pages`, `v_monthly_articles` and `v_monthly_conversion`. Heblo itself never reads
this data. Nothing in the Heblo UI shows it, and no MCP tool exposes it.

This is separate from Metabase's second GA4 source, the BigQuery export (dataset
`analytics_392098710`). That export starts on 2026-05-25 and never backfills. The Data API used here
reaches back to the property's creation on 2023-06-29, so year-over-year comparisons work now.

## Trigger
- Hangfire recurring job `ga4-aggregates-sync` (category Integrations), cron `10 4 * * *`, time
  zone Europe/Prague. 04:10 avoids `flexi-analytics-sync` (03:00) and leaves GA4 time to settle the
  previous day. Can be triggered by hand from the Recurring Jobs admin page.
- The job is registered only when **both** `GoogleAnalytics:PropertyId` is non-blank **and**
  `GoogleAnalytics:CredentialsJson` starts with `{` (it must look like a JSON document). Both come
  from Key Vault (`GoogleAnalytics--PropertyId`, `GoogleAnalytics--CredentialsJson`). It is also
  skipped when the environment's connection string (`ConnectionStrings:<EnvironmentName>`) is blank
  or `InMemory`. Otherwise the whole stack (job, services, `Ga4DbContext`) is absent.
- `Ga4Sync:Enabled = false` makes the job log "disabled" and return without doing anything.
- There is no separate backfill tool. The first run against an empty table **is** the backfill:
  it starts at `Ga4Sync:BackfillFrom` and walks forward.

## Data flow
Source: GA4 Data API v1beta `RunReport` on `properties/{PropertyId}` with a service-account
credential (scope `analytics.readonly`). Target: schema `ga4_agg` in the main Heblo database
(`Heblo_V3` in production, `Heblo_TST` on staging). It has its own `Ga4DbContext`, its own
`__EFMigrationsHistory` in `ga4_agg`, and its own connection pool (`Ga4Database:MaxPoolSize`,
default 5).

1. `Ga4SyncJob.ExecuteAsync` links the Hangfire token with a `Ga4Sync:RequestTimeoutSeconds`
   (1800 s) timeout covering the **whole run**, then calls `Ga4SyncService.SyncAllAsync`.
2. Six table syncs run **one after another**, in this order. They run sequentially because GA4
   caps concurrent requests per property and the database runs on a single vCore:

   | Table (= `sync_state.entity_name`) | GA4 dimensions | GA4 metrics | Grain / cap |
   |---|---|---|---|
   | `traffic_monthly` | `yearMonth` | sessions, totalUsers, newUsers, screenPageViews, engagedSessions, userEngagementDuration | month |
   | `traffic_total_daily` | `date` | same six | day |
   | `traffic_daily` | `date`, `sessionDefaultChannelGroup` | same six | day × channel |
   | `conversions_daily` | `date`, `sessionDefaultChannelGroup` | transactions, purchaseRevenue | day × channel |
   | `landing_page_daily` | `date`, `landingPage` | sessions, engagedSessions | top 100 landing pages per day by sessions |
   | `page_daily` | `date`, `pagePath`, `pageTitle` | screenPageViews, sessions | top 100 paths per day by views |

3. For each table (`Ga4EntitySyncServiceBase.SyncAsync`):
   1. Read or create its row in `ga4_agg.sync_state` and mark it `RUNNING`.
   2. Work out the window: from `watermark_date − TrailingReprocessDays` (7) to **yesterday** in
      `Ga4Sync:TimeZone`. On the first run (no watermark) it starts at `BackfillFrom`.
      `traffic_monthly` widens the start to the 1st of that month and asks for the whole window in a
      single request.
   3. Walk the window in `ChunkDays` (31-day) chunks. Each chunk is one report, paged by `PageSize`
      (100 000 rows), ordered by the first dimension, with a 250 ms pause before every request.
   4. **Replace** the chunk's date range in the table. Incoming rows are upserted by primary key,
      and stored rows in the range that GA4 no longer returns are deleted (a page that dropped out of
      the top 100, for example). Rows are saved every `BatchSize` (500).
   5. Move `watermark_date` to the chunk's end and save it **after every chunk**, so an interrupted
      backfill resumes at the chunk it died on.
   6. Write `OK` or `FAILED` (with `last_error_message`, up to 2000 characters) and the row counts to
      `sync_state`.
4. A table that fails does not stop the others. A cancelled run (timeout or shutdown) stops before
   the next table.
5. No refresh step follows. The views are plain views, so Metabase sees the new rows straight away.

Run health lives in `ga4_agg.sync_state`: one row per table with `watermark_date`, `last_run_*`
(started, finished, status `RUNNING|OK|FAILED`, rows fetched/upserted), `top_n_per_day` and
`last_error_message`. **It is not granted to Metabase.**

## Logic & formulas
**Values.** GA4 returns every metric as a string. Values are parsed as decimals with the invariant
culture and truncated to `long` for counts. An unparseable metric becomes **0**. An unparseable
`date` or `yearMonth` throws and fails the chunk. Empty dimension values are stored as `(not set)`.
`(not set)` and `(other)` values are kept verbatim, because hiding them would change the totals.
`purchase_revenue` is `numeric(18,4)` in the property's currency, as GA4 reports it (it is not
converted here).

**Primary keys.** `traffic_monthly(month)` (stored as the 1st of the month),
`traffic_total_daily(date)`, `traffic_daily(date, channel_group)`,
`conversions_daily(date, channel_group)`, `landing_page_daily(date, landing_page)` and
`page_daily(date, page_path)`.

**Top-N caps.** `landing_page_daily` keeps the top `TopLandingPagesPerDay` (100) per day, sorted
by sessions descending and then by landing page. `page_daily` first collapses rows sharing a path
(the same path can carry several titles). It sums views and sessions, keeps the busiest title, and
then keeps the top `TopPagesPerDay` (100) per day by views. When `Ga4Sync:PagePathPrefixes` is set,
GA4 filters paths case-insensitively with an OR of `BEGINS_WITH` conditions. The repo default is
empty, meaning every path is kept.

**Why three traffic tables.** GA4 figures do not add up across breakdowns. It de-duplicates users
over whatever period and breakdown it is asked about. Measured on property 392098710 for June to
August 2026:
- sessions summed from daily rows, or across channels, come out 0.0–3.8% above GA4's own total;
- users summed across days come out **~34% above** it (August 2026: 29 194 summed, against GA4's
  21 746).

So monthly headlines come from `traffic_monthly` (asked at month grain), daily totals come from
`traffic_total_daily`, and `traffic_daily` is only for the channel *shape*.

**Read views** (`Sql/ga4_agg_views.sql`):

| View | Backlog | Source | Notes |
|---|---|---|---|
| `v_monthly_traffic` | #9 | `traffic_monthly` (+ `traffic_total_daily` for day coverage) | MoM and YoY % on sessions and users. `bounce_rate = 1 − engaged/sessions`. `is_partial_month` is true when the days with data ≠ the days in the month (the current month and June 2023). |
| `v_monthly_traffic_by_channel` | #20 | `traffic_daily` | Channel shape and % of the month's sessions. `channel_users` must never be summed across channels. |
| `v_monthly_landing_pages` | #8 | `landing_page_daily`, excluding `(other)` | Rank per month, previous-month and year-ago rank. Sessions are not a site total, because of the cap. |
| `v_monthly_articles` | #37 | `page_daily`, excluding `(other)` | Rank by views. Filter to the article path prefix in Metabase. |
| `v_monthly_conversion` | #7 | sessions from `traffic_monthly`; transactions/revenue from `conversions_daily` | `conversion_rate_pct = transactions / sessions × 100` (3 decimal places). `avg_order_value = revenue / transactions`. A month without conversions rows stays NULL (shown as a gap), not 0. |

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `GoogleAnalytics:PropertyId` | `""` (Key Vault `GoogleAnalytics--PropertyId`) | GA4 property id. Blank = stack unregistered. |
| `GoogleAnalytics:CredentialsJson` | `""` (Key Vault `GoogleAnalytics--CredentialsJson`) | Service-account key JSON. It must start with `{`, otherwise the stack is unregistered. It is parsed lazily, on the first report. |
| `Ga4Database:MaxPoolSize` | 5 | Size of the dedicated Npgsql pool. |
| `Ga4Sync:Enabled` | true | false = the job runs but returns immediately. |
| `Ga4Sync:CronExpression` | `10 4 * * *` | Seed cron only (see quirks). |
| `Ga4Sync:TimeZone` | `Europe/Prague` | Job time zone and the "yesterday" boundary. An unknown id falls back to UTC. |
| `Ga4Sync:BackfillFrom` | `2023-06-29` | First date for an empty table (the property's creation date). |
| `Ga4Sync:TrailingReprocessDays` | 7 | Days re-pulled before the watermark on each run (GA4 revises about 48 h). |
| `Ga4Sync:ChunkDays` | 31 | Days per report request (values ≤ 0 are clamped to 1). |
| `Ga4Sync:PageSize` | 100000 | Rows per Data API page (API maximum 250 000). |
| `Ga4Sync:TopLandingPagesPerDay` | 100 | Landing-page cap per day. |
| `Ga4Sync:TopPagesPerDay` | 100 | Page cap per day. |
| `Ga4Sync:PagePathPrefixes` | `[]` | Optional ingest-time path filter for `page_daily`. |
| `Ga4Sync:BatchSize` | 500 | Rows per `SaveChanges`. |
| `Ga4Sync:ThrottleMilliseconds` | 250 | Pause before every Data API request. |
| `Ga4Sync:RequestTimeoutSeconds` | 1800 | Timeout for the whole job run. |

## Runtime facts
- The standard GA4 aggregates reach back to property creation, 2023-06-29, even though the
  property's `eventDataRetention` is `FOURTEEN_MONTHS`. Retention limits only event-level and
  Explorations data. A `yearMonth` report from 2018 to 2026 returned 40 months starting 2023-06 —
  agent memory `gotcha_ga4_retention_does_not_limit_data_api` — 2026-09-22.
- Measured roll-up error for June–August 2026: sessions +0.0–3.8%, users summed across days +34%.
  The Data API's e-commerce figures reconcile exactly with the BigQuery export. GA4 sessions run
  57–65% above a naive distinct-id count from BigQuery, because 26–28% of exported events carry no
  ids (consent denied) — agent memory `gotcha_ga4_figures_do_not_decompose` and
  `ga4_agg_views.sql` comments — 2026-09-22.
- A 39-month backfill hit GA4's cardinality limit exactly once: one `page_daily` row with path
  `(other)`, 2024-02-18, 582 views. The views exclude it — `ga4_agg_views.sql` and
  `Ga4EntitySyncServiceBase` comments — undated, written with the code (Sept 2026).
- Whether the GA4 Key Vault secrets are set in production, and therefore whether the job is live
  there, cannot be determined from the repo. The staging and default appsettings ship both values
  blank.

## Known quirks
- **A failed table does not fail the Hangfire job.** `Ga4SyncJob` only logs a warning when
  `FailedServices > 0`, so Hangfire records Succeeded. A timed-out run behaves the same way: the
  table in flight is marked `FAILED`, the rest are skipped, and the job returns normally.
  `ga4_agg.sync_state` is the only reliable health signal, and it is not visible in Metabase. Note
  that `flexi-analytics-sync` was changed to throw in this situation; this job was not.
- **GA4 returning nothing for a range that already holds rows fails the chunk** on purpose
  (`Ga4ChunkUpsert`), because GA4 never retracts history. An empty answer means the wrong property,
  an outage, or an over-narrow path filter. The watermark does not move, and the table reports
  `FAILED` every night until that is fixed.
- **The admin enable toggle does nothing.** `RecurringJobDiscoveryService` reads only the cron from
  `RecurringJobConfigurations`, and this job never consults `IRecurringJobStatusChecker`. The only
  working off switch is `Ga4Sync:Enabled` (agent memory
  `gotcha_adapter_recurring_job_needs_concrete_registration`, 2026-09-22).
- **Admin cron wins after the first seed.** `RecurringJobSeeder` keeps the stored cron, so changing
  `Ga4Sync:CronExpression` later does not move a job that is already seeded.
- **The credential is built lazily.** A malformed `CredentialsJson` that still starts with `{`
  fails only this job, on its first run, rather than application startup. That design came out of
  the incident shape described in agent memory `gotcha_kv_placeholder_defeats_isconfigured_gate`.
- **Channel and landing-page figures never add up to the site total.** Channel rows overshoot by
  up to 3.8%, and the landing-page and page tables are top-100-per-day captures, so their tails are
  incomplete. Totals belong in `v_monthly_traffic`.
- **`v_monthly_conversion` is not the company conversion rate.** GA4 `purchase` events include
  cancelled and unpaid orders, can fire twice when a customer reloads the thank-you page, and miss
  phone/e-mail orders and shoppers who declined tracking consent. Use it as a website trend only.
  Real order counts are in `shoptet_raw` (see `sync-shoptet-orders`).
- **Data-quality warnings go only to the log.** When GA4 reports `DataLossFromOtherRow` or
  `SubjectToThresholding`, `Ga4ReportClient` logs a warning. Nothing is stored about it.
- **Each table has its own watermark.** One table can lag behind another, so a month can be
  complete in `traffic_monthly` but partial in `conversions_daily`. `is_partial_month` counts days
  from `traffic_total_daily` only.
- **The "(other)" date guard has never fired.** A row whose `date` itself comes back as `(other)`
  is dropped with a warning. That has not been observed.
- **Migrations and views are manual:**
  `dotnet ef database update --context Ga4DbContext`, then
  `psql -v ON_ERROR_STOP=1 -f backend/src/Anela.Heblo.Persistence.Ga4/Sql/ga4_agg_views.sql`. The
  SQL drops and recreates the five views and re-applies the grants. It skips the grants if the role
  `metabase_ro` does not exist.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAnalytics/GoogleAnalyticsAdapterServiceCollectionExtensions.cs` — the configuration gate and table order
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAnalytics/Sync/Ga4SyncJob.cs` — job id, cron, timeout, and failure handling (does not throw)
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAnalytics/Sync/Ga4SyncService.cs` — sequential table loop, stop on cancellation
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAnalytics/Sync/Ga4EntitySyncServiceBase.cs` — window, chunks, per-chunk watermark, `sync_state` bookkeeping
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAnalytics/Sync/Ga4ChunkUpsert.cs` — replace-range upsert and the empty-response guard
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAnalytics/Sync/*SyncService.cs` — per-table dimensions, metrics and top-N
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAnalytics/Ga4ReportClient.cs` — Data API paging, ordering, throttle, prefix filter
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAnalytics/Ga4SyncOptions.cs`, `Ga4Options.cs` — defaults and the `IsConfigured` check
- `backend/src/Anela.Heblo.Persistence.Ga4/Ga4DbContext.cs` — tables, keys, columns
- `backend/src/Anela.Heblo.Persistence.Ga4/Sql/ga4_agg_views.sql` — the five views, the conversion caveat, grants
- `docs/architecture/metabase.md` — the grant model and why reporting lives in `Heblo_V3`
