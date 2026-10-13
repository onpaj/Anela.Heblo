# Reporting data over MCP — design

Date: 2026-10-13
Status: draft, awaiting review

## 1. Goal

People in the company already use Heblo's MCP server (`/mcp`) from Claude. They should be able to
ask questions over the reporting data Heblo already mirrors into `Heblo_V3` — GA4 traffic,
Shoptet orders, Flexi costs — with **one permission per source** deciding who can read what.
The permission is enforced in the application layer, so it holds the same for MCP and for any
future REST/UI entry point.

Metabase is out of scope: it keeps its own access model through the `metabase_ro` Postgres role.

### Success criteria

1. A user with `Reporting_WebAnalytics` can list and query the GA4 datasets over MCP and sees no
   Shoptet or Flexi dataset; likewise for the other two permissions.
2. A user without the permission for a dataset gets `Forbidden` from the handler, whatever the
   entry point.
3. Agents can filter, sort, project columns and page through any dataset; no free SQL is accepted.
4. No query can run longer than 10 s or modify data.
5. Payroll, raw tables and per-customer rows are unreachable.

## 2. Data health (verified 2026-10-13 against production)

| Source | Schema | Last sync | Content |
|---|---|---|---|
| Flexi | `flexi_raw` | 2026-10-13 01:00 UTC, 4/4 entities OK | 682,541 ledger rows 2020-01 → now; matviews include October |
| Shoptet | `shoptet_raw` | 2026-10-12 23:30 UTC, OK | 98,084 orders / 536,979 lines 2018-09 → today, no day missing; `order_fact` in step |
| GA4 | `ga4_agg` | 2026-10-13 02:10 UTC, 6/6 entities OK | daily 2023-06-29 → yesterday, no day missing |

Not part of this design but found on the way: the `ads` schema (#4453/#4476) is not migrated in
production although `ads-daily-sync` / `ads-change-sync` are registered.

## 3. Decisions

| # | Decision | Why |
|---|---|---|
| D1 | Three new read-only features: `Reporting_WebAnalytics` (GA4), `Reporting_Eshop` (Shoptet), `Reporting_Accounting` (Flexi) | One permission per source, as requested. Read only — the data is read-only. |
| D2 | Enforcement lives in the MediatR handlers, not in the MCP tools | Same rule for every entry point (the marketing-agents spec C4 made the same call). The tools do not call `EnsureFeatureAccess`; per-dataset permission is unknown at tool level. |
| D3 | A generic catalogue: `ListReportDatasets`, `DescribeReportDataset`, `QueryReportDataset` — three tools for all sources | New views become available by adding a catalogue entry, no new tool. Fewer tools in the agent's context. |
| D4 | Datasets wrap the curated `v_*` read views only, under stable ids (`shoptet.orders_monthly`) | The views already encode the business rules (VAT, cancelled orders, class-5 bounds). Stable ids decouple prompts from SQL names. |
| D5 | Filters, sort, column projection, limit/offset; **no aggregation** in v1 | The views are already month grain; agents can sum a handful of rows. Add `groupBy` only on demand. |
| D6 | No REST controller in v1 | ADR-007: reports are not rendered in the Heblo UI. Handler-level enforcement makes adding one later safe. |
| D7 | Excluded: raw tables, `v_posting`, `v_payroll_monthly`, `v_customer_repeat_purchase`, `v_product_pair_monthly`, `v_sync_health` | Payroll is confidential; customer-grain rows carry md5(email), which is re-identifiable; the product-pair view takes **17 s for a single month** (plain view self-joining order lines) — needs materialising first; sync health is surfaced as freshness instead. |

## 4. Catalogue (v1, 17 datasets)

Defined in code (`ReportDatasetCatalog`), one entry per dataset: id, source, required feature,
SQL relation, period column, key columns (stable sort tiebreak), default sort, description, grain,
caveats, and per column: name, type (`Date`, `Text`, `Integer`, `Decimal`, `Boolean`) and
description. Every catalogue column is filterable and sortable — the views are month grain and
small, so per-column flags would add surface without protecting anything.

| Id | View | Grain |
|---|---|---|
| **GA4 — `Reporting_WebAnalytics`** | | |
| `ga4.traffic_monthly` | `ga4_agg.v_monthly_traffic` | month |
| `ga4.traffic_by_channel_monthly` | `ga4_agg.v_monthly_traffic_by_channel` | month × channel group |
| `ga4.landing_pages_monthly` | `ga4_agg.v_monthly_landing_pages` | month × landing page |
| `ga4.articles_monthly` | `ga4_agg.v_monthly_articles` | month × page |
| `ga4.conversion_monthly` | `ga4_agg.v_monthly_conversion` | month |
| **Shoptet — `Reporting_Eshop`** | | |
| `shoptet.orders_monthly` | `shoptet_raw.v_order_monthly` | month × channel |
| `shoptet.orders_by_shipping_monthly` | `shoptet_raw.v_order_shipping_monthly` | month × channel × shipping method |
| `shoptet.orders_by_payment_monthly` | `shoptet_raw.v_order_payment_monthly` | month × channel × payment method |
| `shoptet.customer_types_monthly` | `shoptet_raw.v_customer_type_monthly` | month × channel × customer status |
| `shoptet.new_customers_monthly` | `shoptet_raw.v_new_customers_monthly` | month × channel |
| `shoptet.product_sales_monthly` | `shoptet_raw.v_product_sales_monthly` | month × channel × product variant |
| `shoptet.product_sales_by_customer_type_monthly` | `shoptet_raw.v_product_sales_by_customer_type_monthly` | month × channel × customer status × product variant |
| `shoptet.set_component_units_monthly` | `shoptet_raw.v_product_set_component_units_monthly` | month × channel × component × set |
| **Flexi — `Reporting_Accounting`** | | |
| `flexi.cost_monthly` | `flexi_raw.v_cost_monthly_total` | month × cost centre |
| `flexi.cost_by_account_monthly` | `flexi_raw.v_cost_monthly_by_account` | month × cost centre × account |
| `flexi.marketing_spend_monthly` | `flexi_raw.v_marketing_spend_monthly` | month × account × category × cost centre × supplier |
| `flexi.ad_spend_monthly` | `flexi_raw.v_ad_spend_monthly` | month × channel × supplier × account |

Caveats come from the views' `COMMENT`s and the design notes in the SQL files, e.g. Shoptet
revenue is CZK from the order header, `*_with_vat` includes VAT, cancelled orders are excluded,
`channel` is `eshop_mo` / `eshop_vo` / `prodejna`; GA4 conversions will not match the ERP; channel
sessions do not sum to the site total; Flexi costs are classes 50–56 excluding personnel cost, and
ad spend is matched on the supplier label.

Measured on production 2026-10-13: every included view answers a filtered query in < 2 s
(`ga4.articles_monthly` is the slowest at 1.8 s for nine months).

## 5. Tools and query model

### `ListReportDatasets`
No parameters. Returns the datasets the caller may read (others are silently omitted), each with
id, source, description, grain, and per source `freshness` = `{ lastSyncAt, status }` read from the
source's `sync_state` table (`status` = `OK` only when every entity's last run is `OK`).

### `DescribeReportDataset(dataset)`
Columns (name, type, description), default sort, period column, caveats,
freshness. Unknown id → `ReportingDatasetNotFound` with up to three fuzzy suggestions. No
permission → `Forbidden`.

### `QueryReportDataset(dataset, columns?, filters?, sort?, limit?, offset?)`

- `columns`: subset to return; default all. Unknown column → `ReportingInvalidQuery`.
- `filters`: up to 20 of `{ column, op, value?, values? }`. Ops: `eq`, `neq`, `gt`, `gte`, `lt`,
  `lte` (one `value`), `in` (1–100 `values`), `between` (exactly 2 `values`, inclusive),
  `contains` (Text only, case-insensitive, `%`/`_`/`\` escaped), `isNull` (`value` `true`/`false`).
  `gt..lte`/`between` not allowed on Text or Boolean; `contains` only on Text.
  Values arrive as strings and are parsed by column type: Date accepts `yyyy-MM-dd` or `yyyy-MM`
  (→ first of month), Decimal/Integer invariant culture, Boolean `true`/`false`.
- `sort`: up to 5 of `{ column, direction: asc|desc }`. Default = the
  dataset's default sort. The dataset's key columns are always appended as a tiebreak.
- `limit`: 1–1000, default 100. `offset`: ≥ 0, max 100,000.

Response: `columns` (name + type, in output order), `rows` (array of value arrays), `rowCount`,
`hasMore` (one extra row is fetched to decide), `caveats`, `freshness`.

## 6. Safety

- **SQL construction**: identifiers come only from the catalogue and are emitted double-quoted;
  every value is an `NpgsqlParameter` with an explicit `NpgsqlDbType`. Nothing from the request is
  concatenated into SQL.
- **Execution**: a raw `NpgsqlConnection` (not EF), `BEGIN TRANSACTION READ ONLY` +
  `SET LOCAL statement_timeout = '10s'`. Raw ADO.NET on purpose: an EF `SqlQuery` runs under the
  Polly execution strategy, whose 10 s per-attempt timeout would *retry* a slow query four times
  on the shared B1ms core. Postgres `57014` (query cancelled) → `ReportingQueryTimeout`.
- **Connection**: the app's singleton `NpgsqlDataSource` (registered by `PersistenceModule`, same
  pool as `ApplicationDbContext`; the views live in `Heblo_V3`). In in-memory mode there is none and
  the reader is replaced by one that fails with a clear message.
- **Load**: limit cap 1,000 rows, timeout 10 s, the expensive view excluded (D7). MCP traffic is
  interactive and low-volume; no extra rate limiter in v1 (C4 adds one for app-only callers).

## 7. Errors

New module range **39XX (Reporting)**:

| Code | HTTP | When |
|---|---|---|
| `ReportingDatasetNotFound = 3901` | 404 | unknown dataset id |
| `ReportingInvalidQuery = 3902` | 400 | bad column / op / value / sort / limit; `Params["Reason"]` says what |
| `ReportingQueryTimeout = 3903` | 504 | statement timeout hit |

Permission failures use the existing `Forbidden = 0014`. The MCP tools turn any non-success
response into `McpException("[<CODE>] <reason>")` so the agent can correct itself. Adding codes
also needs the `ErrorHandlingTests` range bucket and Czech translations in `i18n.ts`.

## 8. Authorization rollout

- `access-matrix.json`: three features, read only, labels
  "Data: Google Analytics", "Data: Shoptet objednávky", "Data: Účetnictví (Flexi)".
  Regenerate with `AccessMatrixGen` (the Debug build does it).
- Seed groups (fresh databases only): `Spravce` and `Vedeni` all three; `Marketer` WebAnalytics +
  Eshop; `Ucetni` Accounting; `Eshop_Administrator` Eshop. **Open question for review.**
- Existing environments: `JsonGroupSeeder` does not add permissions to existing groups — after
  deploy, grant the roles in `/admin/access` on staging, then production (see #4198).

## 9. Testing

- Unit: catalogue integrity (unique ids, every default sort / key / period column exists, every
  dataset has a feature, excluded views absent); query builder (each op, type parsing, escaping,
  quoting, tiebreak, limit+1, every rejection); handlers (permission filtering in List, `Forbidden`
  in Describe/Query, not-found suggestions, timeout mapping); MCP tools (parameter mapping,
  serialisation, error → `McpException`).
- Integration (`Category=Integration`, Testcontainers Postgres): the executor against a small
  fake view — parameters bind, read-only transaction rejects writes, `statement_timeout` fires
  (`pg_sleep`), timeout maps to `ReportingQueryTimeout`.
- Manual on staging after deploy: one query per source through Claude with a user holding one
  permission, and a denial for a source they lack.

## 10. Out of scope / follow-ups

- Materialise `v_product_pair_monthly`, then add `shoptet.product_pairs_monthly`.
- `groupBy`/aggregates if agents keep summing large result sets.
- Payroll as its own permission (`Reporting_Payroll`) if ever needed.
- Ads datasets: covered by the marketing-agents platform (C4) under `Marketing_Ads`.
- REST endpoint / UI.
