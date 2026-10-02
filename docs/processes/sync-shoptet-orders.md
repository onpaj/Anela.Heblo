---
process: sync-shoptet-orders
kind: sync
module: analytics
summary: Nightly mirror of every Shoptet (anela.cz) order header and line item into Heblo_V3.shoptet_raw, with a resumable full-history backfill, a materialized order_fact and eleven month-grain views for Metabase.
owns:
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Analytics/**
  - backend/src/Anela.Heblo.Persistence.ShoptetOrders/**
  - backend/tools/Anela.Heblo.ShoptetOrdersBackfill/**
verified_at: "5e993f9e2"
related: [sync-ga4-aggregates, sync-flexi-analytics, calc-bundle-sales-expansion]
---

# Shoptet orders → shoptet_raw (Metabase reporting)

## Purpose
Gives Anela an order book it can report on in Metabase (ADR-007). It answers questions such as
"orders, revenue and average order value per month for retail e-shop (MO), wholesale (VO) and the
shop (prodejna)", "new vs returning customers", "how often customers buy again", "best-selling
products and sizes", "which products are bought together", and "orders by shipping or payment
method". The backlog items are #10–#19 and #25–#30.

Metabase reads it as role `metabase_ro`, which can see **only** the eleven `v_*` views. The raw
tables and `order_fact` are not granted. Heblo itself does not read `shoptet_raw`. The packing,
expedition and invoice features call Shoptet live through their own clients.

## Trigger
- Hangfire recurring job `shoptet-orders-sync` (category Integrations), cron `30 1 * * *`, time
  zone Europe/Prague, `[AutomaticRetry(Attempts = 0)]`. A stopped run resumes the next night. It can
  also be triggered from the Recurring Jobs admin page.
- The job is registered only when `ShoptetOrdersSync:ConnectionString` (Key Vault
  `ShoptetOrdersSync--ConnectionString`) is non-blank **and** parses as an Npgsql connection
  string with a Host. A value that does not parse writes `[ShoptetOrdersSync] … stays
  unregistered` to stderr at startup and leaves the stack absent. It does not stop the app from
  booting. Registration happens at startup, so a new secret needs an app restart.
- `ShoptetOrdersSync:Enabled = false` makes the job return without doing anything.
- By hand: `dotnet run --project backend/tools/Anela.Heblo.ShoptetOrdersBackfill` runs the same
  orchestrator as the job, not a separate implementation. It needs
  `ShoptetOrdersSync__ConnectionString` and `Shoptet__ApiToken`. It is meant for the long
  first backfill, run off-hours.

## Data flow
Source: the Shoptet REST API (`Shoptet:BaseUrl`, default `https://api.myshoptet.com`, header
`Shoptet-Private-API-Token` from `Shoptet:ApiToken` — the same token the warehouse flows use). Target: schema `shoptet_raw` in
the database named by the connection string (`Heblo_V3` in production, `Heblo_TST` on staging),
with its own `ShoptetOrdersDbContext`, its own `__EFMigrationsHistory`, and its own pool
(`MaxPoolSize` 10).

`ShoptetOrdersSyncJob` applies a `JobTimeoutSeconds` (18 000 s = 5 h) timeout, then
`ShoptetOrdersSyncService` runs **one of two phases**:

**Phase 1 — history backfill** (`ShoptetOrderBackfillService`, `sync_state.entity_name =
'order_backfill'`). It runs until `backfill_completed` is true.
1. Start at `backfill_cursor`, or at `BackfillFrom` (2018-01-01) on the very first run.
2. Walk creation-time windows of `BackfillWindowDays` (31) up to tomorrow (UTC date). For each
   window, page through `GET /api/orders?itemsPerPage=50&page=N&creationTimeFrom=…&creationTimeTo=…`
   (store-time-zone midnights, ISO 8601 with offset) and collect the order codes, de-duplicated.
3. Ingest the codes (see below), then save the cursor at the window's end.
4. Stop when the window reaches tomorrow, which sets `backfill_completed = true`, or after
   `BackfillMaxMinutesPerRun` (240 min). Once complete, every later run skips this phase.
5. While the backfill is unfinished, **the incremental phase does not run at all**.

**Phase 2 — nightly incremental** (`ShoptetOrderIncrementalSyncService`, `entity_name = 'order'`).
1. `changedSince` = `watermark − WatermarkSafetyMarginHours` (2 h). With no watermark, it starts 29
   days back, or at the backfill's start time if the backfill ran earlier than that.
2. If `changedSince` falls within the last 29 days (Shoptet guarantees 30 days of change log):
   - read `GET /api/orders/changes?from=…&itemsPerPage=1000` — the only source of **deletions**
     (`changeType = delete`); every other change counts as edited;
   - add the codes from `GET /api/orders?changeTimeFrom=…`, a safety net in case new orders stop
     appearing in the change log;
   - take out the deleted codes.
   Otherwise only the change-time listing is used, and a warning says deletions in that window were
   not detected.
3. Delete the deleted orders (header and lines) from `shoptet_raw`.
4. Ingest the edited codes.
5. On success, `watermark` = the time the run **started**.

**Ingest** (`ShoptetOrderIngestor`, shared by both phases). For each code it calls
`GET /api/orders/{code}`, because the list endpoint carries no line items. A 404 means the order was
deleted in the meantime and is skipped. A body whose `code` differs from the requested one is
skipped with a warning. The response is mapped and upserted every `BatchSize` (200) orders. For each
batch, the existing lines are deleted first, then the headers are inserted or updated and the lines
re-inserted. The full JSON is kept in `order.raw_payload`.

**Finish.** Whatever the phase, unless the run was cancelled, it then runs
`REFRESH MATERIALIZED VIEW CONCURRENTLY shoptet_raw.order_fact` on a raw connection with a 300 s
command timeout. If `order_fact` does not exist (the views SQL was never run), it only logs a
warning.

**Requests.** All requests are paced by a process-wide `ShoptetApiThrottle` at
`RequestsPerSecond` (3). A 429, a 5xx, or a network error or timeout is retried up to
`MaxRetryAttempts` (5) times with 1, 2, 4, 8 and 16 s back-off, and a 429 also pushes back the shared
pacer. Each request has a `RequestTimeoutSeconds` (60) timeout. Paging is strict: a missing
paginator, an empty page before the last one, or fewer rows than `totalCount` throws instead of
advancing the cursor or watermark.

Health: `shoptet_raw.sync_state` (`watermark`, `backfill_cursor`, `backfill_completed`,
`last_run_*`, status `RUNNING|OK|FAILED|CANCELLED`, `last_error_message` up to 2000 characters). It
is exposed to Metabase as `v_sync_health`.

## Logic & formulas
**Header mapping** (`ShoptetOrderMapper.Map`):
- Key: `code`. Times are stored in UTC.
- `order_date` = the creation time converted to `StoreTimeZone` (Europe/Prague), so an order placed
  at 23:30 belongs to the day the owners would expect.
- Prices come from Shoptet's header `price`: `price_with_vat`, `price_without_vat`, `price_vat`,
  `price_to_pay`, in the **order's currency**, plus `currency_code` and `exchange_rate`.
- `status_id`/`status_name`, `is_paid` (`paid ?? false`), `cash_desk_order`, the shipping and
  payment guid/name, the billing method, the source, the sales channel, and the billing company,
  ICO, city, ZIP and country.
- `customer_email` is trimmed and lower-cased; blanks become NULL.

**Lines** (`order_item`, key `order_code + line_no`):
- Every entry of `items[]` (`source_array = 'items'`), including a set's header line, which carries
  the price.
- Plus only the `product-set-item` entries of `completion[]` (`source_array = 'completion'`). These
  are the unpriced set components. Their `amount` is already the total for the whole order and must
  not be multiplied by the set quantity.
- `line_price_*` = Shoptet `itemPrice`, the **line total**. `unit_price_*` = `unitPrice`.

**Header roll-ups.** These are summed from `items[]` lines only:

| item_type | adds to |
|---|---|
| `product`, `product-set` | `product_price_*` and `product_units` |
| `service` | `product_price_*` only (money, but not units) |
| `shipping` | `shipping_price_*` |
| `billing` | `billing_price_*` (payment fee) |
| `discount-coupon`, `volume-discount` | `discount_*` |

**`order_fact`** (a materialized view, never granted). It is the one place the business rules live:
- `is_cancelled` = `status_id = -4`.
- `channel`: `prodejna` if `cash_desk_order`; `eshop_vo` if the shipping guid is in
  `shoptet_raw.wholesale_shipping`; otherwise `eshop_mo`. Wholesale shipping methods are the
  **only** marker of a wholesale order.
- `customer_key` = the lower-cased e-mail, otherwise `'guid:' || customer_guid`, otherwise NULL.
- `customer_status`, computed over non-cancelled orders of the same `customer_key`, ordered by date
  and then code:
  `new` (first ever), `returning` (the previous order is ≤ 365 days earlier), `returning_lapsed`
  (> 365 days), `unknown` (no identity, or the order is cancelled).
- `revenue_czk_* = price_* / exchange_rate`. The rate is quoted as order currency per CZK (an EUR
  order carries about 0.0398); a missing rate counts as 1.

**Granted views** (all exclude cancelled orders; all amounts in CZK):

| View | Grain | Backlog |
|---|---|---|
| `v_order_monthly` | month × channel: orders, customers, revenue with/without VAT, product revenue, shipping charged, payment fees, discounts, units, AOV, basket size | #25–#30 |
| `v_order_shipping_monthly` | month × channel × shipping method, free-shipping count | #14, #15 |
| `v_order_payment_monthly` | month × channel × payment method | #15 |
| `v_customer_type_monthly` | month × channel × customer status | #16, #17 |
| `v_new_customers_monthly` | month × channel, first orders only | #19 |
| `v_customer_repeat_purchase` | one row per customer, `md5(customer_key)` surrogate: cohort month, orders, lifetime revenue, average days between orders, acquisition channel | #18 |
| `v_product_sales_monthly` | month × channel × product code/variant/item type, from `items[]` product and product-set lines | #12 |
| `v_product_sales_by_customer_type_monthly` | the same, split by customer status | #11 |
| `v_product_set_component_units_monthly` | units of set components (no revenue) | — |
| `v_product_pair_monthly` | unordered product pairs bought together | #10 |
| `v_sync_health` | `sync_state` | operations |

Revenue in the order-level views comes from the **header** price, which is already net of discount
lines. Product-level revenue sums line prices before order-level discounts.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `ShoptetOrdersSync:ConnectionString` | `""` (Key Vault `ShoptetOrdersSync--ConnectionString`) | Target database. Blank or unparseable = stack unregistered. |
| `ShoptetOrdersSync:MaxPoolSize` | 10 | Dedicated pool size. |
| `ShoptetOrdersSync:Enabled` | true | false = the job returns immediately. |
| `ShoptetOrdersSync:CronExpression` | `30 1 * * *` | Seed cron only. |
| `ShoptetOrdersSync:TimeZone` | `Europe/Prague` | Job time zone. |
| `ShoptetOrdersSync:StoreTimeZone` | `Europe/Prague` | Sets `order_date` and the backfill window edges. An unknown id falls back to UTC. |
| `ShoptetOrdersSync:BackfillFrom` | `2018-01-01` | First creation date backfilled. |
| `ShoptetOrdersSync:BackfillWindowDays` | 31 | Creation-time window size. |
| `ShoptetOrdersSync:BackfillMaxMinutesPerRun` | 240 | Backfill time budget per run. |
| `ShoptetOrdersSync:BatchSize` | 200 | Orders per `SaveChanges`. |
| `ShoptetOrdersSync:RequestsPerSecond` | 3.0 | Shoptet API pacing. |
| `ShoptetOrdersSync:MaxRetryAttempts` | 5 | Retries per request on 429, 5xx or network errors. |
| `ShoptetOrdersSync:RequestTimeoutSeconds` | 60 | Per-request HTTP timeout. |
| `ShoptetOrdersSync:JobTimeoutSeconds` | 18000 | Timeout for the whole run. |
| `ShoptetOrdersSync:WatermarkSafetyMarginHours` | 2 | Overlap re-read before the watermark. |
| `Shoptet:BaseUrl`, `Shoptet:ApiToken` | `https://api.myshoptet.com`, — (shared Shoptet settings) | API host and private token. |

## Runtime facts
- The full history is in `Heblo_V3`: 96 615 orders and 529 740 lines, 2018-09-11 to 2026-09-22,
  loaded by a manual run; `order_fact` is populated and the views answer in about 0.2 s — agent
  memory `project_shoptet_raw_mirror` — 2026-09-24.
- The job has been live in production since 2026-09-24 08:02 UTC, after
  `ShoptetOrdersSync--ConnectionString` was added to `kv-heblo-prod` and the app restarted. The first
  run (Hangfire job 100737) Succeeded in about 2 min with 305 upserted and 89 new orders, matching
  the live Shoptet list 89/89 — agent memory `project_shoptet_raw_mirror` — 2026-09-24.
- On the live store, about 4 sequential requests per second run without a single 429, while 4
  parallel connections (~16 req/s) get 429s straight away — `ShoptetApiThrottle` comment —
  2026-09-22.
- `GET /api/orders/snapshot` answers 403 "Webhook for job:finished is not registered" on this
  store, which is why the backfill walks creation-time windows — `ShoptetOrderBackfillService`
  comment — 2026-09-22.
- Of the store's orders, 69% are guest checkouts with no `customer_guid`, and about 5% (cash-desk)
  have no e-mail. Hence the e-mail-first customer identity — `shoptet_raw_views.sql` — 2026-09-22.
- When checking against Shoptet by hand, the App Setting `Shoptet__ApiToken` works. The Key Vault
  secret `Shoptet--Token` returns invalid-token on the private API — agent memory
  `project_shoptet_raw_mirror` — 2026-09-24.

## Known quirks
- **A failed or timed-out run does not fail the Hangfire job.** Both phases catch their own errors
  and record `FAILED` or `CANCELLED` in `sync_state`. The job then only logs a warning, so Hangfire
  shows Succeeded. Check `v_sync_health`, not Hangfire.
- **A secret that is missing or malformed is silent.** With a blank connection string nothing is
  registered and no log line says so. That is how the job stayed off in production from the merge
  until 2026-09-24.
- **The incremental phase waits for the backfill.** It does not run until the backfill completes.
  An environment whose backfill keeps failing never syncs new orders.
- **Deletions older than 29 days are missed.** If the watermark falls behind the change-log window
  (for example after a month-long outage), the run falls back to the change-time listing. Deletions
  in that gap stay mirrored for good.
- **Cancelled orders keep their line prices.** Shoptet zeroes a cancelled order's header price
  (status −4) but not its `items[]` prices. Anything that sums `order_item` must filter
  `is_cancelled` itself, as every product view does.
- **Wholesale classification depends on a hand-seeded table.** `wholesale_shipping` was seeded from
  `docs/integrations/shoptet-api.md` §7, because Shoptet lists only the shipping methods that are
  active today. A new wholesale shipping method must be added there, or its orders count as
  `eshop_mo`.
- **Sets are counted once.** A set counts once, under its own code (e.g. SA010), in
  `v_product_sales_monthly`. Its component units are only in
  `v_product_set_component_units_monthly`. Adding the two double-counts. This is a different rule
  from Heblo's own sales expansion (`calc-bundle-sales-expansion`).
- **The throttle only paces this sync.** `ShoptetApiThrottle` is shared within this sync only. The
  packing and expedition clients use the same token but are not paced by it, so a heavy backfill
  during working hours can still push the token into 429s for them.
- **`order_fact` can go stale.** If its refresh fails, the error is logged and the run still counts
  as successful, so Metabase keeps the previous snapshot until a later run refreshes it.
- **Customer status is computed, not stored.** Changing the 365-day rule is a one-line edit in
  `order_fact`, then re-running the views SQL. No re-ingest is needed.
- **Migrations and views are manual:** `dotnet ef database update --context ShoptetOrdersDbContext`,
  then
  `psql -v ON_ERROR_STOP=1 -f backend/src/Anela.Heblo.Persistence.ShoptetOrders/Sql/shoptet_raw_views.sql`
  (idempotent; drops and rebuilds `order_fact` with CASCADE, then re-grants). It must be re-run
  after every migration.
- **The admin enable toggle and later cron changes do nothing.** As with the other adapter-hosted
  jobs, the admin enable toggle is not consulted, and a cron changed in config after the first seed
  is ignored. Use `ShoptetOrdersSync:Enabled`.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Analytics/ShoptetOrdersAnalyticsServiceCollectionExtensions.cs` — the connection-string gate and the HTTP client
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Analytics/ShoptetOrdersSyncJob.cs` — job id, cron, timeout, no retry
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Analytics/ShoptetOrdersSyncService.cs` — backfill vs incremental, `order_fact` refresh
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Analytics/ShoptetOrderBackfillService.cs` — creation-time windows, cursor, time budget
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Analytics/ShoptetOrderIncrementalSyncService.cs` — change log, deletions, cold start, watermark
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Analytics/ShoptetOrderMapper.cs` — header and line mapping, roll-ups
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Analytics/ShoptetOrderStore.cs` — line replacement, tracker clearing
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Analytics/ShoptetOrderAnalyticsClient.cs`, `ShoptetApiThrottle.cs`, `ShoptetPaging.cs` — endpoints, retries, pacing, strict paging
- `backend/src/Anela.Heblo.Persistence.ShoptetOrders/ShoptetOrdersDbContext.cs` — tables and indexes
- `backend/src/Anela.Heblo.Persistence.ShoptetOrders/Sql/shoptet_raw_views.sql` — `order_fact` rules, views, reference data, grants
- `backend/tools/Anela.Heblo.ShoptetOrdersBackfill/Program.cs` — manual runner
- `docs/integrations/shoptet-api.md` — order shape, set contract, wholesale shipping list
