---
name: shoptet-orders-mirror-business-rules
description: The two owner-facing definitions baked into shoptet_raw — first-ever-purchase = "new customer", and a product set counts as one product
metadata:
  type: decision
---

`Heblo_V3.shoptet_raw` mirrors Shoptet orders for Metabase reporting. Two of its rules are business
definitions rather than technical choices, and both are made in one place — the
`shoptet_raw.order_fact` view — so the owners can change them without a re-ingest.

**"Nové vs stálé zákaznice" — new = first ever non-cancelled order.**
Chosen over "no order in the last 12 months" because it is the standard e-commerce definition
(so the number reconciles with GA4 and Shoptet's own reporting) and because a month's count never
moves retroactively. The lapsed case is not thrown away: `customer_status` carries
`new` / `returning` / `returning_lapsed` (> 365 days since the previous order) / `unknown`, so
switching definitions means folding `returning_lapsed` into `new` in one `CASE`.

**Identity key is `lower(trim(email))`**, falling back to `'guid:' || customer_guid`.
Not `customer_guid`: 69% of orders are guest checkouts with no `customer_guid` at all, while only
~5% lack an e-mail (all cash-desk). Known misses: one person with several addresses counts as
several customers (over-counts "new"), a shared company address under-counts, a typo creates a
phantom, and prodejna sales can never be attributed.

**A product set counts as ONE product, under its own SKU.**
Not a choice between two equally good options: Shoptet attributes **no price at all** to a set
component (they live in `completion[]` with no `itemPrice`/`unitPrice`), so component-level
revenue is not derivable. Revenue therefore sits on the `product-set` line, and
`v_product_sales_monthly` ranks by it. Component units are exposed separately in
`v_product_set_component_units_monthly`, with no revenue, for stock questions. Adding the two
together double-counts every set.

**Why:** these three answers change the value of backlog items #10–#12 and #16–#19 materially,
and none of them can be read off the data.

**How to apply:** change them in `shoptet_raw.order_fact` (see
`backend/src/Anela.Heblo.Persistence.ShoptetOrders/Sql/shoptet_raw_views.sql`) and re-run that
script; never in the ingest path. See [[shoptet-vo-is-a-shipping-method]] for the channel rule
and `docs/features/shoptet-orders-mirror.md` for the whole picture.
