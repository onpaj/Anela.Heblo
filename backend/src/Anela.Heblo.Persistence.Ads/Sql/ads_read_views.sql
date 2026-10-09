-- =============================================================================
-- ads — Metabase read views and grants (marketing agents platform, PR C2)
-- =============================================================================
-- Idempotent: safe to run repeatedly, in any environment. Run after the AdsDbContext
-- migration (and, for the blended views, after the shoptet_raw migration):
--
--   psql "$CONNECTION_STRING" -v ON_ERROR_STOP=1 \
--     -f backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql
--
-- Design notes the SQL depends on:
--  * ad_daily_facts holds every level a platform reports. Summing across levels double-counts,
--    so every view reads exactly ONE level: Campaign.
--  * Cost is net of VAT in the ad account's currency. Blended views add up CZK accounts only
--    and flag a day that also had non-CZK spend (has_non_czk_ad_cost) rather than converting
--    at a guessed rate.
--  * Revenue reads shoptet_raw."order" — the base TABLE, not shoptet_raw.order_fact.
--    shoptet_raw_views.sql drops order_fact with CASCADE on every run, which would silently
--    drop any view built on it. The rules below are copied from order_fact; keep them in step:
--      cancelled  = status_id = -4
--      CZK amount = price / exchange_rate (order currency per CZK); a missing rate counts as 1
--      e-shop     = NOT cash_desk_order
--  * Only month-grain views and the (small, PII-free) change log are granted to metabase_ro.
--    v_ads_blended_daily serves Heblo's MCP read handler and stays ungranted.
-- =============================================================================

DROP VIEW IF EXISTS ads.v_ads_blended_monthly;
DROP VIEW IF EXISTS ads.v_ads_blended_daily;
DROP VIEW IF EXISTS ads.v_ads_change_events;
DROP VIEW IF EXISTS ads.v_ads_campaign_monthly;

-- -----------------------------------------------------------------------------
-- Campaign performance per month, platform-reported conversions.
-- -----------------------------------------------------------------------------
CREATE VIEW ads.v_ads_campaign_monthly AS
SELECT
    date_trunc('month', f.date::timestamp)::date                       AS month,
    a.platform,
    a.external_id                                                      AS account_external_id,
    a.name                                                             AS account_name,
    e.external_id                                                      AS campaign_external_id,
    e.name                                                             AS campaign_name,
    e.status                                                           AS campaign_status,
    f.currency,
    count(DISTINCT f.date)                                             AS days_with_data,
    sum(f.impressions)                                                 AS impressions,
    sum(f.clicks)                                                      AS clicks,
    sum(f.cost)                                                        AS cost,
    sum(f.conversions)                                                 AS conversions,
    sum(f.conversion_value)                                            AS conversion_value,
    CASE WHEN sum(f.impressions) > 0
         THEN round(sum(f.clicks)::numeric * 100 / sum(f.impressions), 3) END AS ctr_pct,
    CASE WHEN sum(f.clicks) > 0
         THEN round(sum(f.cost) / sum(f.clicks), 2) END                AS avg_cpc,
    CASE WHEN sum(f.cost) > 0
         THEN round(sum(f.conversion_value) / sum(f.cost), 4) END      AS platform_roas
FROM ads.ad_daily_facts f
JOIN ads.ad_entities e ON e.id = f.entity_id AND e.level = 'Campaign'
JOIN ads.ad_accounts a ON a.id = e.account_id
WHERE a.is_managed
GROUP BY 1, a.platform, a.external_id, a.name, e.external_id, e.name, e.status, f.currency;

COMMENT ON VIEW ads.v_ads_campaign_monthly IS
'Campaign-level ad performance per month as the platforms report it. Conversions are the platform''s own attribution and over-count; compare with v_ads_blended_monthly.';

-- -----------------------------------------------------------------------------
-- Every change made to a managed or unmanaged ad account, Heblo or out-of-band.
-- -----------------------------------------------------------------------------
CREATE VIEW ads.v_ads_change_events AS
SELECT
    c.id,
    c.occurred_at,
    a.platform,
    a.external_id                                   AS account_external_id,
    a.name                                          AS account_name,
    COALESCE(e.level, c.entity_level)               AS entity_level,
    COALESCE(e.external_id, c.entity_external_ref)  AS entity_external_id,
    e.name                                          AS entity_name,
    c.change_type,
    c.actor_kind,
    c.actor,
    c.source,
    c.origin,
    c.matched_execution_id,
    c.old_value,
    c.new_value
FROM ads.ad_change_events c
JOIN ads.ad_accounts a ON a.id = c.account_id
LEFT JOIN ads.ad_entities e ON e.id = c.entity_id;

COMMENT ON VIEW ads.v_ads_change_events IS
'Ad account change history. origin = OutOfBand means the change did not come through an executed Heblo proposal (agency, platform UI, auto-applied recommendation).';

-- -----------------------------------------------------------------------------
-- Blended reality check: real revenue / total ad spend.
-- -----------------------------------------------------------------------------
DO $blended$
BEGIN
    IF to_regclass('shoptet_raw."order"') IS NULL THEN
        RAISE NOTICE 'ads: shoptet_raw."order" does not exist here; skipping the blended views.';
        RETURN;
    END IF;

    EXECUTE $view$
    CREATE VIEW ads.v_ads_blended_daily AS
    WITH ad_cost AS (
        SELECT
            f.date,
            sum(f.cost) FILTER (WHERE f.currency = 'CZK')                              AS ad_cost_czk,
            sum(f.cost) FILTER (WHERE f.currency = 'CZK' AND a.platform = 'GoogleAds') AS google_ads_cost_czk,
            sum(f.cost) FILTER (WHERE f.currency = 'CZK' AND a.platform = 'MetaAds')   AS meta_ads_cost_czk,
            sum(f.cost) FILTER (WHERE f.currency = 'CZK' AND a.platform = 'Sklik')     AS sklik_cost_czk,
            bool_or(f.currency <> 'CZK')                                               AS has_non_czk_ad_cost
        FROM ads.ad_daily_facts f
        JOIN ads.ad_entities e ON e.id = f.entity_id AND e.level = 'Campaign'
        JOIN ads.ad_accounts a ON a.id = e.account_id
        WHERE a.is_managed
        GROUP BY f.date
    ),
    revenue AS (
        SELECT
            o.order_date                                                                AS date,
            count(*)                                                                    AS orders,
            sum(o.price_without_vat / NULLIF(coalesce(o.exchange_rate, 1), 0))          AS revenue_without_vat_czk,
            sum(o.price_without_vat / NULLIF(coalesce(o.exchange_rate, 1), 0))
                FILTER (WHERE NOT o.cash_desk_order)                                    AS eshop_revenue_without_vat_czk
        FROM shoptet_raw."order" o
        WHERE o.status_id <> -4
        GROUP BY o.order_date
    )
    SELECT
        coalesce(c.date, r.date)                        AS date,
        coalesce(c.ad_cost_czk, 0)                      AS ad_cost_czk,
        coalesce(c.google_ads_cost_czk, 0)              AS google_ads_cost_czk,
        coalesce(c.meta_ads_cost_czk, 0)                AS meta_ads_cost_czk,
        coalesce(c.sklik_cost_czk, 0)                   AS sklik_cost_czk,
        coalesce(c.has_non_czk_ad_cost, false)          AS has_non_czk_ad_cost,
        coalesce(r.orders, 0)                           AS orders,
        coalesce(r.revenue_without_vat_czk, 0)          AS revenue_without_vat_czk,
        coalesce(r.eshop_revenue_without_vat_czk, 0)    AS eshop_revenue_without_vat_czk
    FROM ad_cost c
    FULL OUTER JOIN revenue r ON r.date = c.date
    $view$;

    EXECUTE $view$
    CREATE VIEW ads.v_ads_blended_monthly AS
    SELECT
        date_trunc('month', date::timestamp)::date      AS month,
        count(*)                                        AS days_with_data,
        sum(ad_cost_czk)                                AS ad_cost_czk,
        sum(google_ads_cost_czk)                        AS google_ads_cost_czk,
        sum(meta_ads_cost_czk)                          AS meta_ads_cost_czk,
        sum(sklik_cost_czk)                             AS sklik_cost_czk,
        bool_or(has_non_czk_ad_cost)                    AS has_non_czk_ad_cost,
        sum(orders)                                     AS orders,
        sum(revenue_without_vat_czk)                    AS revenue_without_vat_czk,
        sum(eshop_revenue_without_vat_czk)              AS eshop_revenue_without_vat_czk,
        CASE WHEN sum(ad_cost_czk) > 0
             THEN round(sum(revenue_without_vat_czk) / sum(ad_cost_czk), 4) END      AS blended_roas,
        CASE WHEN sum(revenue_without_vat_czk) > 0
             THEN round(sum(ad_cost_czk) * 100 / sum(revenue_without_vat_czk), 2) END AS pno_pct
    FROM ads.v_ads_blended_daily
    GROUP BY 1
    $view$;

    EXECUTE $c$COMMENT ON VIEW ads.v_ads_blended_monthly IS
'Blended reality check: Shoptet revenue without VAT (non-cancelled, CZK) / total campaign-level ad cost of managed CZK accounts. has_non_czk_ad_cost = some spend was in another currency and is NOT included.'$c$;
END
$blended$;

-- =============================================================================
-- Grants — views only, never the raw tables (docs/architecture/metabase.md)
-- =============================================================================
DO $grants$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'metabase_ro') THEN
        EXECUTE 'GRANT USAGE ON SCHEMA ads TO metabase_ro';
        -- Revoke first: ALL TABLES covers views too, so this clears any earlier blanket grant.
        EXECUTE 'REVOKE ALL ON ALL TABLES IN SCHEMA ads FROM metabase_ro';
        EXECUTE 'GRANT SELECT ON ads.v_ads_campaign_monthly TO metabase_ro';
        EXECUTE 'GRANT SELECT ON ads.v_ads_change_events    TO metabase_ro';
        IF to_regclass('ads.v_ads_blended_monthly') IS NOT NULL THEN
            EXECUTE 'GRANT SELECT ON ads.v_ads_blended_monthly TO metabase_ro';
        END IF;
        RAISE NOTICE 'ads: granted metabase_ro SELECT on the v_ads_* Metabase views.';
    ELSE
        RAISE NOTICE 'ads: role metabase_ro does not exist here; skipping grants.';
    END IF;
END
$grants$;
