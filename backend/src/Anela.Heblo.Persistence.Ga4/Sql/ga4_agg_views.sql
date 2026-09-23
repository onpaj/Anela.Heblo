-- =============================================================================
-- ga4_agg read views for Metabase
-- =============================================================================
-- Idempotent: safe to run repeatedly, in any environment, in any order.
-- Run it after the EF migration has created the ga4_agg tables:
--
--   psql "$CONNECTION_STRING" -v ON_ERROR_STOP=1 -f ga4_agg_views.sql
--
-- metabase_ro is granted USAGE on the schema and SELECT on these views only —
-- never on the underlying tables. Two reasons (see _specs/00-CONTEXT.md):
--   * load — ad-hoc GROUP BY over 250k raw rows hits the same single vCore that
--     serves production Heblo; these views are month grain and bounded;
--   * confidentiality — OSS Metabase has collection permissions only, no row
--     level security, so the boundary has to be enforced here in Postgres.
-- =============================================================================

-- Dropped and rebuilt rather than CREATE OR REPLACE'd. Replacing a view cannot change a
-- column's type, so a revision that swaps SUM(bigint) (numeric) for a plain bigint column fails
-- with "cannot change data type of view column". Dropping first keeps this script re-runnable
-- against any earlier version of itself. The grants at the end are reapplied every run, so
-- dropping does not leave metabase_ro locked out.
DROP VIEW IF EXISTS ga4_agg.v_monthly_conversion;
DROP VIEW IF EXISTS ga4_agg.v_monthly_articles;
DROP VIEW IF EXISTS ga4_agg.v_monthly_landing_pages;
DROP VIEW IF EXISTS ga4_agg.v_monthly_traffic_by_channel;
DROP VIEW IF EXISTS ga4_agg.v_monthly_traffic;

-- -----------------------------------------------------------------------------
-- #9 — total site traffic, with month-over-month and year-over-year comparison
-- -----------------------------------------------------------------------------
-- Reads traffic_monthly, asked of GA4 at month grain, NOT rolled up from the
-- daily tables. GA4 figures do not decompose:
--   * sessions summed from daily or per-channel rows run 0.0-3.8% above the
--     ungrouped total (measured June-August 2026);
--   * users summed across days run ~34% above it (August 2026: 29,194 summed
--     against GA4's own 21,746), because GA4 de-duplicates users over whatever
--     period it is asked about and a visitor returning on three days is counted
--     three times.
-- Only a report asked for the month itself matches what the GA4 UI shows.
CREATE OR REPLACE VIEW ga4_agg.v_monthly_traffic AS
WITH monthly AS (
    SELECT
        m.month,
        m.sessions,
        m.total_users,
        m.new_users,
        m.screen_page_views          AS page_views,
        m.engaged_sessions,
        m.user_engagement_seconds,
        COALESCE(d.days_with_data, 0) AS days_with_data,
        -- Derived from the month itself, never from the LEFT JOIN: the six tables have
        -- independent watermarks, so traffic_total_daily can lag or fail while
        -- traffic_monthly has a row. Reading expected_days out of the subquery made both
        -- sides NULL for such a month, and NULL IS DISTINCT FROM NULL is false, so the
        -- month reported itself complete.
        EXTRACT(DAY FROM (m.month + INTERVAL '1 month - 1 day'))::int AS expected_days
    FROM ga4_agg.traffic_monthly m
    LEFT JOIN (
        SELECT date_trunc('month', date)::date AS month,
               COUNT(*)                        AS days_with_data
        FROM ga4_agg.traffic_total_daily
        GROUP BY 1
    ) d ON d.month = m.month
)
SELECT
    m.month,
    m.sessions,
    -- Distinct users as GA4 counted them for the whole month. Deliberately NOT a sum of
    -- traffic_total_daily.total_users, which counts a returning visitor once per day
    -- (August 2026: 29,194 summed, against GA4's 21,746 for the month).
    m.total_users,
    m.new_users,
    m.page_views,
    m.engaged_sessions,
    -- Bounce rate is derived, never stored: it is a ratio, and a stored ratio
    -- invites an AVG() that weights a 3-session day like a 3000-session day.
    CASE WHEN m.sessions > 0
         THEN ROUND(1.0 - (m.engaged_sessions::numeric / m.sessions), 4) END   AS bounce_rate,
    CASE WHEN m.sessions > 0
         THEN ROUND(m.user_engagement_seconds::numeric / m.sessions, 1) END    AS avg_engagement_seconds_per_session,
    m.days_with_data,
    -- the month before
    pm.sessions                                                                AS sessions_prev_month,
    CASE WHEN pm.sessions > 0
         THEN ROUND((m.sessions - pm.sessions)::numeric * 100 / pm.sessions, 2) END AS sessions_mom_pct,
    -- the same month a year earlier
    py.sessions                                                                AS sessions_same_month_last_year,
    CASE WHEN py.sessions > 0
         THEN ROUND((m.sessions - py.sessions)::numeric * 100 / py.sessions, 2) END AS sessions_yoy_pct,
    py.total_users                                                             AS total_users_same_month_last_year,
    CASE WHEN py.total_users > 0
         THEN ROUND((m.total_users - py.total_users)::numeric * 100 / py.total_users, 2) END AS total_users_yoy_pct,
    -- A partial month (the current one, and June 2023 when the property was
    -- created on the 29th) must not be read as a decline.
    (m.days_with_data IS DISTINCT FROM m.expected_days)                        AS is_partial_month
FROM monthly m
LEFT JOIN monthly pm ON pm.month = m.month - INTERVAL '1 month'
LEFT JOIN monthly py ON py.month = m.month - INTERVAL '1 year'
ORDER BY m.month DESC;

COMMENT ON VIEW ga4_agg.v_monthly_traffic IS
'Backlog #9 — total site traffic per month with MoM and YoY comparison. Source: GA4 Data API asked at month grain, so users are GA4''s own monthly de-duplicated count and every figure matches the GA4 UI. Do not rebuild this from v_monthly_traffic_by_channel: GA4 totals do not decompose.';

-- -----------------------------------------------------------------------------
-- Channel breakdown — shape only, never totals (#20 and follow-ups)
-- -----------------------------------------------------------------------------
CREATE OR REPLACE VIEW ga4_agg.v_monthly_traffic_by_channel AS
WITH monthly AS (
    SELECT
        date_trunc('month', date)::date          AS month,
        channel_group,
        SUM(sessions)                            AS sessions,
        SUM(total_users)                         AS channel_users,
        SUM(new_users)                           AS channel_new_users,
        SUM(engaged_sessions)                    AS engaged_sessions
    FROM ga4_agg.traffic_daily
    GROUP BY 1, 2
)
SELECT
    m.month,
    m.channel_group,
    m.sessions,
    -- Deliberately not called total_users: correct for one channel, wrong the
    -- moment it is summed across channels.
    m.channel_users,
    m.channel_new_users,
    m.engaged_sessions,
    ROUND(m.sessions::numeric * 100 / NULLIF(SUM(m.sessions) OVER (PARTITION BY m.month), 0), 2) AS pct_of_month_sessions,
    pm.sessions                                  AS sessions_prev_month,
    py.sessions                                  AS sessions_same_month_last_year,
    CASE WHEN py.sessions > 0
         THEN ROUND((m.sessions - py.sessions)::numeric * 100 / py.sessions, 2) END AS sessions_yoy_pct
FROM monthly m
LEFT JOIN monthly pm ON pm.month = m.month - INTERVAL '1 month' AND pm.channel_group = m.channel_group
LEFT JOIN monthly py ON py.month = m.month - INTERVAL '1 year'  AND py.channel_group = m.channel_group
ORDER BY m.month DESC, m.sessions DESC;

COMMENT ON VIEW ga4_agg.v_monthly_traffic_by_channel IS
'Sessions per default channel group per month. Use for the shape of acquisition, not for totals — channel sessions sum 0.0-3.8% above the property total, and channel users must never be summed across days. Totals live in v_monthly_traffic.';

-- -----------------------------------------------------------------------------
-- #8 — most-viewed landing pages per month, ranked
-- -----------------------------------------------------------------------------
-- landing_page_daily is capped to the top 100 landing pages per day (the cap in
-- force is on ga4_agg.sync_state.top_n_per_day). The head of the ranking is
-- exact; the tail is not, and the sessions here will not add up to
-- v_monthly_traffic. That is the cap, not a bug.
CREATE OR REPLACE VIEW ga4_agg.v_monthly_landing_pages AS
WITH monthly AS (
    SELECT
        date_trunc('month', date)::date          AS month,
        landing_page,
        SUM(sessions)                            AS sessions,
        SUM(engaged_sessions)                    AS engaged_sessions
    FROM ga4_agg.landing_page_daily
    -- "(other)" is not a page. GA4 substitutes it in a high-cardinality dimension once a report
    -- exceeds its row limit, so it arrives as a landing page that never existed and would sit in
    -- the ranking as a fake entry. The raw row is deliberately kept in the table — its sessions
    -- are real traffic and belong in any total — it is only excluded from the ranking.
    WHERE landing_page <> '(other)'
    GROUP BY 1, 2
),
ranked AS (
    SELECT
        m.*,
        ROW_NUMBER() OVER (PARTITION BY m.month ORDER BY m.sessions DESC, m.landing_page) AS rank_in_month
    FROM monthly m
)
SELECT
    r.month,
    r.rank_in_month,
    r.landing_page,
    r.sessions,
    r.engaged_sessions,
    CASE WHEN r.sessions > 0
         THEN ROUND(1.0 - (r.engaged_sessions::numeric / r.sessions), 4) END AS bounce_rate,
    pm.sessions                                  AS sessions_prev_month,
    pm.rank_in_month                             AS rank_prev_month,
    py.sessions                                  AS sessions_same_month_last_year,
    py.rank_in_month                             AS rank_same_month_last_year,
    CASE WHEN py.sessions > 0
         THEN ROUND((r.sessions - py.sessions)::numeric * 100 / py.sessions, 2) END AS sessions_yoy_pct
FROM ranked r
LEFT JOIN ranked pm ON pm.month = r.month - INTERVAL '1 month' AND pm.landing_page = r.landing_page
LEFT JOIN ranked py ON py.month = r.month - INTERVAL '1 year'  AND py.landing_page = r.landing_page
ORDER BY r.month DESC, r.rank_in_month;

COMMENT ON VIEW ga4_agg.v_monthly_landing_pages IS
'Backlog #8 — landing pages ranked by sessions per month, with previous-month and year-ago rank and sessions. Built from a top-100-per-day capture, so the ranking head is exact and the session sum is not a site total.';

-- -----------------------------------------------------------------------------
-- #37 — most-read articles per month
-- -----------------------------------------------------------------------------
CREATE OR REPLACE VIEW ga4_agg.v_monthly_articles AS
WITH monthly AS (
    SELECT
        date_trunc('month', date)::date          AS month,
        page_path,
        -- The title can change over a month; keep the one from the busiest day.
        (ARRAY_AGG(page_title ORDER BY screen_page_views DESC))[1] AS page_title,
        SUM(screen_page_views)                   AS page_views,
        SUM(sessions)                            AS sessions
    FROM ga4_agg.page_daily
    -- Not a page; see the note in v_monthly_landing_pages. Confirmed live: the 39-month backfill
    -- produced exactly one such row (2024-02-18, 582 views).
    WHERE page_path <> '(other)'
    GROUP BY 1, 2
),
ranked AS (
    SELECT
        m.*,
        ROW_NUMBER() OVER (PARTITION BY m.month ORDER BY m.page_views DESC, m.page_path) AS rank_in_month
    FROM monthly m
)
SELECT
    r.month,
    r.rank_in_month,
    r.page_path,
    r.page_title,
    r.page_views,
    r.sessions,
    pm.page_views                                AS page_views_prev_month,
    pm.rank_in_month                             AS rank_prev_month,
    py.page_views                                AS page_views_same_month_last_year,
    py.rank_in_month                             AS rank_same_month_last_year,
    CASE WHEN py.page_views > 0
         THEN ROUND((r.page_views - py.page_views)::numeric * 100 / py.page_views, 2) END AS page_views_yoy_pct
FROM ranked r
LEFT JOIN ranked pm ON pm.month = r.month - INTERVAL '1 month' AND pm.page_path = r.page_path
LEFT JOIN ranked py ON py.month = r.month - INTERVAL '1 year'  AND py.page_path = r.page_path
ORDER BY r.month DESC, r.rank_in_month;

COMMENT ON VIEW ga4_agg.v_monthly_articles IS
'Backlog #37 — pages ranked by views per month (filter page_path to the article prefix in Metabase, or set Ga4Sync:PagePathPrefixes to restrict at ingest). Top 100 pages per day are captured.';

-- -----------------------------------------------------------------------------
-- #7 — visits versus completed orders
-- -----------------------------------------------------------------------------
-- ***************************************************************************
-- READ THIS BEFORE COMPARING THESE NUMBERS TO THE ERP. THEY WILL NOT MATCH,
-- AND THAT IS NOT A BUG.
--
-- transactions counts GA4 `purchase` events, which fire when the browser
-- reaches the thank-you page. Against the ERP's order book that is wrong in
-- four directions at once:
--
--   1. TOO HIGH — it counts orders that were later cancelled, returned, or
--      never paid. GA4 never hears about what happens after checkout.
--   2. TOO HIGH — a customer who reloads or bookmarks the thank-you page can
--      fire the event twice.
--   3. TOO LOW  — orders taken by phone, e-mail or in person never touch the
--      site, so GA4 cannot see them at all.
--   4. TOO LOW  — a shopper who blocks tracking, or declines consent, buys
--      without ever producing a purchase event.
--
-- The sessions denominator has its own gap: it includes GA4's behavioural
-- modelling for consent-denied traffic. Measured against the BigQuery export
-- for June-August 2026, GA4's session count runs 57-65% above a naive distinct
-- (user_pseudo_id, ga_session_id) count of the raw events, because 26-28% of
-- exported events carry no identifiers at all. Transactions and revenue, by
-- contrast, reconciled to the exact unit in all three months.
--
-- So: this rate is a reliable TREND for the website, and a reliable comparison
-- of one month against another. It is not the company's conversion rate, and
-- it must never be reconciled against an invoice count. For the real order
-- book use shoptet_raw / flexi_raw once those land.
-- ***************************************************************************
CREATE OR REPLACE VIEW ga4_agg.v_monthly_conversion AS
WITH sessions AS (
    -- Sessions come from traffic_monthly, the same source as v_monthly_traffic, so the two views
    -- never disagree on a dashboard. Summing traffic_total_daily instead would be off by a
    -- handful of sessions a month (34,024 vs 34,022 for August 2026) and invite a bug report.
    SELECT m.month,
           m.sessions,
           COALESCE(d.days_with_data, 0) AS days_with_data,
           EXTRACT(DAY FROM (m.month + INTERVAL '1 month - 1 day'))::int AS expected_days
    FROM ga4_agg.traffic_monthly m
    LEFT JOIN (
        SELECT date_trunc('month', date)::date AS month, COUNT(*) AS days_with_data
        FROM ga4_agg.traffic_total_daily GROUP BY 1
    ) d ON d.month = m.month
),
orders AS (
    SELECT date_trunc('month', date)::date AS month,
           SUM(transactions)               AS transactions,
           SUM(purchase_revenue)           AS purchase_revenue
    FROM ga4_agg.conversions_daily
    GROUP BY 1
),
monthly AS (
    SELECT
        s.month,
        s.sessions,
        s.days_with_data,
        s.expected_days,
        -- Deliberately NOT COALESCE(..., 0). conversions_daily carries its own watermark, so a
        -- month it has not reached yet has no row here at all. Folding that to 0 renders as a
        -- genuine 0.000% conversion rate — a cliff on the #7 trend chart indistinguishable from
        -- a catastrophic month. NULL propagates through every ratio below and reads as a gap.
        o.transactions,
        o.purchase_revenue
    FROM sessions s
    LEFT JOIN orders o ON o.month = s.month
)
SELECT
    m.month,
    m.sessions,
    m.transactions,
    m.purchase_revenue,
    CASE WHEN m.sessions > 0
         THEN ROUND(m.transactions::numeric * 100 / m.sessions, 3) END AS conversion_rate_pct,
    CASE WHEN m.transactions > 0
         THEN ROUND(m.purchase_revenue / m.transactions, 2) END        AS avg_order_value,
    pm.sessions             AS sessions_prev_month,
    pm.transactions         AS transactions_prev_month,
    CASE WHEN pm.sessions > 0
         THEN ROUND(pm.transactions::numeric * 100 / pm.sessions, 3) END AS conversion_rate_pct_prev_month,
    py.sessions             AS sessions_same_month_last_year,
    py.transactions         AS transactions_same_month_last_year,
    CASE WHEN py.sessions > 0
         THEN ROUND(py.transactions::numeric * 100 / py.sessions, 3) END AS conversion_rate_pct_same_month_last_year,
    CASE WHEN py.transactions > 0
         THEN ROUND((m.transactions - py.transactions)::numeric * 100 / py.transactions, 2) END AS transactions_yoy_pct,
    (m.days_with_data IS DISTINCT FROM m.expected_days)                              AS is_partial_month
FROM monthly m
LEFT JOIN monthly pm ON pm.month = m.month - INTERVAL '1 month'
LEFT JOIN monthly py ON py.month = m.month - INTERVAL '1 year'
ORDER BY m.month DESC;

COMMENT ON VIEW ga4_agg.v_monthly_conversion IS
'Backlog #7 — GA4 sessions against GA4 purchase events per month. WILL NOT MATCH THE ERP: purchase events count cancelled and unpaid orders, can double-fire on a thank-you-page reload, and miss every phone/e-mail order and every consent-blocked shopper. Valid as a website trend, never as the company conversion rate. See the block comment in ga4_agg_views.sql.';

COMMENT ON COLUMN ga4_agg.v_monthly_conversion.transactions IS
'GA4 purchase events, NOT ERP orders. Includes cancelled/unpaid, excludes offline. Do not reconcile against invoices.';

COMMENT ON COLUMN ga4_agg.v_monthly_conversion.conversion_rate_pct IS
'transactions / sessions, both measured by GA4. A trend line, not the business conversion rate.';

-- =============================================================================
-- Grants — views only, never the raw tables
-- =============================================================================
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'metabase_ro') THEN
        EXECUTE 'GRANT USAGE ON SCHEMA ga4_agg TO metabase_ro';

        -- Revoke first, then grant. ALL TABLES covers views too, so this both clears any
        -- earlier blanket grant on the raw tables and makes the grant list below the only
        -- thing metabase_ro can read. Granting before the revoke would be a no-op.
        EXECUTE 'REVOKE ALL ON ALL TABLES IN SCHEMA ga4_agg FROM metabase_ro';
        EXECUTE 'GRANT SELECT ON ga4_agg.v_monthly_traffic            TO metabase_ro';
        EXECUTE 'GRANT SELECT ON ga4_agg.v_monthly_traffic_by_channel TO metabase_ro';
        EXECUTE 'GRANT SELECT ON ga4_agg.v_monthly_landing_pages      TO metabase_ro';
        EXECUTE 'GRANT SELECT ON ga4_agg.v_monthly_articles           TO metabase_ro';
        EXECUTE 'GRANT SELECT ON ga4_agg.v_monthly_conversion         TO metabase_ro';
        RAISE NOTICE 'ga4_agg: granted metabase_ro SELECT on the five v_ views.';
    ELSE
        RAISE NOTICE 'ga4_agg: role metabase_ro does not exist here; skipping grants.';
    END IF;
END $$;
