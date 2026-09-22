# Metabase

Metabase is where Anela's reporting lives. Heblo ingests and shapes the data; it does not render
these reports. See **ADR-007** in [development_guidelines.md](development_guidelines.md).

## Where it runs

| | |
|---|---|
| Azure Web App | `anelametabase` (resource group `rgHeblo`, Germany West Central) |
| URL | `https://anelametabase-dwejace7e3hfadfd.germanywestcentral-01.azurewebsites.net` |
| Image | `metabase/metabase:latest` (v0.63.18.1 as of 2026-09-22) |
| Container port | 3000 (`WEBSITES_PORT`) |
| Timezone | `Europe/Prague` (`JAVA_TIMEZONE`) |
| Edition | **OSS** — this matters, see *Confidentiality* below |

Metabase keeps its own application state (questions, dashboards, users, permissions) in the
`metabase` database on `heblosql.postgres.database.azure.com`, connecting as the `metabase_app`
role. That database is Metabase's internal storage and has nothing to do with Anela's data.

As of 2026-09-22 the instance is healthy but essentially unused: 0 saved questions, ~25 queries
ever run, all in May 2026.

## How it authenticates to Postgres

Two different roles, for two different jobs:

| Role | Used for | Scope |
|---|---|---|
| `metabase_app` | Metabase's own application database | full rights on database `metabase` |
| `metabase_ro` | reading Anela's data | `SELECT` only, see *Grant model* |

> **Finding, not fixed here.** `MB_DB_PASS` and `MB_ENCRYPTION_SECRET_KEY` currently sit in the
> `anelametabase` App Service settings rather than in Key Vault, which is the opposite of the rule
> the Heblo app follows (`CLAUDE.md`: all secrets go to Key Vault). `MB_ENCRYPTION_SECRET_KEY` is
> the key Metabase uses to encrypt stored data-source credentials, so it is the more sensitive of
> the two — losing or rotating it invalidates every saved connection. Moving both to
> `kv-heblo-prod` is worth doing, but it is a change to the Metabase deployment, outside the scope
> of the ingestion work that produced this document.

## Connected data sources

| Source | What it is |
|---|---|
| `Heblo_V3` | the production Heblo database, read as `metabase_ro` |
| GA4 via BigQuery | dataset `analytics_392098710`, daily export — **data starts 2026-05-25 only** |

`Heblo_V3` carries both the operational schema (`public`) and the reporting schemas added by the
three ingestion directions. **Metabase has no cross-database joins in any edition**, which is the
whole reason the reporting schemas live inside `Heblo_V3` rather than in a database of their own
(ADR-007).

## Reporting schemas in `Heblo_V3`

| Schema | Source | Owned by |
|---|---|---|
| `flexi_raw` | Flexi (ABRA FlexiBee) general ledger, departments, contacts | `AnalyticsDbContext` |
| `shoptet_raw` | Shoptet orders | separate ingestion direction |
| `ga4_agg` | GA4 aggregates | separate ingestion direction |

Each schema carries its own `__EFMigrationsHistory`, pinned explicitly — EF Core does **not** derive
the history table's schema from `HasDefaultSchema` (ADR-007).

## Grant model

`metabase_ro` holds `SELECT` on all 96 tables of `Heblo_V3.public` — a legacy blanket grant that
predates this work.

**New reporting schemas do not follow that pattern.** They grant `USAGE` on the schema and `SELECT`
on the month-grain `v_*` read views only. Two reasons:

1. **Load.** `heblosql` is a `Standard_B1ms` Burstable instance: **1 vCore, 2 GB RAM**, shared with
   production Heblo. An ad-hoc `GROUP BY` in Metabase over ~680k raw ledger rows competes with the
   request path for that single core. Views that pre-aggregate to month grain keep the cost bounded.
2. **Confidentiality.** OSS Metabase has collection-level permissions only — **no row-level
   security and no data sandboxing**, both of which are Enterprise features. Anything the connected
   role can read, any Metabase user with query access can read. So the boundary has to be a
   Postgres grant.

### `flexi_raw`

Granted to `metabase_ro`:

All four are **materialized**. As plain views each Metabase card cost a full double scan of
`ledger_entry` — measured at 18.6 s and 118k buffer reads on 2026-09-22, because `account_name`
detoasts `raw_payload` per posting — which defeated the whole point of having a read layer. The
same queries now return in ~36 ms. They are refreshed at the end of every successful nightly sync
(`FlexiAnalyticsSyncService.RefreshReadModelsAsync`); a sync with any failed entity deliberately
skips the refresh, so Metabase keeps the last complete snapshot rather than publishing a
half-synced month.

| View | Backlog item | Grain |
|---|---|---|
| `v_cost_monthly_total` | #1 | month × cost centre |
| `v_cost_monthly_by_account` | #1 drill-down, #38–#43 | month × cost centre × account |
| `v_marketing_spend_monthly` | #38–#43 | month × account × cost centre × supplier |
| `v_ad_spend_monthly` | #31–#33 | month × channel |

Not granted to anyone: `ledger_entry`, `contact`, `department`, `accounting_template`,
`sync_state`, the row-grain helper view `v_posting`, and `v_payroll_monthly`.

Three backlog items cannot be served from `flexi_raw` as Flexi is currently coded, and the SQL
file carries the evidence for each:

- **#36 (Dobruška shop revenue)** — there is deliberately no view. The shop has its own cost
  centre, `PRODEJNA`, but it is used for the shop's *costs* only: across the whole 2020–2026 load
  `PRODEJNA` carries exactly one class-6 posting, for 0.00 Kč, while all 111.6M Kč of revenue is
  booked to cost centre `C`. Answering #36 needs a change in how Flexi books shop revenue.
- **#38–#43 by accounting template** — `flexi_raw.ledger_entry.accounting_template` is always
  NULL. FlexiBee's `ucetni-denik` evidence exposes 38 properties and the předkontace is not one of
  them; it lives on the source document, not on the journal line. `v_marketing_spend_monthly`
  groups by **account** instead, which is the dimension Anela actually books marketing against.
  Graphics, photography, PR and influencers all share account `518030 Marketing-Externiste`, so
  within that account only the supplier tells them apart — which is why supplier is in the grain.
- **#1 as literally stated** — see *Payroll* below. Note also that the cost views bound accounts to
  classes **50–56**, not all of class 5: group 58 (*změna stavu zásob vlastní činnosti*,
  *aktivace*) is a contra-cost normally credited, and group 59 is income tax. On the current load
  they are −3 334 877.67 Kč and +1 847 600.00 Kč, so including them both understated the total and
  made it unreconcilable against the accountant's figures.

The grants are defined in
[`backend/src/Anela.Heblo.Persistence.Analytics/Sql/flexi_raw_read_views.sql`](../../backend/src/Anela.Heblo.Persistence.Analytics/Sql/flexi_raw_read_views.sql),
which is idempotent and re-runnable. It ends with explicit `REVOKE`s on every ungranted object so a
mistake in an earlier version of the script cannot leave a widened grant behind.

There is deliberately **no** `GRANT SELECT ON ALL TABLES IN SCHEMA flexi_raw`, and there must never
be one: `ALL TABLES` includes views, so it would hand `v_payroll_monthly` to Metabase.

## Payroll (#35)

Backlog item #35 is marked *"pozor neveřejné"* in the source document. `flexi_raw.v_payroll_monthly`
exists and is **granted to no role**. Every general cost view excludes payroll: class 52 accounts
plus the balance-sheet accounts that settle them (331, 333, 335, 336, 342).

"Payroll" here is wider than class 52. It also covers the balance-sheet accounts that settle it
(331, 333, 335, 336, 342) and, less obviously, **`548003` *Ostatní provozní náklady – zákonné
pojištění*** — the employer's statutory liability insurance, which is a fixed permille of the wage
base. On the 2020–2026 load it is 86 442.00 Kč against a 521 base of ~20.6M, i.e. exactly the
4.2‰ statutory rate, so publishing it hands over the gross wage bill by division. It is a class-5
account and would otherwise pass straight through every general cost view.

> **The boundary this file describes is not the whole boundary.** `metabase_ro`'s legacy blanket
> grant on `Heblo_V3.public` (96 tables, predating this work) already exposes
> `OvertimeEmployees` and `OvertimeMonthlyStatements` — per-person monthly hours including
> `DoctorHours`, joinable on `PersonId`. No koruna amounts, so the literal claim about payroll
> *figures* holds, but anyone reading "Metabase cannot see payroll" as "Metabase cannot see
> sensitive HR data" would be wrong, and medical-absence hours per named person is arguably the
> more sensitive datum under GDPR. Out of scope for the ingestion work that produced this
> document, but it belongs in the same decision.

Because of the payroll exclusion, `v_cost_monthly_total` is *operating cost excluding personnel*,
not the literal total. **#1 (total monthly costs) and #35 (payroll is confidential) cannot both be served to
the same Metabase audience on OSS.** That is a decision for Andrea, not a technical gap.

If payroll access is wanted later, the shape is:

1. Create a second Postgres role, e.g. `metabase_payroll`, with `USAGE` on `flexi_raw` and `SELECT`
   on `v_payroll_monthly` only.
2. Add `Heblo_V3` a **second time** as a Metabase data source, connecting as that role.
3. Restrict that data source to a Metabase collection only Andrea can open.

Two data sources is the only way OSS Metabase can express this, since it cannot vary permissions
within one connection. Do not implement it without Andrea asking for it.

## Cross-schema joins

Ratio metrics that span sources — ROAS, PNO, cost per purchase — need `flexi_raw` joined to
`shoptet_raw` or `ga4_agg`. Those join views are **out of scope for every individual ingestion
direction** and belong to a separate piece of work once all three schemas exist. Creating them from
inside one direction would mean three parallel workstreams editing the same file.

## Operating `flexi_raw`

Full detail on the sync itself is in ADR-007; the operational summary:

- **Nightly job**: `flexi-analytics-sync`, Hangfire, `0 3 * * *` Europe/Prague. Registered only when
  `AnalyticsDatabase:ConnectionString` is non-empty, so unconfigured environments stay inert.
- **Migrations are manual** in this project:
  ```bash
  AnalyticsDatabase__ConnectionString="<ConnectionStrings--Production>" \
    dotnet ef database update \
      --project backend/src/Anela.Heblo.Persistence.Analytics/Anela.Heblo.Persistence.Analytics.csproj \
      --context AnalyticsDbContext
  ```
- **Read views** are applied the same way, by hand:
  ```bash
  psql "<connection>" -f backend/src/Anela.Heblo.Persistence.Analytics/Sql/flexi_raw_read_views.sql
  ```
- **Initial historical load** is a deliberate, watched, off-hours operation — not something the
  nightly job should discover on its own. It walks accounting-date month windows so every FlexiBee
  offset stays shallow and any interruption resumes at the month it died on:
  ```bash
  AnalyticsDatabase__ConnectionString=... \
  FlexiBeeSettings__Server=... FlexiBeeSettings__Company=... \
  FlexiBeeSettings__Login=... FlexiBeeSettings__Password=... \
  FlexiAnalyticsSync__BackfillThrottleMilliseconds=250 \
    dotnet run --project backend/tools/Anela.Heblo.FlexiAnalyticsBackfill -- 2020-01-01 2026-09-30
  ```
  It also refreshes the three dimension tables first, so a backfilled database is complete without
  waiting for the first nightly run. The read views do **not** join them — `v_ad_spend_monthly`
  matches on `ledger_entry`'s own denormalised contact label (see the note in
  `flexi_raw_read_views.sql` for why a key join was rejected); the dimensions are there for ad-hoc
  Metabase use. A dimension refresh that fails now aborts the run instead of being logged past.
- **Running the nightly job by hand** — same tool, `--incremental` instead of a date range. Use it
  to verify the job end to end without waiting for 03:00, or to catch up after an outage:
  ```bash
  AnalyticsDatabase__ConnectionString=... FlexiBeeSettings__…     dotnet run --project backend/tools/Anela.Heblo.FlexiAnalyticsBackfill -- --incremental
  ```
- **Health**: `flexi_raw.sync_state` carries a row per entity with the watermark, last run status,
  row counts and last error.

### Cut-over state (as of 2026-09-22)

The database side is live: `flexi_raw` exists in `Heblo_V3` and `Heblo_TST`, the ledger is
backfilled, the views and grants are applied, and `AnalyticsDatabase--ConnectionString` is set in
both Key Vaults.

**The nightly job is not running yet, and the apps must not be restarted until this branch
deploys.** Key Vault is read at startup, so the running production image would pick the new secret
up on its next restart — but that image still carries the pre-fix ledger mapping, which maps every
row's key to `-1` and would fail the first batch on a duplicate key. Nothing would be corrupted
(the run simply writes nothing and records `FAILED` in `sync_state`), but there is no reason to
invite it.

Cut-over, in order:

1. Merge and deploy this branch.
2. Restart `heblo` / `heblo-test` so Key Vault is re-read.
3. Confirm `flexi-analytics-sync` appears in `public."RecurringJobConfigurations"` and in the
   Hangfire dashboard's recurring-job list.
4. Confirm the first nightly run: `SELECT * FROM flexi_raw.sync_state;` should show
   `last_run_status = 'OK'` with a few hundred rows fetched, not hundreds of thousands.

Until step 2, the sync can be run by hand with the `--incremental` flag described above; that is
how it was verified on 2026-09-22 (3 ledger rows, 5 seconds, no row growth).

Staging holds the schema, the dimension tables and a single rehearsal month rather than the full
history. That is deliberate: Metabase reads `Heblo_V3`, so a second 674k-row load would cost an
hour of the same shared vCore for no consumer. Its watermark is set, so the staging nightly job
does an incremental delta rather than discovering six years of backlog.

### Watch the CPU credits, not the disk

All databases on `heblosql` together are ~1 GB against 32 GB of storage, so storage is a non-issue.
Compute is not: on a `Standard_B1ms` the baseline is 10% of one vCore and anything above that burns
banked credits (cap 288). Check before and during any bulk load:

```bash
RID=$(az postgres flexible-server show -g rgHeblo -n heblosql --query id -o tsv)
az monitor metrics list --resource "$RID" --metric cpu_credits_remaining \
  --interval PT5M --aggregation Average -o table
```

Burstable tiers support no read replicas, so there is no way to move reporting load off this
instance short of resizing the server.

`flexi_raw` is 1 146 MB, of which the great majority is `ledger_entry.raw_payload` (the full Flexi
JSON per row, kept so a missing dimension can be recovered without a re-sync). That roughly doubles
the server's total data footprint — still trivial against 32 GB, but worth knowing before adding a
second schema of the same shape.

## Cross-check against Heblo's own marketing figures

`flexi_raw.v_ad_spend_monthly` and Heblo's `MarketingPerformanceChannelCosts` answer the same
question by completely independent routes — the general ledger via `ILedgerClient` versus received
invoices filtered by supplier DIČ via `IReceivedInvoicesClient`. Comparing them is the cheapest
available audit of both.

As of 2026-09-22, for 2026-01 … 2026-09: **meta and google agree to the haléř in every month.**
**S-klik does not**, and in four of those months Heblo's stored figure is *negative*
(−7 350.00, −18 763.50, −22 690.50) where the ledger shows 35 000.00, 89 350.00 and 108 050.00.
A negative ad cost is not a real number, so the defect is on the `MarketingPerformance` side, not in
`flexi_raw`. Worth chasing: the ledger carries Seznam spend under two different counterparty
spellings, one of which has no contact code, which is exactly the shape that a DIČ-keyed invoice
search can half-miss. Not fixed here — it is a different feature.
