---
process: module-background-jobs
kind: module
module: background-jobs
summary: Runs every scheduled task in Heblo (imports, syncs, checks, printing, cleanups) on Hangfire, and lets administrators see, switch off, reschedule and trigger them from the Recurring Jobs page.
owns: []
verified_at: "5e993f9e2"
related: [job-recurring-job-registration, sync-flexi-analytics]
---

# Background jobs (Hangfire)

## Purpose
Anything Heblo does "on its own" at a set time — the 04:00 invoice imports, bank statement
imports, nightly data-quality tests (DQT), picking-list printing, tracking-number filling,
Shoptet order completion, Ecomail/GA4/Flexi syncs, AI ingestion — is a **recurring job** run by
Hangfire. This module is the plumbing for all of them, not any one business process:

- it finds every job in code, stores its schedule and on/off switch in the database, and
  registers it with Hangfire at startup;
- it gives administrators the **Recurring Jobs** page (Administrace → Recurring Jobs) to switch a
  job off, change when it runs, or run it now;
- it reports failed jobs (dashboard tile, Application Insights event, Hangfire dashboard).

What each job actually does is documented by the module that owns the job (see the table below).
Short, frequent cache reloads (catalog stock, prices, costs) do **not** run here — they are
BackgroundRefresh tasks, see `module-background-refresh`.

## Users & screens
- **Recurring Jobs** — route `/recurring-jobs`, sidebar *Administrace → Recurring Jobs*. Lists every
  row of `RecurringJobConfigurations` grouped by category (Finance, Marketing, Catalog, Warehouse,
  DataQuality, Content, Attendance, Integrations, Uncategorized) with search, the cron, its time
  zone, computed next run, last change (who/when). Actions:
  - enable/disable toggle — needs permission `Jobs_Disable` ("Vypínání úloh");
  - **Run now** (with confirmation dialog) — needs `Jobs_Trigger` ("Spouštění úloh");
  - edit cron — needs `Admin_Administration` write;
  - viewing the list needs `Admin_Administration` read.
- **Hangfire dashboard** — `/hangfire` (served by the API). Any signed-in user with the base
  role `heblo_user` (`AccessRoles.Base`); `HangfireAuthenticationMiddleware` redirects anonymous users to login. Shows
  queued/processing/succeeded/failed runs, retries, and the registered recurring jobs.
- **Dashboard tile "Failed background jobs"** (tile id `failedjobs`, category System, not auto-shown)
  — count of jobs in Hangfire's Failed list; click-through opens `/hangfire/jobs/failed`.
- No MCP tool exposes jobs.

## Processes
- `job-recurring-job-registration` — how a job gets its database row, its Hangfire schedule, its
  on/off switch, a cron override and a manual run; how failures are reported. Runs at every
  application start and on each admin action.
- The jobs themselves belong to their business modules. The full inventory as of `5e993f9e2`
  (cron in Europe/Prague; "Toggle" = whether the job obeys the admin enable switch, see quirks):

| Job id | Cron (repo default) | Category | Owner (code area) | Default on | Toggle | Process doc |
|---|---|---|---|---|---|---|
| `product-weight-recalculation` | `0 2 * * *` | Catalog | Catalog | yes | yes | — |
| `product-export-download` | `0 2 * * *` | Catalog | Catalog | yes | yes | — |
| `purchase-price-recalculation` | `0 2 * * *` | Catalog | Purchase | yes | yes | — |
| `daily-consumption-calculation` | `0 6 * * *` | Catalog | PackingMaterials | yes | yes | — |
| `daily-invoice-import-eur` | `0 4 * * *` | Finance | Invoices | yes | yes | — |
| `daily-invoice-import-czk` | `15 4 * * *` | Finance | Invoices | yes | yes | — |
| `daily-comgate-czk-import` | `30 4 * * *` | Finance | Bank | yes | yes | — |
| `daily-comgate-eur-import` | `40 4 * * *` | Finance | Bank | yes | yes | — |
| `daily-shoptetpay-czk-import` | `50 4 * * *` | Finance | Bank | yes | yes | — |
| `invoice-classification` | `0 * * * *` | Finance | InvoiceClassification | yes | yes | — |
| `meta-ads-invoice-import` | `0 6,18 * * *` | Finance | Adapters.MetaAds | yes | yes | — |
| `google-ads-invoice-import` | `15 6,18 * * *` | Finance | Adapters.GoogleAds | yes | yes | — |
| `marketing-calendar-sync` | `0 * * * *` | Marketing | Marketing | yes | yes | — |
| `marketing-performance-refresh` | `MarketingPerformance:CronExpression` = `0 5 * * *` | Marketing | MarketingPerformance | yes | yes | — |
| `ecomail-sync` | `Ecomail:CronExpression` = `0 */6 * * *` | Marketing | Ecomail | yes | yes | — |
| `print-picking-list` | `0 3,8 * * *` | Warehouse | ExpeditionList | yes | yes | — |
| `fill-tracking-numbers` | `*/10 * * * *` | Warehouse | Packaging | yes | yes | — |
| `complete-delivered-orders` | `0 * * * *` | Warehouse | ShoptetOrders | yes | yes | — |
| `daily-invoice-dqt` | `0 5 * * *` | DataQuality | DataQuality | yes | yes | — |
| `daily-product-pairing-dqt` | `0 6 * * *` | DataQuality | DataQuality | yes | yes | — |
| `daily-stock-writeback-dqt` | `0 7 * * *` | DataQuality | DataQuality | yes | yes | — |
| `daily-lot-stock-dqt` | `0 8 * * *` | DataQuality | DataQuality | yes | yes | — |
| `daily-price-comparison-dqt` | `0 9 * * *` | DataQuality | DataQuality | yes | yes | — |
| `knowledge-base-ingestion` | `*/15 * * * *` | Content | KnowledgeBase | yes | yes | — |
| `leaflet-ingestion` | `Leaflet:IngestionCronExpression` = `*/15 * * * *` | Content | Leaflet | yes | yes | — |
| `photobank-index` | `0 3 * * *` | Content | Photobank | yes | yes | — |
| `photobank-auto-tag` | `0 4 * * *` | Content | Photobank | **no** | yes | — |
| `logeto-break-insertion` | `0 3 * * *` | Attendance | Attendance | **no** | yes | — |
| `logeto-absence-hours` | `0 4 * * *` | Attendance | Attendance | **no** | yes | — |
| `plaud-polling` | `*/5 * * * *` | Integrations | MeetingTasks | **no** | yes | — |
| `smartsupp-webhook-audit-cleanup` | `30 3 * * *` | Integrations | Smartsupp | yes | **no** | — |
| `shoptet-orders-sync` | `ShoptetOrdersSync:CronExpression` = `30 1 * * *` | Integrations | Adapters.ShoptetApi | `ShoptetOrdersSync:Enabled` (true) | **no** (config flag) | — |
| `ga4-aggregates-sync` | `Ga4Sync:CronExpression` = `10 4 * * *` | Integrations | Adapters.GoogleAnalytics | `Ga4Sync:Enabled` (true) | **no** (config flag) | — |
| `flexi-analytics-sync` | `FlexiAnalyticsSync:CronExpression` = `0 3 * * *` | Integrations | Adapters.Flexi | `FlexiAnalyticsSync:Enabled` (true) | **no** (config flag) | `sync-flexi-analytics` |

  34 jobs. The last three exist only when their adapter is configured: `shoptet-orders-sync`
  needs `ShoptetOrdersSync:ConnectionString`; `ga4-aggregates-sync` needs `GoogleAnalytics`
  configured and a relational connection string; `flexi-analytics-sync` needs
  `AnalyticsDatabase:ConnectionString`. The "Process doc" column lists only docs that exist at the
  time of writing; the other jobs are being documented by their own modules.
- Plain CRUD with no process doc: none — every admin action is covered by
  `job-recurring-job-registration`.

## Data owned
- `public."RecurringJobConfigurations"` (Heblo DB) — one row per job id (`JobName` = PK):
  `DisplayName`, `Description`, `CronExpression`, `TimeZoneId`, `IsEnabled`, `LastModifiedAt`,
  `LastModifiedBy`. Display name/description/time zone are rewritten from code on every start;
  cron and enabled flag belong to the administrator. Category is **not** stored — it is joined
  from code at read time.
- Schema **`hangfire_heblo`** (same database, `Hangfire:SchemaName`) — Hangfire's own storage:
  jobs, states, queues, recurring-job definitions, counters. Created by `HangfireSchemaInitializer`
  if missing; tables created by Hangfire (`PrepareSchemaIfNecessary = true`).

## External systems
- None directly. Hangfire storage is PostgreSQL in the Heblo database (in-memory storage when
  `Hangfire:UseInMemoryStorage` = true, i.e. the `Test` environment).
- Application Insights: every job that ends in the Failed state emits custom event
  `HangfireJobFailed` plus a tracked exception; each job run is also an Activity
  `Hangfire.Job.<TypeName>` (source `Anela.Heblo.Hangfire`) for trace correlation.

## Dependencies
- Reads from: Users (`ICurrentUserService` for "last modified by"), Authorization (permissions),
  Dashboard (tile registry), Telemetry.
- Read by: every module that defines an `IRecurringJob` and checks `IRecurringJobStatusChecker`;
  features that enqueue one-off Hangfire jobs and so share the same server and single worker:
  Invoices (async invoice import + running-jobs view via `IBackgroundWorker`), Photobank (re-tag),
  MarketingPerformance (recompute), MindMaps (attach meeting / regenerate), Article (generate).

## Known quirks
- **Disabling a job does not unschedule it.** Hangfire keeps firing a disabled job on its cron; the
  job itself must check `IRecurringJobStatusChecker` and return. Disabled runs therefore appear as
  *Succeeded* in the Hangfire dashboard although they did nothing. Four jobs never check the switch
  (`smartsupp-webhook-audit-cleanup`, `shoptet-orders-sync`, `ga4-aggregates-sync`,
  `flexi-analytics-sync`): the toggle on the page is a no-op for them; only their config flag
  (or nothing, for the Smartsupp cleanup) stops them. Agent memory, 2026-09-22: in production
  `meta-ads-invoice-import` and `google-ads-invoice-import` were `IsEnabled = false` yet showed
  Succeeded runs (they skip internally).
- **Only production schedules jobs.** `Hangfire:SchedulerEnabled` defaults to false and is true only
  in `appsettings.Production.json`. Staging (`heblo-test`), Development and Conductor worktrees
  register no recurring jobs, but they still **seed** `RecurringJobConfigurations` rows and still
  **run** any enqueued one-off job — a populated table on staging is not evidence that anything is
  scheduled (agent memory, 2026-09-22).
- **One worker for everything.** `Hangfire:WorkerCount` defaults to 1 and no environment raises it.
  A slow job blocks every other job, including a user-clicked print: on 2026-09-29 12:05–13:50
  `plaud-polling` took 300–440 s per run and manual picking-list prints waited 12–15 min (agent
  memory, 2026-09-29). Hangfire also polls the queue only every 15 s (`QueuePollInterval`).
- **Use schema `hangfire_heblo`, not `hangfire`.** A legacy `hangfire` schema with jobs up to 2025-11
  exists in `Heblo_TST`; querying it answers "did the job run?" wrongly (agent memory, 2026-09-22).
- **Admin cron wins forever.** After the first seed, changing a job's cron in code or appsettings
  does not move an already-seeded job; the stored `CronExpression` is used (see
  `job-recurring-job-registration`).
- **Removed jobs linger.** Nothing deletes a `RecurringJobConfigurations` row or removes the Hangfire
  recurring definition when a job class is deleted: the row shows under *Uncategorized* and Hangfire
  keeps firing a definition whose type no longer loads.
- **Comment drift:** `PrintPickingListJob`'s comment says "4:00 and 9:00 Prague time" but the cron
  `0 3,8 * * *` with time zone Europe/Prague fires at 03:00 and 08:00 Prague time.
- **`docs/features/recurring-jobs-management.md` is outdated** — it names `IRecurringJobTriggerService`
  / `RecurringJobTriggerService` (do not exist) and says status lives in Hangfire storage (it lives
  in `RecurringJobConfigurations`). Trust this doc and the code.
- **Default Hangfire retries.** A job without `[AutomaticRetry]` gets Hangfire's default of 10
  automatic retries; long syncs therefore declare `Attempts = 0` (agent memory, 2026-09-23).

## Code entry points
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJob.cs` — the contract every job implements
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobMetadata.cs` — job id, cron, category, default-enabled, time zone
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs` — DB entity, cron shape check
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs` — DI: repository, seeder, status checker, tile
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — `AddHangfireServices`, `AddRecurringJobs`, `SeedRecurringJobConfigurationsAsync`
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/` — discovery, registration helper, enqueuer, cron scheduler, filters, dashboard auth
- `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` — REST endpoints and permissions
- `backend/src/Anela.Heblo.Xcc/HangfireOptions.cs` — `Hangfire:*` settings and defaults
- `frontend/src/pages/RecurringJobsPage.tsx` — the admin page
