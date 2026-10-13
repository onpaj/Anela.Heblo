---
name: shoptet-order-shape-traps
description: Four Shoptet order-response traps that each produce silently wrong numbers — line vs unit price, zeroed cancelled totals, the +0200 offset, and nullable paid/vatPayer
metadata:
  type: gotcha
---

Verified live against the anela.cz store on 2026-09-22 while building the `shoptet_raw` mirror.
Full detail in `docs/integrations/shoptet-api.md` §3.9. Each of these produces plausible-looking
but wrong output rather than an error:

1. **`itemPrice` is the LINE TOTAL, `unitPrice` is per unit.** Treating `itemPrice` as the unit
   price multiplies every multi-quantity line by its own quantity.
2. **A cancelled order (status `-4`) has `price.withVat` = `0.00` but keeps its
   `items[].itemPrice` values.** Header-level revenue is storno-safe for free; anything that sums
   item lines silently counts cancelled orders as revenue unless it filters status itself.
3. **Times come back as `2026-09-21T20:36:22+0200`** — the basic-format offset. `System.Text.Json`
   only accepts `+02:00` and throws; Npgsql then refuses *any* non-zero offset on a `timestamptz`
   column. Both ends need handling: a custom converter to read, `.ToUniversalTime()` to write.
4. **`paid`, `vatPayer` and `cashDeskOrder` can be `null`**, not just true/false (seen on
   cash-desk orders). A non-nullable `bool` blows up mid-backfill.

**Why:** none of these surface in a small happy-path sample — the first order I looked at had
`amount` 1 (so line == unit price), was not cancelled, and had `paid: true`.

**How to apply:** when reading Shoptet orders, model monetary fields with
`JsonNumberHandling.AllowReadingFromString` (prices are JSON strings, quantities are JSON numbers),
model every boolean as `bool?`, use `ShoptetDateTimeOffsetConverter`, and prefer the header price
over summed item lines for revenue. See [[shoptet-snapshot-needs-webhook]] and
[[shoptet-vo-is-a-shipping-method]].
