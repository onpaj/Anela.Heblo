# Shoptet orders mirror — `Heblo_V3.shoptet_raw`

Mirrors Shoptet order headers and line items into their own schema so Metabase can answer the
reporting backlog (#10–#12, #14–#19, #25–#30) without going near the Shoptet API or the Heblo UI.

**Reports live in Metabase, not in Heblo.** There is no MediatR handler, controller, generated
client or React page for any of this, and there should not be one.

---

## 1. Why not `public.IssuedInvoices`

70,536 invoice rows already sit in `Heblo_V3` and Metabase already reads them, but they carry no
channel dimension, no line items and no customer identity — the three things every one of those
backlog items needs. `Price` is also with VAT, `PriceC` is always 0, and roughly 20% of rows are
unsynced. The Shoptet order is the record that has all of it.

---

## 2. What is stored

| Table | Grain | Notes |
|---|---|---|
| `shoptet_raw."order"` | one row per Shoptet order code | header plus the line rollups the month views need, plus `raw_payload` (the verbatim detail JSON) |
| `shoptet_raw.order_item` | one row per line, keyed `(order_code, line_no)` | every `items[]` line **and** the `product-set-item` components from `completion[]` |
| `shoptet_raw.sync_state` | one row per sync entity | watermark, last-run status, backfill cursor |
| `shoptet_raw.wholesale_shipping` | reference | the shipping GUIDs that mark an order as VO |
| `shoptet_raw.sales_channel` | reference | channel guid → name, from `GET /api/sales-channels` |

`raw_payload` exists so that recovering a field nobody thought to map is a SQL query rather than
a seven-hour re-backfill.

Entities: `backend/src/Anela.Heblo.Persistence.ShoptetOrders/Entities/`.
Sync: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Analytics/`.

### Line rollups on the header

`product_price_*`, `shipping_price_*`, `billing_price_*` (the payment-method surcharge) and
`discount_*` are summed from the `items[]` lines at ingest so a month-grain view never has to join
~440k item rows. They add up exactly to the header `price_with_vat`.

`product_units` counts `product` and `product-set` lines only — not `gift` (always priced 0) and
not the set components (see §5).

---

## 3. How it is filled

### Backfill

`GET /api/orders/snapshot` — the documented full export — **cannot be used**: it answers 403
`required-webhook-registration` until a `job:finished` webhook is registered, and this store has
none (docs/integrations/shoptet-api.md §3.11).

So `ShoptetOrderBackfillService` walks **creation-time windows** (31 days by default) from
`BackfillFrom` forward, pages `GET /api/orders?creationTimeFrom=…&creationTimeTo=…` for the codes,
then fetches one `GET /api/orders/{code}` per order because the list endpoint carries no items.

- Creation time is what makes it resumable: it never changes, so a window's contents and its
  paging are stable. The unfiltered list is *not* ordered by creation time.
- The cursor is persisted to `sync_state.backfill_cursor` after every window, and the run stops at
  `BackfillMaxMinutesPerRun`. An interrupted run resumes at the next window.
- Idempotent: re-running a window re-upserts the same order codes and replaces their lines.

Run it with the supervised tool rather than waiting for the nightly job to grind through it a few
hours at a time. The tool runs the **same orchestrator** as the Hangfire job — backfill while the
history is unfinished, incremental catch-up once it is — so it doubles as "run the sync now":

```bash
ShoptetOrdersSync__ConnectionString='Host=heblosql.postgres.database.azure.com;Port=5432;Database=Heblo_V3;User ID=…;Password=…;' \
Shoptet__ApiToken='<premium private API token>' \
ShoptetOrdersSync__BackfillFrom='2018-01-01' \
ShoptetOrdersSync__RequestsPerSecond=4 \
dotnet run --project backend/tools/Anela.Heblo.ShoptetOrdersBackfill
```

~97k detail calls at 4 req/s is roughly seven hours. **Run it off-hours**: the Postgres server is a
shared 1-vCore burstable instance, and the Shoptet API token is the same one the warehouse's
packing and expedition flows use.

### Nightly incremental

`ShoptetOrderIncrementalSyncService`, wired as the Hangfire recurring job `shoptet-orders-sync`
at **`30 1 * * *` Europe/Prague** (the 02:00–09:00 band is full of the existing daily imports and
`flexi-analytics-sync` holds 03:00).

It reads `GET /api/orders/changes?from=<watermark − 2h>`, which reports **edits and deletions**.
A changed order is re-fetched and upserted with its lines replaced; a deleted one is removed.
`GET /api/orders?changeTimeFrom=` would never notice a deletion — the order just stops appearing.

The change log keeps a guaranteed 30 days. If the watermark is older than that, the run falls back
to the order list and logs a warning that deletions in that window were not covered.

### Configuration

Everything hangs off `ShoptetOrdersSync:*`. **An empty `ShoptetOrdersSync:ConnectionString` leaves
the whole stack unregistered**, so an unconfigured environment is inert — the same gate
`FlexiAdapterServiceCollectionExtensions` uses for `flexi_raw`.

The connection string is a secret and belongs in Key Vault as
`ShoptetOrdersSync--ConnectionString` (staging `kv-heblo-stg`, production `kv-heblo-prod`),
never in App Service settings.

---

## 4. Migrations

Manual, as everywhere in this project.

```bash
dotnet ef migrations script \
  --project backend/src/Anela.Heblo.Persistence.ShoptetOrders/Anela.Heblo.Persistence.ShoptetOrders.csproj \
  --context ShoptetOrdersDbContext --idempotent -o /tmp/shoptet_raw_schema.sql

psql "$CONN" -v ON_ERROR_STOP=1 -f /tmp/shoptet_raw_schema.sql
psql "$CONN" -v ON_ERROR_STOP=1 \
  -f backend/src/Anela.Heblo.Persistence.ShoptetOrders/Sql/shoptet_raw_views.sql
```

The migration history for this context lives in `shoptet_raw."__EFMigrationsHistory"`, separate
from the main application one in `public`.

The views script is idempotent and safe to re-run; run it after every migration.

### If the migration changes before merge

`shoptet_raw` was created in `Heblo_V3` and `Heblo_TST` from the initial migration **while this
branch was still unmerged**, and the backfill was run against it. EF records the migration as
`<timestamp>_InitialShoptetRawSchema` in `shoptet_raw."__EFMigrationsHistory"`, and that timestamp
changes whenever the migration is regenerated — so **any schema change made during review puts the
deployed schema out of step with the migration in `main`**. This already happened once, when a
redundant `order_item.raw_payload` column was dropped.

There is no in-place fix. The recovery is:

```bash
psql "$CONN" -c "DROP SCHEMA shoptet_raw CASCADE;"
# then re-apply the migration script and the views script from §4, and re-run the backfill
```

The backfill is the expensive part (~7 hours), so **settle the schema before re-running it**.
Nothing else depends on the schema — no deployed code reads it, and the Hangfire job stays
unregistered until `ShoptetOrdersSync:ConnectionString` is set — so dropping it is safe at any
point up to that secret being created.

---

## 5. The two decisions that are business rules, not code

### Channel — eshop MO / eshop VO / prodejna

```
prodejna   cash_desk_order = true
eshop_vo   shipping_guid is in shoptet_raw.wholesale_shipping
eshop_mo   everything else
```

`salesChannelGuid` does **not** separate retail from wholesale — a VO order carries the same
"E-shop" channel as a retail one. Shoptet's wholesale split gives VO customers their own shipping
methods, and that is the only marker the order carries. The live API returns only the currently
active methods, so the retired VO ones are seeded into `wholesale_shipping` from
docs/integrations/shoptet-api.md §7; adding a new VO method is one `INSERT`, no re-ingest.

### New vs returning customer

Defined once, in `shoptet_raw.order_fact`:

| Value | Rule |
|---|---|
| `new` | the identity's first ever non-cancelled order |
| `returning` | a previous order exists, the most recent one within the past 365 days |
| `returning_lapsed` | a previous order exists, but more than 365 days ago |
| `unknown` | the order carries no identity at all (cash-desk) |

**Identity key** is `lower(trim(email))`, falling back to `'guid:' || customer_guid` when there is
no e-mail. E-mail rather than `customer_guid` because 69% of orders are guest checkouts with no
`customer_guid`, while only ~5% lack an e-mail (all of them cash-desk).

What it misses: one person with several e-mail addresses counts as several customers, which
over-counts "new"; a shared company address under-counts; a typo creates a phantom new customer;
and prodejna sales can never be attributed at all.

`returning_lapsed` is broken out separately on purpose. To switch to a "no order in the last 12
months counts as new" definition, fold `returning_lapsed` into `new` in that one `CASE` — nothing
downstream changes and no re-ingest is needed.

### Product sets

Both levels are stored faithfully. The counting decision is:

- **Revenue always sits on the `product-set` line.** Shoptet attributes no price to a component at
  all, so component-level revenue is not derivable, not merely inconvenient.
- **`v_product_sales_monthly` counts a set as one product**, under its own SKU (`SA010`), because
  that is the line the money is on and it is what the customer actually chose.
- **`v_product_set_component_units_monthly`** exposes the components as units of their own SKUs,
  with no revenue, for stock and popularity questions.

Never add the two together: every set would be counted twice. And a component's `amount` is
already the order total — multiplying it by the set quantity is a real bug that doubled
quantities on order 126014786.

---

## 6. Read views and Metabase access

All in `shoptet_raw`, all month-grain except `v_customer_repeat_purchase` (customer grain).

| View | Backlog |
|---|---|
| `v_order_monthly` | #25–#30 — orders, revenue, AOV by channel |
| `v_order_shipping_monthly` | #14, #15 — by shipping method, with the delivery charge collected |
| `v_order_payment_monthly` | #15 — by payment method |
| `v_customer_type_monthly` | #16, #17 — revenue and basket size, new vs returning |
| `v_new_customers_monthly` | #19 — first-time buyers and what their first order was worth |
| `v_customer_repeat_purchase` | #18 — repeat rate, lifetime value, cohort month |
| `v_product_sales_monthly` | #12 — product and size ranking |
| `v_product_sales_by_customer_type_monthly` | #11 — product ranking by customer type |
| `v_product_set_component_units_monthly` | set components, units only |
| `v_product_pair_monthly` | #10 — products bought together |
| `v_sync_health` | operational |

Revenue is reported in CZK (`price / exchange_rate`, since `exchange_rate` is quoted as
order-currency-per-CZK) and excludes cancelled orders.

`metabase_ro` gets `USAGE` on the schema and `SELECT` on the `v_*` views only. The raw tables and
`order_fact` stay ungranted: ad-hoc `GROUP BY`s over ~97k orders and ~440k lines hit the same
single vCore that serves production Heblo, and OSS Metabase has no row-level security to fall
back on. A view runs with its owner's privileges, so Metabase needs nothing else.

### Cost

Every `v_*` view recomputes `order_fact`, which window-functions over the whole order table. That
is the thing to watch as the mirror grows: if Metabase use picks up enough that the recompute
starts showing on the shared vCore, turn `order_fact` into a materialised view refreshed at the
end of the nightly sync. Nothing else has to change. `v_product_pair_monthly` is the expensive one
— a self-join over every product line — and is the first candidate for a Metabase question cache.

**#14, shipping subsidy, is only half answerable here.** `v_order_shipping_monthly` gives what the
customer paid for delivery; what the carrier charged Anela is in the Flexi ledger. Joining the two
is a cross-schema question and deliberately out of scope until all three ingestion directions land.

---

## 7. Gotchas worth keeping

- `itemPrice` is the **line total**, `unitPrice` is per unit.
- A cancelled order (status `-4`) has `price.withVat` = 0 but keeps its item prices. Header revenue
  is storno-safe; anything summing item lines must filter cancelled orders itself.
- Shoptet stamps times as `+0200`, which `System.Text.Json` rejects, and Npgsql then refuses any
  non-zero offset on a `timestamptz` column. `ShoptetDateTimeOffsetConverter` parses it and the
  mapper normalises with `.ToUniversalTime()`; `order_date` is derived before that so a month still
  means a Prague month.
- `paid`, `vatPayer` and `cashDeskOrder` can be `null`.
- `completion[]` duplicates every `items[]` line — only the `product-set-item` entries are stored.
- A `product-set-item`'s `itemId` is the catalogue id and repeats across orders; `line_no` is what
  makes the primary key unique.
- The backfill reuses one scoped `DbContext` for ~97k orders, so `ShoptetOrderStore` clears the
  change tracker after every batch.
