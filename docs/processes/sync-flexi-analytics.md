---
process: sync-flexi-analytics
kind: sync
summary: Nightly copy of the Flexi (ABRA FlexiBee) general ledger, cost centres, accounting templates and contacts into Heblo_V3.flexi_raw, refreshing the month-grain views Metabase reports read.
owns:
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/**
  - backend/src/Anela.Heblo.Persistence.Analytics/**
  - backend/tools/Anela.Heblo.FlexiAnalyticsBackfill/**
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs
verified_at: "a008e2306"
related: []
---

# Flexi ledger → flexi_raw (Metabase reporting)

## Purpose
Feeds Anela's reporting in Metabase (ADR-007: reports live in Metabase, never in the Heblo UI).
Answers questions such as "what did we spend per month / per cost centre / per account",
"marketing spend by category and supplier", "ad spend per channel (Meta, Google, S-klik)".
Metabase reads `Heblo_V3` as role `metabase_ro`, which can see only four materialized views:
`v_cost_monthly_total`, `v_cost_monthly_by_account`, `v_marketing_spend_monthly`,
`v_ad_spend_monthly`. Heblo's own margin calculation does **not** read this data; it queries
FlexiBee live (see `calc-margins`).

## Trigger
- Hangfire recurring job `flexi-analytics-sync` (category Integrations), cron `0 3 * * *`,
  time zone Europe/Prague, `[AutomaticRetry(Attempts = 0)]` — a failed run is not retried; the
  next night resumes from the watermark. Can be triggered from the Recurring Jobs admin page.
- Registered only when `AnalyticsDatabase:ConnectionString` is non-empty; otherwise the whole
  stack (job, services, `AnalyticsDbContext`) is absent.
- By hand: `dotnet run --project backend/tools/Anela.Heblo.FlexiAnalyticsBackfill -- --incremental`
  runs exactly the nightly work; `-- <from> <to>` (yyyy-MM-dd) runs the historical ledger backfill.

## Data flow
Source: FlexiBee REST via Rem.FlexiBeeSDK. Target: schema `flexi_raw` in `Heblo_V3`
(staging `Heblo_TST`), own `AnalyticsDbContext`, own migrations and `__EFMigrationsHistory`.

1. `FlexiAnalyticsSyncJob.ExecuteAsync` links the Hangfire token with a
   `RequestTimeoutSeconds` (120 s) timeout that bounds the **whole run**, then calls
   `FlexiAnalyticsSyncService.SyncAllAsync`.
2. The orchestrator runs the entity syncs in DI registration order, clearing the EF change
   tracker before each one:
   1. `ledger_entry` — `LedgerSyncService`: incremental, FlexiBee `ucetni-denik` filtered
      `lastUpdate gte (watermark − 1 h)` (first run: `InitialBackfillFrom`), ordered by
      `lastUpdate`, paged by `BatchSize` with `skip`.
   2. `department` — `DepartmentSyncService`: full refresh of cost centres (`stredisko`).
   3. `accounting_template` — `AccountingTemplateSyncService`: full refresh of předkontace.
   4. `contact` — `ContactSyncService`: full refresh of the address book (all four relation types).
3. Each entity upserts by `flexi_id` (existing rows updated in place, new rows added), keeps the
   full Flexi JSON in `raw_payload` (jsonb) and records its run in `flexi_raw.sync_state`.
4. Only if **all four** succeeded: `REFRESH MATERIALIZED VIEW` for the four `v_*` views.
5. If any entity failed, the job throws so Hangfire records the run as Failed.

Health lives in `flexi_raw.sync_state` (one row per entity: `watermark`, `last_run_status`
= `RUNNING|OK|FAILED|CANCELLED|BACKFILL`, start/finish times, rows fetched/upserted,
`last_error_message` truncated to 2000 chars).

## Logic & formulas
**Ledger row mapping** (`LedgerSyncService.Map`):
- Key `flexi_id` = `idUcetniDenik` (SDK `JournalId`), never `id` — `ucetni-denik` is a view and
  returns `id = -1` for every row. A row without a parsable id fails the batch.
- `account_debit` / `account_credit` / `cost_center` / `currency` = `.FirstOrDefault()?.Code`
  of the nested `mdUcet` / `dalUcet` / `stredisko` / `mena` arrays.
- `amount` = `AmountLocal` (local currency, CZK), `numeric(18,4)`; one row carries both the
  debit and the credit account.
- `entry_date` = accounting date; `period` = `postingPeriod` (e.g. "2026/06", may differ from the
  entry month); `document_type` = `idDokl@evidencePath` (faktura-prijata, banka, interni-doklad,
  skladovy-pohyb, pokladni-pohyb); `contact` = `firma@showAs` ("CODE: Name"), else `nazFirmy`.
- `last_modified` = FlexiBee `lastUpdate` (Prague local time) converted to UTC with
  `TimeZoneInfo.Local`, i.e. it assumes the container runs on Prague time; on a UTC host it
  would be off by 1–2 h. Same conversion in the department, template and contact syncs.

**Watermark (ledger only)**: on success = time the run finished (UTC). On failure or
cancellation = the highest `last_modified` actually written, never moved backwards — sound
because pages arrive ordered by `lastUpdate`; the −1 h overlap on the next run re-reads any
tie group split across a page boundary, and upserts are idempotent. Department writes a
watermark it never reads; accounting templates and contacts are full refreshes with no watermark.

**Backfill** (`ILedgerBackfillService.BackfillAsync`, tool only): walks accounting-date calendar
months, pausing `BackfillThrottleMilliseconds` between pages. Stamps the watermark (to the
backfill **start** time) only if the range reaches today; 0 rows over the whole range is treated
as a failed query, not an empty period.

**Read views** (`Sql/flexi_raw_read_views.sql`, applied by hand):
- `v_posting` (not granted) splits each row into an MD side (+amount) and a DAL side (−amount)
  and flags `is_payroll`: accounts 52x, 331, 333, 335, 336, 342 and `548003`.
- Cost views keep accounts matching `^5[0-6]` (classes 50–56) and drop payroll rows.
  `v_cost_monthly_total` is therefore *operating cost excluding personnel*, not total cost.
- `v_marketing_spend_monthly`: accounts 518030–518036, 518004, 501002, 501004 by month,
  account, category, cost centre and supplier.
- `v_ad_spend_monthly`: channel by contact label prefix — `META: ` / `Meta Platform…` → meta,
  `GOOGLE: ` / `Google Ireland…` → google, `SEZNAMCZ: ` / `Seznam.cz,…` → sklik; accounts `^5[0-6]`.
- `v_payroll_monthly` exists and is granted to no role.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `AnalyticsDatabase:ConnectionString` | `""` (appsettings.json, Staging); absent in Production.json | Empty = whole sync unregistered. Real value comes from Key Vault `AnalyticsDatabase--ConnectionString` |
| `AnalyticsDatabase:MaxPoolSize` | 10 | Separate Npgsql pool for the analytics context |
| `FlexiAnalyticsSync:Enabled` | true | Job skips itself when false; also the job's default enabled flag |
| `FlexiAnalyticsSync:CronExpression` | `0 3 * * *` | Seed cron (see quirks: admin value wins after first seed) |
| `FlexiAnalyticsSync:TimeZone` | Europe/Prague | Cron time zone |
| `FlexiAnalyticsSync:BatchSize` | 500 | FlexiBee page size and upsert batch |
| `FlexiAnalyticsSync:InitialBackfillFrom` | 2020-01-01 | Ledger start when no watermark exists |
| `FlexiAnalyticsSync:RequestTimeoutSeconds` | 120 | Timeout for the entire nightly run |
| `FlexiAnalyticsSync:BackfillThrottleMilliseconds` | 250 (class default) | Pause between backfill pages |

## Runtime facts
- Live in production since 2026-09-24: `sync_state` shows all four entities OK; first nightly
  delta was 1 637 ledger rows — agent memory `project_flexi_raw_reporting_schema` — 2026-09-24.
- `flexi_raw.ledger_entry` held 673 898 rows, 2020-01-01..2026-09-30, 1 146 MB (mostly
  `raw_payload`), reconciled exactly with FlexiBee; loaded in 63 min by the backfill tool —
  same memory + `docs/architecture/metabase.md` — 2026-09-22.
- Cost centres present: BUVOL, C, MARKETING, PRODEJNA, SKLAD, VYROBA — same memory — 2026-09-24.
- Staging `Heblo_TST` holds the schema, dimensions and one rehearsal month only, with its
  watermark set — `docs/architecture/metabase.md` — 2026-09-22.
- `metabase_ro`'s SELECT on the matviews is visible only via `has_table_privilege`;
  `information_schema.role_table_grants` omits materialized views — agent memory — 2026-09-24.
- As plain views each Metabase card took 18.6 s; as matviews ~36 ms —
  `docs/architecture/metabase.md` — 2026-09-22.

## Known quirks
- **Hangfire status used to lie.** Entity failures are counted, not thrown, so until #4301 a
  run with 4 failed entities showed Succeeded. The job now throws when `FailedServices > 0`.
  `sync_state` remains the per-entity source of truth.
- **One bad ledger page used to fail all four entities** (first live run, 2026-09-24: 1 260 of
  1 637 rows lost, `sync_state` stuck on RUNNING for six hours). Cause: skip paging over
  `order=lastUpdate` with no tiebreaker returned a row twice → "already being tracked" → the
  orphaned INSERTs were flushed by the FAILED bookkeeping and by every later entity on the
  shared context. Fixed in #4299: de-dup by key per batch, `ChangeTracker.Clear()` after every
  batch, before terminal bookkeeping, and between entities.
- **The dimension syncs do not clear the tracker before writing FAILED** (only the ledger does).
  A failed `SaveChanges` inside department/template/contact can still leave that entity's
  `sync_state` on RUNNING; the orchestrator counts it failed either way. (Read from code, not
  observed.)
- **The 120 s timeout covers the whole run.** A large ledger delta is cancelled, recorded
  CANCELLED with its high-water mark kept, the remaining entities do not run, views are not
  refreshed, and Hangfire records Failed; the next night continues from the mark.
- **The order-by-`lastUpdate` guarantee is real** (`LedgerRequest(DateTime since)` sets
  `Order = "lastUpdate"`, pinned by `LedgerRequestOrderingTests`). It was wrongly reverted once
  (69512771e) on the claim the SDK sends no order; don't repeat that.
- **288k ledger rows share `lastUpdate` = 2025-06-03** (FlexiBee bulk touch) — the reason
  duplicates across pages happen at all.
- **`accounting_template` on ledger rows is always NULL**: `ucetni-denik` has no předkontace
  property. Marketing is grouped by account instead; backlog #38–#43 by template cannot be served.
- **`contact.cin` / `contact.vatin` are always NULL**: the SDK's `ContactListRequest` never
  requests `ic`/`dic`. Anything keyed on supplier VAT id must match the contact label.
- **An empty contact type list is not "no filter"**: it renders `typVztahuK in ()`, FlexiBee
  rejects it, and the client logs and returns an empty list. Hence all four types are passed and
  an empty result throws.
- **Class 5 is not all operating cost**: group 58 is a contra-cost (−3 334 877.67 Kč on the
  2020–2026 load) and 59 is income tax (+1 847 600.00 Kč), so views bound to `^5[0-6]`.
- **`548003` leaks payroll**: statutory liability insurance is 4.2 ‰ of the wage base, so it is
  in the payroll predicate even though it is not 52x.
- **PRODEJNA carries costs only**; all shop revenue is booked to cost centre C, so shop revenue
  (#36) cannot come from this data.
- **Admin cron/enable wins after first seed**: `RecurringJobSeeder` preserves the stored
  `CronExpression`/`IsEnabled` in `RecurringJobConfigurations`, so changing
  `FlexiAnalyticsSync:CronExpression` later does not move an already-seeded job.
- **The stack was dormant May–Sept 2026**: written in May against a separate `anela_analytics`
  database that was never created; ADR-007 (2026-09-22) moved it into `Heblo_V3.flexi_raw`.
  `docs/superpowers/plans/2026-05-20-flexi-analytics-sync.md` describes the superseded design.
- **Migrations and view SQL are manual** (`dotnet ef database update --context AnalyticsDbContext`,
  `psql -f …/flexi_raw_read_views.sql`); a new view needs both the SQL and, if Metabase should
  see it, a line in `ReadModels` so it gets refreshed.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/FlexiAnalyticsSyncJob.cs` — job metadata, timeout, failure → Hangfire
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/FlexiAnalyticsSyncService.cs` — entity loop, tracker clearing, matview refresh
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/LedgerSyncService.cs` — incremental + backfill, watermark rules, row mapping
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ContactSyncService.cs` — contact full refresh, empty-result guard
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/DepartmentSyncService.cs` — cost centres
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/AccountingTemplateSyncService.cs` — předkontace
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/FlexiAnalyticsSyncOptions.cs` — config defaults
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` — connection-string-gated registration
- `backend/src/Anela.Heblo.Persistence.Analytics/AnalyticsDbContext.cs` — tables, columns, schema pin
- `backend/src/Anela.Heblo.Persistence.Analytics/AnalyticsPersistenceModule.cs` — own data source and resilience pipeline
- `backend/src/Anela.Heblo.Persistence.Analytics/Sql/flexi_raw_read_views.sql` — views, payroll predicate, grants
- `backend/tools/Anela.Heblo.FlexiAnalyticsBackfill/Program.cs` — backfill / `--incremental` runner
- `docs/architecture/metabase.md` — operations, grant model, cut-over notes
