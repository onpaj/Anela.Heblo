---
name: shoptet-vo-is-a-shipping-method
description: Wholesale orders carry the same salesChannelGuid as retail — the only marker is the wholesale shipping GUID, and the API only lists the currently active ones
metadata:
  type: gotcha
---

On anela.cz, the MO / VO / prodejna split does **not** come from `salesChannelGuid`. A wholesale
order carries the same "E-shop" channel guid (`0199be14-…`) as a retail one, its top-level
`company` field is `null` even for company orders, and there is no `salesChannels` include on
`GET /api/eshop`.

What actually separates them:

- **prodejna** — `cashDeskOrder = true` (also true for both `in_store` sales channels, *and* for a
  deleted register whose guid `GET /api/sales-channels/{guid}` no longer resolves).
- **eshop VO** — the order's `shipping.guid` is one of the **wholesale** shipping methods. The
  store has `settings.wholesaleSplitActive = true`, and `GET /api/eshop?include=shippingMethods`
  returns separate `retail` and `wholesale` groups with different GUIDs for the same carrier.
- **eshop MO** — everything else.

**Why:** the trap is that `GET /api/eshop?include=shippingMethods` returns only the methods active
*today* (3 wholesale, 8 retail as of 2026-09-22). Historical VO orders used four now-retired VO
methods that the API will never mention again, so classifying from a live API call misclassifies
years of history as retail.

**How to apply:** keep the full historical VO GUID list as seeded reference data — in
`shoptet_raw.wholesale_shipping`, sourced from `docs/integrations/shoptet-api.md` §7 — and derive
the channel in the read view, not at ingest, so correcting it is one `INSERT` rather than a
re-backfill. Scale check over the whole history (96,584 orders): 2,873 VO, ~88,573 retail,
~5,138 with no shipping method (cash desk).
