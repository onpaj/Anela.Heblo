---
process: module-analytics
kind: module
module: analytics
summary: Anela's reporting layer — nightly raw mirrors of the Flexi ledger, Shoptet orders and GA4 traffic for Metabase, plus the in-app margin analysis and import-health statistics.
owns: []
verified_at: "5e993f9e2"
related: [sync-flexi-analytics, sync-shoptet-orders, sync-ga4-aggregates, calc-product-margin-summary, calc-import-statistics, calc-margins]
---

# Analytics

## Purpose
The analytics module answers business questions that span time — what did we spend, sell, earn
and attract month by month. It does that in two ways:

1. **Reporting mirrors for Metabase** (ADR-007, `docs/architecture/metabase.md`). Each night,
   three syncs copy raw data from Anela's systems into dedicated schemas of the production
   database `Heblo_V3`:
   - the accounting ledger from Flexi into `flexi_raw`;
   - e-shop orders from Shoptet into `shoptet_raw`;
   - web traffic from Google Analytics 4 into `ga4_agg`.

   Each schema publishes month-grain `v_*` views to the read-only Metabase role. Reports, dashboards
   and ad-hoc questions are built in **Metabase**, never in the Heblo UI. Everything is in one
   database because Metabase cannot join across databases, so cross-source ratios (ROAS, cost per
   order) need all three schemas side by side.
2. **Two in-app reports.** These are the margin analysis page (Analýza marže), which values sales
   with the catalog's per-unit margins, and an import-health check that counts how many invoices
   were imported per day.

## Users & screens
- **Management / finance**: the page **Analýza marže** (`/analytics/product-margin-summary`,
  sidebar Finance section). It shows a monthly stacked chart and a table of M0/M1/M2 margin by
  product, family or category, for windows from 6 to 24 months. Permission:
  `finance.margin_analysis.read`.
- **Whoever watches the invoice import**: the page **Import vydaných faktur**
  (`/automation/invoice-import-statistics`) shows daily counts of issued invoices by issue date or
  sync date, with days under the threshold highlighted. The dashboard tile **Faktury importované
  včera** links to it.
- **Metabase users** (owners, marketing): the views listed under *Data owned*, in the
  `anelametabase` instance, connected to `Heblo_V3` as `metabase_ro`.
- **MCP**: no analytics tool. Margins per product are served by `GetProductMargins` (see
  `calc-margins`).

## Processes
- `sync-flexi-analytics` — Flexi general ledger, cost centres, předkontace and contacts →
  `flexi_raw`. Hangfire `flexi-analytics-sync`, `0 3 * * *`.
- `sync-shoptet-orders` — Shoptet order headers and lines → `shoptet_raw`, with a resumable
  history backfill and a materialized `order_fact`. Hangfire `shoptet-orders-sync`, `30 1 * * *`.
  Manual runner `backend/tools/Anela.Heblo.ShoptetOrdersBackfill`.
- `sync-ga4-aggregates` — GA4 Data API traffic, channels, landing pages, pages and purchase events →
  `ga4_agg`. Hangfire `ga4-aggregates-sync`, `10 4 * * *`.
- `calc-product-margin-summary` — the margin analysis report (on demand, `GET /api/analytics/product-margin-summary`;
  also the unused `margin-report` and `margin-analysis` endpoints).
- `calc-import-statistics` — daily invoice and bank-statement counts, the statistics page and the
  dashboard tile (on demand).

There is no plain CRUD in this module. It has no user action that writes anything.

## Data owned
In `Heblo_V3` (staging: `Heblo_TST`). Each schema has its own `__EFMigrationsHistory`, and
migrations and view SQL are applied by hand.
- `flexi_raw.ledger_entry` — one Flexi journal posting (`ucetni-denik`) with debit and credit
  account, cost centre and CZK amount. `flexi_raw.department`, `accounting_template`, `contact` —
  Flexi dimensions. `flexi_raw.sync_state` — per-entity watermark and health. Materialized views
  `v_cost_monthly_total`, `v_cost_monthly_by_account`, `v_marketing_spend_monthly` and
  `v_ad_spend_monthly` are granted to Metabase. `v_payroll_monthly` is granted to nobody.
- `shoptet_raw."order"` — one Shoptet order (header prices in the order's currency, status,
  channel markers, customer e-mail). `shoptet_raw.order_item` — one priced `items[]` line or one
  unpriced set component. `shoptet_raw.sync_state` — backfill cursor and incremental watermark.
  `wholesale_shipping` and `sales_channel` are hand-seeded reference data. `order_fact` is a
  materialized view holding the channel (MO/VO/prodejna) and new/returning rules; it is not granted.
  Eleven `v_*` views are granted.
- `ga4_agg.traffic_monthly`, `traffic_total_daily`, `traffic_daily`, `conversions_daily`,
  `landing_page_daily` and `page_daily` hold GA4 aggregates at the grain in their names.
  `ga4_agg.sync_state` holds per-table watermarks. Five plain `v_monthly_*` views are granted.
- The in-app reports own no tables. They read the catalog cache, `IssuedInvoices` and
  `BankStatements`.

## External systems
- **ABRA Flexi (FlexiBee)**, read only: evidences `ucetni-denik`, `stredisko`, předkontace and the
  address book (`sync-flexi-analytics`).
- **Shoptet REST API**, read only: `GET /api/orders` (by creation or change time),
  `GET /api/orders/{code}` and `GET /api/orders/changes`, with the private API token shared with
  the warehouse flows, paced at 3 req/s (`sync-shoptet-orders`).
- **Google Analytics 4 Data API**, read only: `RunReport` on property `392098710`, using a service
  account from Key Vault (`sync-ga4-aggregates`).
- **Metabase** reads this module's views. It never writes, and Heblo never calls it.

## Dependencies
- Reads from **Catalog**: the in-memory catalog cache — products, sales history and the per-unit
  margins from `calc-margins` (via `CatalogAnalyticsSourceAdapter`). Bundle-expanded sales from
  `calc-bundle-sales-expansion` are deliberately excluded.
- Reads from **Invoices**: the `IssuedInvoices` written by the Shoptet → Flexi invoice import
  (jobs `daily-invoice-import-czk` / `-eur`).
- Reads from **Bank**: the `BankStatements` written by the bank statement import.
- Uses the shared **Shoptet** API settings (`Shoptet:BaseUrl` and `Shoptet:ApiToken`).
- **Consumers**: Metabase only. No other Heblo module reads these schemas.

## Known quirks
- **All three syncs are inert until their secret exists.** Each registers its job only when its
  connection string or credentials are configured (`AnalyticsDatabase:ConnectionString`,
  `ShoptetOrdersSync:ConnectionString`, `GoogleAnalytics:PropertyId` + `CredentialsJson`).
  Registration happens at startup, so a newly added Key Vault secret needs an app restart. The
  Flexi stack sat dormant from May to September 2026 for exactly this reason. `shoptet_raw` went
  live in production on 2026-09-24.
- **Hangfire "Succeeded" is not proof of a good run.** The GA4 and Shoptet jobs record failed or
  timed-out entities in their `sync_state` and return normally. Only `flexi-analytics-sync` throws.
  Check `sync_state` (Shoptet: `v_sync_health`).
- **The admin enable toggle does nothing for these jobs.** `RecurringJobDiscoveryService` reads only
  the cron, so the toggle in Recurring Jobs does not stop them. Use each job's own `…:Enabled`
  setting. A cron changed in config after the first seed is ignored too.
- **The database server is small.** `heblosql` is a Standard_B1ms burstable server (1 vCore, 2 GB)
  shared with production Heblo. That is why the raw tables are never granted, the views are month
  grain, and batches and pacing are small. Watch `cpu_credits_remaining` during any backfill.
- **GA4, Shoptet and Flexi will not reconcile.** GA4 purchase events, Shoptet orders and Flexi
  revenue each measure something different. Their individual docs say how.
- **The margin analysis values history at today's margin.** It applies the latest per-unit margin
  to every month's units, and it is capped by the catalog's 400-day sales history (100 days on
  staging).
- **Unused endpoints.** `GET /api/analytics/margin-report`, `/margin-analysis` and
  `/bank-statement-import-statistics` have no frontend caller.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — DI for the in-app reports and the tile
- `backend/src/Anela.Heblo.API/Controllers/AnalyticsController.cs` — the five endpoints and the permission
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/` — the `flexi_raw` sync
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Analytics/` — the `shoptet_raw` sync
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAnalytics/` — the `ga4_agg` sync
- `backend/src/Anela.Heblo.Persistence.Analytics/`, `Anela.Heblo.Persistence.ShoptetOrders/`, `Anela.Heblo.Persistence.Ga4/` — DbContexts, migrations, `Sql/*.sql` views and grants
- `docs/architecture/metabase.md` — Metabase instance, grant model, operations
