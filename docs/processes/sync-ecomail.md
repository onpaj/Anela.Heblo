---
process: sync-ecomail
kind: sync
module: ecomail
summary: Every 6 hours pulls Ecomail newsletter (campaign) statistics, the automation list, a daily snapshot of each automation's lifetime counters and per-month automation event counts into four Heblo tables — nothing in Heblo reads them yet.
owns:
  - backend/src/Anela.Heblo.Application/Features/Ecomail/**
  - backend/src/Anela.Heblo.Domain/Features/Ecomail/**
  - backend/src/Adapters/Anela.Heblo.Adapters.Ecomail/**
  - backend/src/Anela.Heblo.Persistence/Ecomail/**
verified_at: "5e993f9e2"
related: []
---

# Ecomail newsletters & automations → Heblo

## Purpose
Anela sends its newsletters (newslettery / kampaně) and runs its e-mail automations (automatizace,
e.g. abandoned cart "Opuštěný košík_2025" and first purchase "První nákup") in **Ecomail**
(account `anela`). Marketing wants to see, month by month: how many newsletters went out, to how
many people, open / click / unsubscribe rates, and how much revenue Ecomail attributes to them;
and the same for each automation. This sync stores those numbers in Heblo's database so they can be
reported later.

**Nothing reads the data yet.** There is no Heblo page, API endpoint, MCP tool or Metabase grant
for these tables. The ingest exists first because one series — automation conversions per month —
can only be built from snapshots Heblo takes itself, and a day not captured is lost forever
(see *Logic*). Answering a question about newsletter performance today means querying the tables
directly.

## Trigger
- Hangfire recurring job `ecomail-sync` (display name "Ecomail Sync", category **Marketing** on the
  Recurring Jobs admin page), cron `0 */6 * * *` in Europe/Prague → runs at 00:00, 06:00, 12:00,
  18:00 Prague time. Can be triggered by hand from the Recurring Jobs page.
- `[DisableConcurrentExecution(1800)]` (a second run waits up to 30 min for the lock) and
  `[AutomaticRetry(Attempts = 1)]` (one Hangfire retry after a failed run).
- The job is always registered and seeded enabled (`DefaultIsEnabled = true`). It exits as a
  no-op (logged "has no API key configured") when `Ecomail:ApiKey` is empty, and when the job is
  disabled on the Recurring Jobs page.

## Data flow
Source: Ecomail REST API v2, `https://api2.ecomailapp.cz`, header `key: <ApiKey>`. Read-only —
nothing is sent, triggered or deleted in Ecomail. Target: four tables in schema `public` of the
main Heblo database (prod `Heblo_V3`), one `ApplicationDbContext`.

`EcomailSyncService.SyncAllAsync` runs four stages in order:

1. **Campaigns** → `EcomailCampaigns`
   - `GET /campaigns?per_page=50&page=N`, paged until a page shorter than 50 (cap 200 pages).
   - Every campaign is upserted by Ecomail's id (title, subject, from e-mail, `campaign_type`,
     status, `sent_at`, `parent_id`, recipients, `SyncedAt` = now UTC).
   - For **reportable** campaigns only (status `3` = sent **and** type `email` or `ab`):
     `GET /campaigns/{id}/stats` → lifetime counters written onto the same row (`Inject`,
     `Delivery`, `Open`, `TotalOpen`, `Click`, `TotalClick`, `Unsub`, `Bounce`, `Spam`,
     `Conversions`, `ConversionsValue`). Re-fetched on every run; there is no lock.
2. **Automations (pipelines)** → `EcomailPipelines`
   - `GET /pipelines` → upsert by id (name, list id, created/updated, `SyncedAt`).
   - If the listing fails, or returns nothing while the table already knows pipelines, the run
     continues with the pipeline ids already in `EcomailPipelines` and records an error.
3. **Daily snapshot** → `EcomailAutomationSnapshots` (append-only)
   - For each pipeline, unless a row for (pipeline, today's **UTC** date) already exists:
     `GET /pipelines/{id}/stats` → one row of lifetime counters (`Triggered`, `Ended`, `Send`,
     `Open`, `Click`, `Unsub`, `Bounce`, `Conversions`, `ConversionsValue`).
   - **`SaveChanges` #1** — commits campaigns, pipelines and snapshots before the long month stage.
4. **Automation months** → `EcomailAutomationMonths`
   - For each pipeline and each calendar month from the start month (see *Logic*) to the current
     month: four calls
     `GET /pipelines/{id}/stats-detail?event={send|open|click|unsub}&from_date={1st}&to_date={last day}&per_page=1`,
     reading only `total`.
   - **`SaveChanges` #2.**

Then `EcomailSyncJob` logs the counts and fails the Hangfire run if the data looks empty
(see *Logic → Failure rules*). Per-item errors are collected, logged as warnings and do not stop
the run.

## Logic & formulas
**What each number means**
- Campaign counters are Ecomail's **lifetime** totals for that send. A one-off newsletter's opens,
  clicks and conversions all arrive within days of sending, so lifetime ≈ "the number for that
  newsletter"; bucket campaigns into months by `SentAt`.
- `Open` / `Click` are unique recipients; `TotalOpen` / `TotalClick` count every open/click.
- `Conversions` / `ConversionsValue` are Ecomail's own attribution (via the Shoptet↔Ecomail
  connector), not Heblo/Shoptet orders. Currency is whatever Ecomail reports (CZK on this
  account); VAT treatment is not stated by the API. Stored `numeric(18,2)`.
- Only counts are stored. Rates (open rate = `Open / Delivery`, etc.) are meant to be derived at
  read time.
- **Selection rule for reporting newsletters**: `Status = 3 AND CampaignType IN ('email','ab')`
  (`EcomailCampaign.IsReportable`, not a DB column). `variation` rows are the A/B test arms —
  samples already contained in their `ab` parent (`ParentId` points to it) — so counting them
  double-counts; `sms` is not e-mail; status ≠ 3 are drafts. All campaigns are still stored.

**Automation months** (`EcomailAutomationMonths`): unique subscribers with that event inside the
calendar month, from `stats-detail` — the only Ecomail endpoint whose date filter works.
- Start month = later of `Ecomail:BackfillFrom` (normalised to the 1st) and the **first calendar
  month starting on or after today − 365 days**. Ecomail rejects older windows with `422`, so
  automation months reach back roughly one year and never further.
- Recompute window: months with `monthsBack < RecomputeWindowMonths` (default 2 = current +
  previous month) are re-counted every run. An existing successful row outside the window is
  set `IsLocked = true` on the next run without being re-counted — its final value is the last
  run while it was still in the window.
- A `null` total (Ecomail's answer for an unsupported event) or any exception fails the month:
  a row is kept with `LastError` set, zero counts, `IsLocked = false` and `ComputedAt` not set
  (0001-01-01), and it is retried every run regardless of age. **`LastError IS NOT NULL` is the
  only way to tell a failed month from a month with genuinely zero events.**
- There is no conversions column: `stats-detail` has no conversion event.

**Automation conversions per month** must be computed as the difference between two
`EcomailAutomationSnapshots` rows (e.g. last snapshot of the month minus last snapshot of the
previous month). That is why the snapshot table exists. It starts empty on the first day the job
ran and has a hole for every day the job did not write a row; such a day can never be backfilled.
The snapshot is taken by the first run of each UTC day, which is the 06:00 Prague run.

**Failure rules** (`EcomailSyncJob`):
- Throws (Hangfire Failed) when `CampaignStatsFetched`, `SnapshotsWritten` and
  `AutomationMonthsComputed` are all 0.
- Throws when no campaigns were listed but pipelines were — the campaigns stage failed.
- The error message carries the first 10 collected errors plus "(+N more)".
- `CampaignsUpserted` / `PipelinesUpserted` count listed rows, not landed stats — never use them
  as a health signal.

**HTTP behaviour** (`EcomailApiClient`):
- Polly retry, 3 attempts, exponential from 2 s, on `429` (waits Ecomail's `Retry-After`,
  default 5 s), transport faults, `408`, `5xx` and client timeouts. Other `4xx` are not retried.
- `404` on a single campaign/pipeline (deleted) → skipped silently, the row keeps its old values.
  `404` on the `/campaigns` or `/pipelines` **listing** → error (endpoint moved / scope lost).
  `403` always fails.
- Timestamps arrive as `yyyy-MM-dd HH:mm:ss` and are stored as-is in `timestamp without time
  zone` columns (Kind Unspecified); `SyncedAt` / `ComputedAt` are UTC.
- A full run is roughly 400 calls (~2 min); Ecomail allows 1000 calls/min per key.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Ecomail:ApiKey` | `""` | Ecomail API key; Key Vault secret `Ecomail--ApiKey`. Empty = job is a no-op |
| `Ecomail:BaseUrl` | `https://api2.ecomailapp.cz` | Must be an absolute URL (validated on start — startup fails otherwise) |
| `Ecomail:HttpTimeoutSeconds` | 60 | Per-request timeout, 1–600 |
| `Ecomail:CronExpression` | `0 */6 * * *` | Seed cron (admin value wins after the first seed) |
| `Ecomail:TimeZone` | `Europe/Prague` | Cron time zone |
| `Ecomail:RecomputeWindowMonths` | 2 | Automation months re-counted each run (current + previous); ≥ 1 |
| `Ecomail:BackfillFrom` | `2024-11-01` | Earliest automation month wanted; clamped up to the 365-day floor; must be ≥ 2020-01-01 and not in the future |

## Runtime facts
- Account `anela`: 4 automations, 2 with real traffic — `Opuštěný košík_2025` (id 14720) and
  `První nákup` (id 31762); the others are `Opuštěný košík_vánoce koncept` (all zeros) and `test`
  — `CLUSTER-B-FINDINGS.md` §8.1 — 2026-09-22.
- Lifetime automation conversions: abandoned cart 150 conversions / 314 708,90 Kč, first purchase
  25 / 28 453,00 Kč — `CLUSTER-B-FINDINGS.md` §8.2 — 2026-09-22.
- 250 campaigns: 141 `email`, 28 `ab`, 60 `variation`, 21 `sms`; 129 reportable newsletters
  2024-11-29 → 2026-09-13; a full A/B send reaches ~12 700 recipients (each test arm ~1 270) —
  `CLUSTER-B-FINDINGS.md` §8.6 — 2026-09-22.
- Send volume dropped from 6–11 newsletters a month to 1–2 since June 2026 —
  `CLUSTER-B-FINDINGS.md` §8.6 — 2026-09-22.
- On 276 campaigns, `recipients` is null on 10 and `subject` null on 25 (drafts and SMS) —
  `IEcomailApiClient.cs` comment / commit c0609bb7b — 2026-09-24.
- `stats-detail` floor measured: `422 … must be a date after or equal to 2025-09-24`; first
  production run got 2025-10 … 2026-09 for all four pipelines and failed every older month —
  `CLUSTER-B-FINDINGS.md` §8.9 — 2026-09-24.
- The Ecomail API key returns `403` on `GET /transactions` — `CLUSTER-B-FINDINGS.md` §8.5 — 2026-09-22.
- Roughly a third of opens are machine prefetches (`GmailImageProxy`, Mountain View) — open rate
  overstates human readers — `CLUSTER-B-FINDINGS.md` §8.7 — 2026-09-22.

## Known quirks
- **No consumer.** Four tables are filled every 6 hours and nothing in Heblo, MCP or Metabase reads
  them (no `metabase_ro` grant either). Reporting (#4 trend chart, #5 automations, #6 newsletters
  in `CLUSTER-B-FINDINGS.md`) is still to be built.
- **Automation history starts 2025-10 and can never go further back** — `stats-detail` answers only
  the last 365 days, and the window rolls; a month not captured before it falls off is lost.
  Before #4300 the job retried the doomed months on every run.
- **44 failure placeholder rows from the first production run** (2024-11 … 2025-09 × 4 pipelines,
  2026-09-24) are now below the loop's start month, so the job never revisits them; they stay with
  `LastError` set and zero counts unless deleted by hand. Readers must filter `LastError IS NULL`.
  (Count from commit c0609bb7b; whether they were deleted is not visible in the repo.)
- **The first production run went green with zero campaigns** (2026-09-24): a non-nullable
  `recipients` made JSON parsing of the whole page throw, the campaigns stage was caught and
  skipped, and snapshots + months kept the run green. Fixed in #4300 (nullable DTO fields +
  "pipelines but no campaigns" guard).
- **Automation conversions MoM start on the day snapshots began** and have a hole for any day the
  job did not run (e.g. API key missing, app down all day). There is no backfill path.
- **Snapshots are not fully isolated from campaign errors.** Campaign rows are tracked on the same
  `DbContext` before `SaveChanges` #1, so a campaign value the DB rejects (e.g. a title over 500
  characters) fails that save and loses the day's snapshots too — the code comment says the early
  save protects against this, but it only protects against month-stage failures. (Read from code,
  not observed.)
- **Deleted items linger.** A campaign or pipeline removed in Ecomail is never deleted from Heblo;
  its stats call returns 404 and is skipped, so the row keeps its last values.
- **`SentAt` time zone is Ecomail's**, stored without conversion; `SyncedAt` is UTC. Treat
  `SentAt` as Prague-local for month bucketing (not verifiable from the repo).
- **`from_date`/`to_date` on `/stats` is a no-op** on this account (returns lifetime numbers for any
  window) — never use it for monthly figures; that is why `stats-detail` is used.
- **Admin cron/enable wins after first seed**: `RecurringJobSeeder` keeps the stored
  `CronExpression`/`IsEnabled`, so changing `Ecomail:CronExpression` later does not move an
  already-seeded job.
- **Migration `20260922124454_AddEcomailIngestTables` is manual**, like every Heblo migration.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Ecomail/Infrastructure/Jobs/EcomailSyncJob.cs` — job id, cron, lock, failure guards
- `backend/src/Anela.Heblo.Application/Features/Ecomail/Services/EcomailSyncService.cs` — the four stages, reportable filter, month window / lock / retry rules
- `backend/src/Adapters/Anela.Heblo.Adapters.Ecomail/EcomailApiClient.cs` — endpoints, paging, retry and 404/403 policy
- `backend/src/Adapters/Anela.Heblo.Adapters.Ecomail/EcomailNullableDateTimeConverter.cs` — timestamp parsing
- `backend/src/Anela.Heblo.Domain/Features/Ecomail/EcomailCampaign.cs` — `IsReportable` selection rule
- `backend/src/Anela.Heblo.Domain/Features/Ecomail/EcomailOptions.cs` / `EcomailOptionsValidator.cs` — config defaults and start-up validation
- `backend/src/Anela.Heblo.Persistence/Ecomail/*Configuration.cs` — tables, column limits, unique indexes
- `CLUSTER-B-FINDINGS.md` §8–9 — API measurements and the storage design
