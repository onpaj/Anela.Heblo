-- =============================================================================
-- flexi_raw read layer for Metabase
-- =============================================================================
-- Idempotent. Run after `dotnet ef database update --context AnalyticsDbContext`
-- has created the flexi_raw schema, and again after any change to this file:
--
--   psql "$ConnectionStrings__Production" \
--     -f backend/src/Anela.Heblo.Persistence.Analytics/Sql/flexi_raw_read_views.sql
--
-- See docs/architecture/metabase.md for the grant model and ADR-007 in
-- docs/architecture/development_guidelines.md for why this schema lives inside Heblo_V3.
--
-- Two rules this file exists to enforce, neither of which OSS Metabase can:
--
--   1. metabase_ro never sees flexi_raw.ledger_entry. It gets month-grain views only, so an
--      ad-hoc GROUP BY in Metabase cannot scan ~680k raw rows on the single vCore that also
--      serves production Heblo.
--   2. metabase_ro never sees payroll. Backlog item #35 is "pozor neveřejné" and OSS Metabase
--      has no row-level security or data sandboxing, so the boundary is a Postgres grant.
--
-- Sign convention: FlexiBee's ucetni-denik carries a positive `sumTuz` plus a debit (MD) and a
-- credit (DAL) account. v_posting unpivots each row into its two sides and signs them, so a
-- net figure is always SUM(signed_amount) over the accounts of interest: positive for a cost
-- (class 5), negative for revenue (class 6), and corrections net themselves out.
-- =============================================================================

-- Abort on the first error and apply the whole file or none of it. Without these, a failing DROP
-- (a future cross-schema view depending on v_posting will cause one) leaves psql carrying on with
-- the reporting views already dropped and no grants re-applied -- every Metabase question against
-- flexi_raw breaks until someone notices.
\set ON_ERROR_STOP on
BEGIN;

-- -----------------------------------------------------------------------------
-- Drop first, then recreate. CREATE OR REPLACE VIEW cannot rename or reorder a column, so a
-- straight replace fails the moment a view's column list changes. Dropping also clears the old
-- grants, which the GRANT block at the bottom then re-establishes from scratch — that is the point:
-- the grant state after this script is exactly what this script says, never an accumulation.
-- Dependents before the spine.
-- -----------------------------------------------------------------------------
DROP VIEW IF EXISTS flexi_raw.v_payroll_monthly;
DROP VIEW IF EXISTS flexi_raw.v_shop_revenue_monthly;  -- removed 2026-09-22, see the #36 note below
DROP VIEW IF EXISTS flexi_raw.v_ad_spend_monthly;
DROP VIEW IF EXISTS flexi_raw.v_marketing_spend_monthly;
DROP VIEW IF EXISTS flexi_raw.v_cost_monthly_by_account;
DROP VIEW IF EXISTS flexi_raw.v_cost_monthly_total;
DROP VIEW IF EXISTS flexi_raw.v_posting;

-- -----------------------------------------------------------------------------
-- v_posting — internal spine. Deliberately NOT granted: it is row-grain.
-- -----------------------------------------------------------------------------
CREATE VIEW flexi_raw.v_posting AS
WITH sides AS (
    SELECT
        e.flexi_id,
        e.entry_date,
        e.cost_center,
        e.document_type,
        e.contact,
        e.description,
        e.raw_payload,
        'MD'::text            AS side,
        e.account_debit       AS account,
        e.amount              AS signed_amount,
        e.raw_payload -> 'mdUcet' -> 0 ->> 'nazev' AS account_name
    FROM flexi_raw.ledger_entry e
    UNION ALL
    SELECT
        e.flexi_id,
        e.entry_date,
        e.cost_center,
        e.document_type,
        e.contact,
        e.description,
        e.raw_payload,
        'DAL'::text           AS side,
        e.account_credit      AS account,
        -e.amount             AS signed_amount,
        e.raw_payload -> 'dalUcet' -> 0 ->> 'nazev' AS account_name
    FROM flexi_raw.ledger_entry e
)
SELECT
    s.*,
    date_trunc('month', s.entry_date)::date AS month,
    -- Personnel cost (52x) plus the balance-sheet accounts that settle it: employees (331),
    -- other employee liabilities (333), receivables from employees (335), social/health
    -- institutions (336) and employee income tax (342). Anything touching one of these is
    -- payroll for the purposes of the metabase_ro boundary.
    (s.account LIKE '52%'
     OR s.account LIKE '331%'
     OR s.account LIKE '333%'
     OR s.account LIKE '335%'
     OR s.account LIKE '336%'
     OR s.account LIKE '342%') AS is_payroll
FROM sides s
WHERE s.account IS NOT NULL;

COMMENT ON VIEW flexi_raw.v_posting IS
    'Internal: ledger rows unpivoted to one signed row per accounting side. Row-grain, never granted to metabase_ro.';

-- -----------------------------------------------------------------------------
-- v_cost_monthly_total — backlog #1, monthly operating cost by cost centre.
-- -----------------------------------------------------------------------------
-- NOTE: "total" here means total EXCLUDING personnel cost. #1 (total monthly costs) and #35
-- (payroll is confidential) cannot both be served to the same audience; this view answers #1
-- for everyone and leaves #35 to v_payroll_monthly, which is granted to nobody.
CREATE VIEW flexi_raw.v_cost_monthly_total AS
SELECT
    p.month,
    p.cost_center,
    SUM(p.signed_amount)::numeric(18, 2) AS net_cost,
    count(*)                             AS posting_count
FROM flexi_raw.v_posting p
WHERE p.account LIKE '5%'
  AND NOT p.is_payroll
GROUP BY p.month, p.cost_center;

COMMENT ON VIEW flexi_raw.v_cost_monthly_total IS
    'Backlog #1. Monthly class-5 cost by cost centre, EXCLUDING personnel cost (see v_payroll_monthly).';

-- -----------------------------------------------------------------------------
-- v_cost_monthly_by_account — backlog #1 drill-down and the base for #38-#43.
-- -----------------------------------------------------------------------------
CREATE VIEW flexi_raw.v_cost_monthly_by_account AS
SELECT
    p.month,
    p.cost_center,
    p.account,
    min(p.account_name)                  AS account_name,
    SUM(p.signed_amount)::numeric(18, 2) AS net_cost,
    count(*)                             AS posting_count
FROM flexi_raw.v_posting p
WHERE p.account LIKE '5%'
  AND NOT p.is_payroll
GROUP BY p.month, p.cost_center, p.account;

COMMENT ON VIEW flexi_raw.v_cost_monthly_by_account IS
    'Backlog #1 drill-down. Monthly class-5 cost by cost centre and account, excluding personnel cost.';

-- -----------------------------------------------------------------------------
-- v_marketing_spend_monthly — backlog #38-#43.
-- -----------------------------------------------------------------------------
-- The spec expected to group these by accounting template. FlexiBee's ucetni-denik has no
-- předkontace field at all (38 properties, none of them it), so flexi_raw.ledger_entry
-- .accounting_template is structurally always NULL. The analytic dimension that does exist,
-- and that Anela actually books marketing against, is the ACCOUNT. Supplier is carried
-- alongside because 518030 "Marketing-Externiste" pools graphics, photography, PR and
-- influencers into one account — the supplier is the only thing that tells them apart.
CREATE VIEW flexi_raw.v_marketing_spend_monthly AS
SELECT
    p.month,
    p.account,
    min(p.account_name) AS account_name,
    CASE
        WHEN p.account = '518033' THEN 'performance'
        WHEN p.account = '518034' THEN 'brand'
        WHEN p.account = '518035' THEN 'trhy'
        WHEN p.account = '518036' THEN 'darky'
        WHEN p.account = '518030' THEN 'externiste'
        WHEN p.account = '518031' THEN 'pr'
        WHEN p.account = '518032' THEN 'socialni-site'
        WHEN p.account = '518004' THEN 'ostatni'
        WHEN p.account = '501004' THEN 'material'
        WHEN p.account = '501002' THEN 'vyrobky-na-reklamu'
        ELSE 'ostatni'
    END                                  AS category,
    p.cost_center,
    p.contact                            AS supplier,
    SUM(p.signed_amount)::numeric(18, 2) AS net_cost,
    count(*)                             AS posting_count
FROM flexi_raw.v_posting p
WHERE p.account IN (
        '518030', '518031', '518032', '518033', '518034', '518035', '518036', '518004',
        '501002', '501004'
      )
  AND NOT p.is_payroll
GROUP BY p.month, p.account, p.cost_center, p.contact;

COMMENT ON VIEW flexi_raw.v_marketing_spend_monthly IS
    'Backlog #38-#43. Monthly marketing spend by account, category and supplier. Grouped by account, not by accounting template: ucetni-denik carries no předkontace.';

-- -----------------------------------------------------------------------------
-- v_ad_spend_monthly — backlog #31-#33 (S-klik / Google Ads / Meta).
-- -----------------------------------------------------------------------------
-- Ad spend reaches the ledger as received invoices. The three channels are the same three
-- suppliers the marketing-performance feature already tracks (appsettings.json ->
-- MarketingPerformance:Channels); that config keys them by supplier VAT id, and the VAT ids are
-- named below so the two mappings can be checked against each other.
--
-- Why this matches on the contact LABEL rather than on a key:
--   * flexi_raw.contact.vatin is structurally always NULL — the SDK's ContactListRequest asks
--     FlexiBee for kod/nazev/... but never for `ic`/`dic`, so the VAT id never arrives.
--   * raw_payload->>'firma@ref' carries the contact id but is present on only a minority of rows.
--   * Joining flexi_raw.contact on "<code>: <name>" looked clean but silently dropped 110 715.76
--     Kč across 24 postings (0.74% of all ad spend): where FlexiBee returns no `firma@showAs`,
--     ledger_entry.contact falls back to the bare `nazFirmy`, which carries no code — and the
--     address book spells Meta "Meta Platforma" while those invoices say "Meta Platforms".
--
-- So both spellings are matched explicitly, anchored at the start. The anchoring matters: an
-- unanchored '%seznam.cz%' also matches private e-mail addresses used as a counterparty name
-- (two such postings exist), which would attribute them to S-klik.
--
-- Verified against the full 2020-2026 load on 2026-09-22: 14 994 549.54 Kč, 2022-11 to 2026-09.
CREATE VIEW flexi_raw.v_ad_spend_monthly AS
SELECT
    p.month,
    CASE
        WHEN p.contact LIKE 'META: %'     OR p.contact LIKE 'Meta Platform%'  THEN 'meta'    -- DIČ IE9692928F
        WHEN p.contact LIKE 'GOOGLE: %'   OR p.contact LIKE 'Google Ireland%' THEN 'google'  -- DIČ IE6388047V
        WHEN p.contact LIKE 'SEZNAMCZ: %' OR p.contact LIKE 'Seznam.cz,%'     THEN 'sklik'   -- DIČ CZ26168685
    END                                  AS channel,
    p.contact                            AS supplier,
    p.account,
    min(p.account_name)                  AS account_name,
    SUM(p.signed_amount)::numeric(18, 2) AS net_cost,
    count(*)                             AS posting_count
FROM flexi_raw.v_posting p
WHERE (p.contact LIKE 'META: %'     OR p.contact LIKE 'Meta Platform%'
    OR p.contact LIKE 'GOOGLE: %'   OR p.contact LIKE 'Google Ireland%'
    OR p.contact LIKE 'SEZNAMCZ: %' OR p.contact LIKE 'Seznam.cz,%')
  AND p.account LIKE '5%'
  AND NOT p.is_payroll
GROUP BY p.month, 2, p.contact, p.account;

COMMENT ON VIEW flexi_raw.v_ad_spend_monthly IS
    'Backlog #31-#33. Monthly ad spend by channel. Matched on the supplier label, not on a key: flexi_raw.contact.vatin is never populated - see the note in flexi_raw_read_views.sql.';

-- -----------------------------------------------------------------------------
-- Backlog #36 (Dobruška shop revenue) — NO VIEW, deliberately.
-- -----------------------------------------------------------------------------
-- The shop is its own cost centre, PRODEJNA, and a v_shop_revenue_monthly grouping class-6
-- postings by that cost centre is the obvious shape. It does not work: across the whole
-- 2020-2026 load PRODEJNA carries exactly ONE class-6 posting, for 0.00 Kč. The cost centre is
-- used for the shop's costs only (payroll 52x, material 501, services 518, settlements); all
-- 111.6M Kč of revenue is booked to cost centre C. The shop's takings are not separated in the
-- ledger at all, so #36 cannot be answered from flexi_raw as Flexi is currently coded.
--
-- A view returning one 0.00 row would be worse than no view: it reads as "the shop earned
-- nothing" rather than "this question has no answer here". Answering #36 needs a change in how
-- Flexi books shop revenue (a cost centre on the revenue side), not a change here.

-- -----------------------------------------------------------------------------
-- v_payroll_monthly — backlog #35. RESTRICTED: granted to nobody.
-- -----------------------------------------------------------------------------
-- Deliberately left ungranted. Giving Andrea access means a dedicated Postgres role and a
-- second Metabase data source connecting as that role -- see docs/architecture/metabase.md.
CREATE VIEW flexi_raw.v_payroll_monthly AS
SELECT
    p.month,
    p.cost_center,
    p.account,
    min(p.account_name)                  AS account_name,
    SUM(p.signed_amount)::numeric(18, 2) AS net_cost,
    count(*)                             AS posting_count
FROM flexi_raw.v_posting p
WHERE p.is_payroll
GROUP BY p.month, p.cost_center, p.account;

COMMENT ON VIEW flexi_raw.v_payroll_monthly IS
    'Backlog #35 (pozor neveřejné). Monthly personnel cost. Granted to NO role -- do not add metabase_ro.';

-- =============================================================================
-- Grants
-- =============================================================================
-- Deliberately NOT granted: flexi_raw.ledger_entry, contact, department,
-- accounting_template, sync_state, v_posting and v_payroll_monthly.
-- There is no GRANT ... ON ALL TABLES IN SCHEMA flexi_raw here, and there must never be one.
--
-- Start from nothing rather than from a hand-maintained deny-list. metabase_ro holds a blanket
-- SELECT across Heblo_V3.public, which is the signature of a GRANT ... ON ALL TABLES setup; if
-- whoever ran it also set ALTER DEFAULT PRIVILEGES, every CREATE VIEW above would be granted to
-- metabase_ro at creation and a newly added restricted view would leak silently. These two
-- statements make the "exactly what this script says" claim in the header actually true, and mean
-- a new restricted object is private by default instead of needing someone to remember a REVOKE.
ALTER DEFAULT PRIVILEGES IN SCHEMA flexi_raw REVOKE ALL ON TABLES FROM metabase_ro;
REVOKE ALL ON ALL TABLES IN SCHEMA flexi_raw FROM metabase_ro;

GRANT USAGE ON SCHEMA flexi_raw TO metabase_ro;

GRANT SELECT ON flexi_raw.v_cost_monthly_total      TO metabase_ro;
GRANT SELECT ON flexi_raw.v_cost_monthly_by_account TO metabase_ro;
GRANT SELECT ON flexi_raw.v_marketing_spend_monthly TO metabase_ro;
GRANT SELECT ON flexi_raw.v_ad_spend_monthly        TO metabase_ro;

-- Belt and braces on top of the blanket REVOKE above: these are the objects whose exposure would
-- actually matter, spelled out so the intent survives a careless edit.
REVOKE ALL ON flexi_raw.ledger_entry        FROM metabase_ro;
REVOKE ALL ON flexi_raw.contact             FROM metabase_ro;
REVOKE ALL ON flexi_raw.department          FROM metabase_ro;
REVOKE ALL ON flexi_raw.accounting_template FROM metabase_ro;
REVOKE ALL ON flexi_raw.sync_state          FROM metabase_ro;
REVOKE ALL ON flexi_raw.v_posting           FROM metabase_ro;
REVOKE ALL ON flexi_raw.v_payroll_monthly   FROM metabase_ro;

COMMIT;
