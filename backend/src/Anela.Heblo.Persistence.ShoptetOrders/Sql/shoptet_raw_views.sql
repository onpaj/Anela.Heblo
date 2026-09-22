-- =============================================================================
-- shoptet_raw — reference data, read views and Metabase grants
-- =============================================================================
-- Idempotent. Run after the EF migration has created order / order_item / sync_state:
--
--   psql "$CONN" -v ON_ERROR_STOP=1 \
--     -f backend/src/Anela.Heblo.Persistence.ShoptetOrders/Sql/shoptet_raw_views.sql
--
-- Design notes that the SQL below depends on and that are easy to get wrong:
--
--  * Revenue is taken from the order header (price_with_vat), not from summing item lines.
--    Shoptet zeroes the header price of a cancelled order but leaves its item prices intact,
--    and the header price is already net of discount-coupon / volume-discount lines.
--    Item-level views therefore have to exclude cancelled orders explicitly.
--  * exchange_rate is quoted as "order currency per CZK" (an EUR order carries ~0.0398), so the
--    CZK amount is price / exchange_rate. CZK orders carry 1.0 and are unaffected.
--  * A month means a month in the store's timezone: order_date is already Europe/Prague.
--  * Only the v_* views are granted to metabase_ro. order_fact and the reference tables stay
--    ungranted; the views run with the owner's privileges, so Metabase still reads them fine.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. Reference data
-- -----------------------------------------------------------------------------

-- Shoptet's wholesale split gives VO customers their own shipping methods. That is the ONLY
-- marker a wholesale order carries: salesChannelGuid is the same "E-shop" channel for retail and
-- wholesale alike, and the top-level company field is null even on company orders.
-- GET /api/eshop?include=shippingMethods returns only the methods that are active TODAY
-- (3 wholesale, 8 retail as of 2026-09-22), so the historical methods below cannot be rediscovered
-- from the API and are seeded from docs/integrations/shoptet-api.md §7.
CREATE TABLE IF NOT EXISTS shoptet_raw.wholesale_shipping (
    shipping_guid text PRIMARY KEY,
    method_name   text NOT NULL,
    is_active     boolean NOT NULL DEFAULT true,
    note          text
);

INSERT INTO shoptet_raw.wholesale_shipping (shipping_guid, method_name, is_active, note) VALUES
    ('389ce5b4-40f1-11ea-beb1-002590dad85e', 'PPL (do ruky)',                          true,  'VO'),
    ('b7e787c5-011d-11ec-a39f-002590dc5efc', 'GLS (do ruky)',                          true,  'VO'),
    ('389ce19e-40f1-11ea-beb1-002590dad85e', 'Osobní odběr v Dobrušce - po dohodě',    true,  'VO'),
    ('389cea0b-40f1-11ea-beb1-002590dad85e', 'Zásilkovna Z-Point',                     false, 'VO, retired'),
    ('83372e07-9a86-11ea-beb1-002590dad85e', 'PPL ParcelShop (vyzvednutí na pobočce)', false, 'VO, retired'),
    ('2fd96b91-1508-11eb-933a-002590dad85e', 'PPL Export (doručení do zahraničí)',     false, 'VO, retired'),
    ('bbbe7223-4ea8-11ec-a39f-002590dc5efc', 'GLS Export (doručení do zahraničí)',     false, 'VO, retired')
ON CONFLICT (shipping_guid) DO NOTHING;

-- GET /api/sales-channels, anela.cz, 2026-09-22. Kept as reference so Metabase can name a channel
-- guid; the MO/VO/prodejna split does NOT come from here (see order_fact).
CREATE TABLE IF NOT EXISTS shoptet_raw.sales_channel (
    sales_channel_guid text PRIMARY KEY,
    channel_id         integer,
    name               text NOT NULL,
    channel_type       text
);

INSERT INTO shoptet_raw.sales_channel (sales_channel_guid, channel_id, name, channel_type) VALUES
    ('0199be14-cf30-7071-8002-7ac7a4338548',  3, 'E-shop',   'online_store'),
    ('0199c897-3102-7d13-8552-6f5470438844',  6, 'Heureka',  'system'),
    ('0199c897-3102-7d13-8552-6f6125e494c6',  9, 'Aukro',    'system'),
    ('0199c897-3103-7790-be2a-e22a0ff80bb2', 12, 'Admin',    'system'),
    ('019eab6a-7f98-716f-889c-f99d15219194', 13, 'anela.cz', 'in_store'),
    ('019eab6a-8025-72f2-bb38-900d72f97a9f', 25, 'Trhy',     'in_store'),
    -- Present on ~1% of cash-desk orders between 2025-03 and 2026-05 but no longer returned by
    -- GET /api/sales-channels — a deleted in-store register. cash_desk_order is true on all of
    -- them, so they classify as prodejna regardless.
    ('019eab6a-7fed-7021-a070-45c5424398cc', NULL, 'Prodejna (zrušený kanál)', 'in_store')
ON CONFLICT (sales_channel_guid) DO NOTHING;

-- -----------------------------------------------------------------------------
-- 2. order_fact — the single place the business rules live (NOT granted)
-- -----------------------------------------------------------------------------
--
-- CHANNEL
--   prodejna  cash_desk_order = true (the in-store registers: anela.cz, Trhy)
--   eshop_vo  shipping method is a wholesale method
--   eshop_mo  everything else
--
-- CUSTOMER IDENTITY
--   lower(email), falling back to 'guid:' || customer_guid when the order has no e-mail.
--   Email rather than customer_guid because 69% of orders are guest checkouts with no
--   customer_guid at all, while only ~5% (the cash-desk ones) have no e-mail.
--
-- CUSTOMER STATUS — the business rule the owners can change here and nowhere else
--   new               this is the identity's first ever non-cancelled order
--   returning         a previous order exists, the last one within the past 12 months
--   returning_lapsed  a previous order exists, but more than 12 months ago
--   unknown           no identity at all (cash-desk orders) — excluded from the customer views
--
--   "new" is first-ever-purchase, the standard e-commerce definition. To switch to a
--   "no order in the last 12 months counts as new" rule, fold returning_lapsed into new here;
--   nothing downstream has to change and no re-ingest is needed.
CREATE OR REPLACE VIEW shoptet_raw.order_fact AS
WITH base AS (
    SELECT
        o.code,
        o.order_date,
        date_trunc('month', o.order_date::timestamp)::date          AS order_month,
        o.status_id,
        o.status_name,
        o.status_id = -4                                            AS is_cancelled,
        o.is_paid,
        CASE
            WHEN o.cash_desk_order THEN 'prodejna'
            WHEN w.shipping_guid IS NOT NULL THEN 'eshop_vo'
            ELSE 'eshop_mo'
        END                                                         AS channel,
        o.sales_channel_guid,
        sc.name                                                     AS sales_channel_name,
        o.source_id,
        o.source_name,
        o.shipping_guid,
        o.shipping_name,
        o.payment_method_name,
        o.billing_method_name,
        o.customer_guid,
        o.customer_email,
        COALESCE(o.customer_email, CASE WHEN o.customer_guid IS NOT NULL
                                        THEN 'guid:' || o.customer_guid END)  AS customer_key,
        o.billing_company,
        o.billing_country_code,
        o.currency_code,
        -- A missing rate means "no conversion known". Left as NULL it would propagate through
        -- every division below, so sum(revenue) silently skipped the order while count(*) still
        -- counted it — revenue quietly undershooting order count with nothing to signal it.
        -- CZK orders carry 1.0, and 1.0 is also the right assumption for a rate Shoptet omitted.
        coalesce(o.exchange_rate, 1) AS exchange_rate,
        o.price_with_vat,
        o.price_without_vat,
        o.product_price_with_vat,
        o.product_price_without_vat,
        o.shipping_price_with_vat,
        o.billing_price_with_vat,
        o.discount_with_vat,
        o.product_units,
        -- exchange_rate is order-currency-per-CZK, so dividing converts to CZK.
        o.price_with_vat    / NULLIF(coalesce(o.exchange_rate, 1), 0) AS revenue_czk_with_vat,
        o.price_without_vat / NULLIF(coalesce(o.exchange_rate, 1), 0) AS revenue_czk_without_vat
    FROM shoptet_raw."order" o
    LEFT JOIN shoptet_raw.wholesale_shipping w ON w.shipping_guid = o.shipping_guid
    LEFT JOIN shoptet_raw.sales_channel sc     ON sc.sales_channel_guid = o.sales_channel_guid
),
-- The order sequence is computed over non-cancelled orders only, in its own CTE rather than as a
-- CASE around the window function: a cancelled order must not consume sequence position 1 and
-- push a customer's real first purchase into "returning".
customer_orders AS (
    SELECT code, customer_key, order_date
    FROM base
    WHERE NOT is_cancelled AND customer_key IS NOT NULL
),
sequenced AS (
    SELECT
        code,
        row_number() OVER (PARTITION BY customer_key ORDER BY order_date, code) AS customer_order_seq,
        lag(order_date)  OVER (PARTITION BY customer_key ORDER BY order_date, code) AS previous_order_date
    FROM customer_orders
)
SELECT
    b.*,
    s.customer_order_seq,
    s.previous_order_date,
    CASE
        WHEN b.customer_key IS NULL OR b.is_cancelled THEN 'unknown'
        WHEN s.previous_order_date IS NULL THEN 'new'
        WHEN b.order_date - s.previous_order_date > 365 THEN 'returning_lapsed'
        ELSE 'returning'
    END AS customer_status
FROM base b
LEFT JOIN sequenced s ON s.code = b.code;

-- -----------------------------------------------------------------------------
-- 3. Read views (granted)
-- -----------------------------------------------------------------------------

-- #25–#30 — orders, revenue and average order value per month and channel.
CREATE OR REPLACE VIEW shoptet_raw.v_order_monthly AS
SELECT
    order_month,
    channel,
    count(*)                                              AS orders,
    count(DISTINCT customer_key)                          AS customers,
    sum(revenue_czk_with_vat)                             AS revenue_with_vat_czk,
    sum(revenue_czk_without_vat)                          AS revenue_without_vat_czk,
    sum(product_price_with_vat / NULLIF(exchange_rate,0)) AS product_revenue_with_vat_czk,
    sum(shipping_price_with_vat / NULLIF(exchange_rate,0))AS shipping_charged_with_vat_czk,
    sum(billing_price_with_vat / NULLIF(exchange_rate,0)) AS payment_fee_with_vat_czk,
    sum(discount_with_vat / NULLIF(exchange_rate,0))      AS discount_with_vat_czk,
    sum(product_units)                                    AS units,
    avg(revenue_czk_with_vat)                             AS avg_order_value_with_vat_czk,
    avg(product_units)                                    AS avg_basket_units
FROM shoptet_raw.order_fact
WHERE NOT is_cancelled
GROUP BY order_month, channel;

-- #14, #15 — orders and the delivery charge actually collected, by shipping method.
-- Note: this is what the customer PAID for delivery. What the carrier charged Anela is not in
-- Shoptet, so the shipping subsidy (#14) is this figure minus the carrier cost from the Flexi
-- ledger — a cross-schema question, deliberately out of scope here.
CREATE OR REPLACE VIEW shoptet_raw.v_order_shipping_monthly AS
SELECT
    order_month,
    channel,
    COALESCE(shipping_name, '(bez dopravy)')               AS shipping_name,
    shipping_guid,
    count(*)                                               AS orders,
    sum(revenue_czk_with_vat)                              AS revenue_with_vat_czk,
    sum(shipping_price_with_vat / NULLIF(exchange_rate,0)) AS shipping_charged_with_vat_czk,
    avg(shipping_price_with_vat / NULLIF(exchange_rate,0)) AS avg_shipping_charged_with_vat_czk,
    count(*) FILTER (WHERE shipping_price_with_vat = 0)     AS orders_with_free_shipping
FROM shoptet_raw.order_fact
WHERE NOT is_cancelled
GROUP BY order_month, channel, shipping_name, shipping_guid;

-- #15 — orders by payment method.
CREATE OR REPLACE VIEW shoptet_raw.v_order_payment_monthly AS
SELECT
    order_month,
    channel,
    COALESCE(payment_method_name, '(neuvedeno)')           AS payment_method_name,
    count(*)                                               AS orders,
    sum(revenue_czk_with_vat)                              AS revenue_with_vat_czk,
    avg(revenue_czk_with_vat)                              AS avg_order_value_with_vat_czk,
    sum(billing_price_with_vat / NULLIF(exchange_rate,0))  AS payment_fee_with_vat_czk
FROM shoptet_raw.order_fact
WHERE NOT is_cancelled
GROUP BY order_month, channel, payment_method_name;

-- #16, #17 — revenue and basket size, new vs returning.
CREATE OR REPLACE VIEW shoptet_raw.v_customer_type_monthly AS
SELECT
    order_month,
    channel,
    customer_status,
    count(*)                        AS orders,
    count(DISTINCT customer_key)    AS customers,
    sum(revenue_czk_with_vat)       AS revenue_with_vat_czk,
    avg(revenue_czk_with_vat)       AS avg_order_value_with_vat_czk,
    sum(product_units)              AS units,
    avg(product_units)              AS avg_basket_units
FROM shoptet_raw.order_fact
WHERE NOT is_cancelled
GROUP BY order_month, channel, customer_status;

-- #19 — how many customers bought for the first time, and what that first order was worth.
CREATE OR REPLACE VIEW shoptet_raw.v_new_customers_monthly AS
SELECT
    order_month,
    channel,
    count(*)                    AS new_customers,
    sum(revenue_czk_with_vat)   AS first_order_revenue_with_vat_czk,
    avg(revenue_czk_with_vat)   AS avg_first_order_value_with_vat_czk,
    avg(product_units)          AS avg_first_order_units
FROM shoptet_raw.order_fact
WHERE NOT is_cancelled AND customer_status = 'new'
GROUP BY order_month, channel;

-- #18 — repeat purchasing per customer, with the acquisition month as a cohort key.
-- Customer grain rather than month grain, but bounded by the number of identities (~60k),
-- not by the number of orders.
--
-- The grain is a SURROGATE, not the e-mail address. customer_key is the raw address, and this is
-- the one granted view whose grain is per-customer — selecting it directly would handed anyone
-- with the Metabase connection an exportable ~60k-row list of customer e-mails with lifetime
-- spend attached, which is exactly what withholding the raw tables above is meant to prevent.
-- md5 keeps every question this view exists to answer (cohorts, repeat rate, order cadence, LTV
-- distribution), because those group and count identities rather than read them.
-- To identify an individual customer, join order_fact directly — it is not granted, so that stays
-- a deliberate act by someone with database access.
CREATE OR REPLACE VIEW shoptet_raw.v_customer_repeat_purchase AS
SELECT
    md5(customer_key)                                            AS customer_id,
    min(order_date)                                              AS first_order_date,
    date_trunc('month', min(order_date)::timestamp)::date        AS cohort_month,
    max(order_date)                                              AS last_order_date,
    count(*)                                                     AS orders,
    count(*) > 1                                                 AS is_repeat_customer,
    sum(revenue_czk_with_vat)                                    AS lifetime_revenue_with_vat_czk,
    avg(revenue_czk_with_vat)                                    AS avg_order_value_with_vat_czk,
    sum(product_units)                                           AS lifetime_units,
    CASE WHEN count(*) > 1
         THEN (max(order_date) - min(order_date))::numeric / (count(*) - 1)
    END                                                          AS avg_days_between_orders,
    max(channel) FILTER (WHERE customer_order_seq = 1)           AS acquisition_channel
FROM shoptet_raw.order_fact
WHERE NOT is_cancelled AND customer_key IS NOT NULL
GROUP BY customer_key;

-- #12 — product and size ranking.
-- SET COUNTING: a product set is counted as ONE product, under its own SKU (e.g. SA010), because
-- that is the only line the money is attached to — Shoptet returns set components in completion[]
-- with no price at all. Component units are available separately in
-- v_product_set_component_units_monthly; they must never be added to this view's units, or every
-- set would be counted twice.
CREATE OR REPLACE VIEW shoptet_raw.v_product_sales_monthly AS
SELECT
    f.order_month,
    f.channel,
    i.product_code,
    max(i.product_name)                                  AS product_name,
    i.variant_name,
    i.item_type,
    count(DISTINCT i.order_code)                         AS orders,
    sum(i.amount)                                        AS units,
    sum(i.line_price_with_vat / NULLIF(f.exchange_rate,0))    AS revenue_with_vat_czk,
    sum(i.line_price_without_vat / NULLIF(f.exchange_rate,0)) AS revenue_without_vat_czk
FROM shoptet_raw.order_item i
JOIN shoptet_raw.order_fact f ON f.code = i.order_code
WHERE NOT f.is_cancelled
  AND i.source_array = 'items'
  AND i.item_type IN ('product', 'product-set')
GROUP BY f.order_month, f.channel, i.product_code, i.variant_name, i.item_type;

-- #11 — product ranking split by customer type.
CREATE OR REPLACE VIEW shoptet_raw.v_product_sales_by_customer_type_monthly AS
SELECT
    f.order_month,
    f.channel,
    f.customer_status,
    i.product_code,
    max(i.product_name)                                  AS product_name,
    i.variant_name,
    count(DISTINCT i.order_code)                         AS orders,
    sum(i.amount)                                        AS units,
    sum(i.line_price_with_vat / NULLIF(f.exchange_rate,0)) AS revenue_with_vat_czk
FROM shoptet_raw.order_item i
JOIN shoptet_raw.order_fact f ON f.code = i.order_code
WHERE NOT f.is_cancelled
  AND i.source_array = 'items'
  AND i.item_type IN ('product', 'product-set')
GROUP BY f.order_month, f.channel, f.customer_status, i.product_code, i.variant_name;

-- The other half of the set-counting decision: what actually left the shelf.
-- amount on a product-set-item is ALREADY the order total for that component (per-set quantity ×
-- set count) — it must not be multiplied by the parent's quantity. Revenue is deliberately absent:
-- Shoptet attributes none to a component.
CREATE OR REPLACE VIEW shoptet_raw.v_product_set_component_units_monthly AS
SELECT
    f.order_month,
    f.channel,
    i.product_code,
    max(i.product_name)                 AS product_name,
    i.variant_name,
    max(parent.product_code)            AS set_code,
    count(DISTINCT i.order_code)        AS orders,
    sum(i.amount)                       AS units
FROM shoptet_raw.order_item i
JOIN shoptet_raw.order_fact f  ON f.code = i.order_code
LEFT JOIN shoptet_raw.order_item parent
       ON parent.order_code = i.order_code
      AND parent.item_id = i.parent_item_id
      AND parent.source_array = 'items'
WHERE NOT f.is_cancelled
  AND i.item_type = 'product-set-item'
GROUP BY f.order_month, f.channel, i.product_code, i.variant_name;

-- #10 — which products are bought together. Unordered pairs, one row per pair per month/channel.
CREATE OR REPLACE VIEW shoptet_raw.v_product_pair_monthly AS
-- product_name is deliberately NOT part of the DISTINCT: it is descriptive, not identifying, and
-- one product_code carrying two spellings within a single order would otherwise yield two rows for
-- that order and count the pair twice. Names are looked up separately.
WITH order_products AS (
    SELECT DISTINCT
        f.order_month,
        f.channel,
        i.order_code,
        i.product_code
    FROM shoptet_raw.order_item i
    JOIN shoptet_raw.order_fact f ON f.code = i.order_code
    WHERE NOT f.is_cancelled
      AND i.source_array = 'items'
      AND i.item_type IN ('product', 'product-set')
      AND i.product_code IS NOT NULL
),
product_name AS (
    SELECT product_code, max(product_name) AS product_name
    FROM shoptet_raw.order_item
    WHERE product_code IS NOT NULL
    GROUP BY product_code
)
SELECT
    a.order_month,
    a.channel,
    a.product_code            AS product_code_a,
    na.product_name           AS product_name_a,
    b.product_code            AS product_code_b,
    nb.product_name           AS product_name_b,
    count(*)                  AS orders_together
FROM order_products a
JOIN order_products b
  ON b.order_code = a.order_code
 AND b.product_code > a.product_code
LEFT JOIN product_name na ON na.product_code = a.product_code
LEFT JOIN product_name nb ON nb.product_code = b.product_code
GROUP BY a.order_month, a.channel, a.product_code, na.product_name,
         b.product_code, nb.product_name;

-- Operational view so the sync can be watched from Metabase without granting the raw tables.
CREATE OR REPLACE VIEW shoptet_raw.v_sync_health AS
SELECT
    entity_name,
    watermark,
    last_run_started_at,
    last_run_finished_at,
    last_run_status,
    last_run_rows_fetched,
    last_run_rows_upserted,
    backfill_cursor,
    backfill_completed,
    last_error_message
FROM shoptet_raw.sync_state;

-- -----------------------------------------------------------------------------
-- 4. Grants — views only, never the raw tables
-- -----------------------------------------------------------------------------
-- Raw rows stay ungranted for two reasons: ad-hoc GROUP BYs over ~97k orders and ~440k lines hit
-- the same single vCore that serves production Heblo, and OSS Metabase has no row-level security
-- to fall back on. A view runs with its owner's privileges, so metabase_ro needs nothing else.

GRANT USAGE ON SCHEMA shoptet_raw TO metabase_ro;

GRANT SELECT ON shoptet_raw.v_order_monthly                            TO metabase_ro;
GRANT SELECT ON shoptet_raw.v_order_shipping_monthly                   TO metabase_ro;
GRANT SELECT ON shoptet_raw.v_order_payment_monthly                    TO metabase_ro;
GRANT SELECT ON shoptet_raw.v_customer_type_monthly                    TO metabase_ro;
GRANT SELECT ON shoptet_raw.v_new_customers_monthly                    TO metabase_ro;
GRANT SELECT ON shoptet_raw.v_customer_repeat_purchase                 TO metabase_ro;
GRANT SELECT ON shoptet_raw.v_product_sales_monthly                    TO metabase_ro;
GRANT SELECT ON shoptet_raw.v_product_sales_by_customer_type_monthly   TO metabase_ro;
GRANT SELECT ON shoptet_raw.v_product_set_component_units_monthly      TO metabase_ro;
GRANT SELECT ON shoptet_raw.v_product_pair_monthly                     TO metabase_ro;
GRANT SELECT ON shoptet_raw.v_sync_health                              TO metabase_ro;

-- Take back anything a blanket grant may already have handed out...
REVOKE ALL ON shoptet_raw."order"                 FROM metabase_ro;
REVOKE ALL ON shoptet_raw.order_item              FROM metabase_ro;
REVOKE ALL ON shoptet_raw.sync_state              FROM metabase_ro;
REVOKE ALL ON shoptet_raw.wholesale_shipping      FROM metabase_ro;
REVOKE ALL ON shoptet_raw.sales_channel           FROM metabase_ro;
REVOKE ALL ON shoptet_raw.order_fact              FROM metabase_ro;

-- ...and stop a FUTURE one reaching these tables at all. A REVOKE run now cannot affect a grant
-- issued later, which is what the line above was mistakenly documented as doing.
ALTER DEFAULT PRIVILEGES IN SCHEMA shoptet_raw REVOKE ALL ON TABLES FROM metabase_ro;
