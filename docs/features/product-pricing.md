# Product Pricing — Shoptet as the Retail Price Source of Truth

## Overview

Retail prices for products, goods, and sets are set and read **live from Shoptet** — there
is no local master price table, no sync job, and no cached copy Heblo trusts over the
e-shop. ABRA Flexi (the ERP) carries its own copy of the same price, kept in step by a
write-through when an operator edits a price in Heblo. The two can still drift — a price
changed directly in Shoptet's own admin, a failed Flexi write, a foreign price type in
Flexi — so a live comparison, a nightly automated check, and an append-only change log
exist to detect and record that drift. Nothing in this feature rolls a divergence back
automatically; the comparison screen is the safety net, not an enforcement mechanism.

This replaces an earlier design (removed on this branch) that synced Shoptet prices into a
local master price table on an hourly job and reconciled conflicts from there. That table,
its sync states, and the hourly job no longer exist. If you find a reference to a "master
price" concept elsewhere in the codebase or docs, it is stale.

## Where it lives

- **Domain** (`Anela.Heblo.Domain/Features/ProductPricing/`): `ProductPriceChangeLog`,
  `IEshopPriceListClient`, `IErpPriceWriter`, `IProductPriceChangeLogRepository`,
  `IProductVatRateProvider`, `VatRateCalculator`.
- **Application** (`Anela.Heblo.Application/Features/ProductPricing/`):
  `PriceComparisonService` (the live comparison), `GetPriceDivergenceReport` and
  `SetProductPrice` use cases, `PriceComparisonDqtAdapter` (bridges into DataQuality).
- **Persistence** (`Anela.Heblo.Persistence/ProductPricing/`): `ProductPriceChangeLogRepository`,
  EF configuration. Migration `20260903130253_AddProductPricing` creates the
  `product_price_change_logs` table — the *only* table this feature owns. There is no
  master-price table.
- **Adapters**: `ShoptetPriceListClient` (`Anela.Heblo.Adapters.ShoptetApi/Pricing/`) reads
  and writes the Shoptet retail price list; the Flexi side reuses the existing ERP price
  read/write clients.
- **API**: `ProductPricingController` — `GET /api/product-pricing/divergence`,
  `PUT /api/product-pricing/prices/{productCode}`. Gated by `Feature.Products_Catalog`
  (write requires `AccessLevel.Write`).
- **Frontend**: `frontend/src/pages/ProductPricingPage.tsx` at `/products/pricing`, backed
  by `PriceDivergenceReport.tsx` and `useProductPricing.ts`.

## The live comparison

`PriceComparisonService.BuildReportAsync` is a pure read-and-compare — it never writes
anywhere. On every call it:

1. Loads all in-scope catalog products (`ProductType.Product`, `Goods`, `Set` — assumption
   A3, only sellable types carry a retail price), deduplicated by product code.
2. Reads current Shoptet prices in bulk (`IEshopPriceListClient.GetPricesWithVatAsync`).
3. Reads current Flexi ERP prices in bulk (`IProductPriceErpClient.GetAllAsync`).
4. Classifies each in-scope product into one `PriceDivergenceKind`.

Classification precedence (most to least urgent — a row that could match more than one
condition gets the first that applies):

| Kind | Meaning |
|---|---|
| `MissingInShoptet` | Shoptet is the source of truth; a product absent from it has no comparison to make at all. |
| `MissingInFlexi` | Shoptet has a price but the ERP read has nothing for this product. |
| `FlexiPriceTypeUnknown` | Both sides have a price, but Flexi's with-VAT figure was derived from an *assumed* price type — see [Flexi price type](#flexi-price-type-and-why-a-write-can-be-refused-outright) below. Reported even when the numbers happen to agree, because that agreement cannot be trusted. |
| `FlexiDiffers` | Both known, prices disagree (outside tolerance). |
| `InAgreement` | Both known, prices match to 2 decimals within tolerance. |

### The 0.01 Flexi rounding tolerance

Flexi stores the price **excluding** VAT (`cenaZakl`) and reconstructs the with-VAT figure
on read as `cena * (100 + vat) / 100`. That does not round-trip exactly — a 190.00 CZK
price can read back as 189.99 purely from rounding, with no real divergence. Without a
tolerance, a large share of the catalogue would report as divergent by a single haléř and
the dashboard tile would be permanently orange. `PriceComparisonService` therefore treats
Shoptet and Flexi prices as in agreement when they round to within **0.01** of each other
(`FlexiRoundTripTolerance`, both sides rounded to 2 decimals first). Shoptet, which stores
the with-VAT price directly, gets no such allowance — it is the exact source of truth.

### Flexi price type, and why a write can be refused outright

Flexi's `cenaZakl` field means different things depending on the item's own price type:

- **`bezDph`** ("without VAT") — `cenaZakl` is the excl-VAT price; the with-VAT figure is
  derived by grossing it up with the item's VAT rate.
- **`sDph`** ("with VAT") — `cenaZakl` **is already** the with-VAT price.
- Unknown/unset — the ERP read did not expose a usable price type at all.

The comparison surfaces this as `FlexiPriceType` (`"bezDph"`, `"sDph"`, or `null`) on every
row so an operator can see it. The **write path is stricter than the read path**: when
setting a price, `SetProductPriceHandler` refuses outright unless the item's Flexi price
type is exactly `bezDph`. Writing the excl-VAT figure into an `sDph` item would silently
under-price it by the VAT rate with no error and no divergence for the comparison to catch
— the exact failure mode a write-through exists to prevent. An unknown price type is
refused for the same reason `PriceComparisonService` already reports it as untrustworthy
(`FlexiPriceTypeUnknown`): writing under an assumption the read side itself rejects would
be incoherent. `sDph` write semantics are also unverified against the live ERP — there is
no sandbox — so the handler declines rather than encode a guess into a live write.

### The VAT band, and why an unrecognised one refuses the write

Flexi reports each item's VAT band in `typszbdphk`. Heblo recognises **two vocabularies**
for it — the enum form the rest of the Flexi adapter uses (`typSzbDph.dphZakl`,
`typSzbDph.dphSniz`, `typSzbDph.dphSniz2`, `typSzbDph.dphOsv`, and their bare `dphZakl`
forms) and the Czech labels (`základní`, `snížená`, `druhá snížená`, `osvobozeno`, with
accent-free spellings), matched case-insensitively after trimming. User query 41's
definition is not visible from this repository, so which vocabulary it actually returns is
not knowable here; recognising both removes the guess in either direction.

- **Read path** (`ProductPriceFlexiDto.Vat`, the comparison screen): an unrecognised band
  falls back to 21%, exactly as before, so the comparison behaves unchanged.
- **Write path** (`ProductPriceFlexiDto.VatRate`, `ProductPriceErp.VatRate`): an
  unrecognised band yields *no* rate. `FlexiProductVatRateProvider` omits the product
  entirely and `SetProductPriceHandler` refuses with `ProductPriceFlexiVatRateUnknown`,
  writing nothing anywhere.

The reason the write is stricter: a wrong VAT rate produces a wrong `cenaZakl`, which Flexi
then reconstructs at its *real* rate — a 12% item priced as if it were 21% ends up ~7%
under-priced in the ERP. The comparison cannot catch it either, because the read applies the
same wrong rate to the same `cenaZakl` and reproduces the requested price, marking the row
`InAgreement`. The provider also never recovers a rate arithmetically from the
with/without-VAT pair: both of those numbers are derived from the band in question, so the
result would be Heblo's own assumption returned as if it were a measurement.

An unrecognised `typszbdphk` value is logged once per run at warning level, raw — that log
line is how we find out which vocabulary Flexi really uses.

## Nightly DQT check and dashboard tile

The comparison also runs as a scheduled data-quality check, wired into the shared DQT
(Data Quality Tests) framework — see `docs/features/data-quality-dqt.md`.

- **Test type**: `DqtTestType.PriceComparison`, implemented by `PriceComparisonDqtComparer`
  (an `IDriftDqtComparer`). A price comparison is a snapshot, not a date-ranged query, so
  the DQT framework's from/to bounds are ignored, the same accommodation the other drift
  comparers make.
- **Job**: `PriceComparisonDqtJob`, Hangfire recurring job `daily-price-comparison-dqt`,
  `0 6 * * *` (06:00 daily, alongside the other DQT checks).
- **Source**: `PriceComparisonDqtAdapter` implements DataQuality's consumer-owned
  `IPriceComparisonSource` contract, translating `PriceComparisonService`'s rows into
  `PriceDivergence` records. `FlexiDiffers`, `MissingInFlexi`, and `FlexiPriceTypeUnknown`
  all count as mismatches; `MissingInShoptet` does not (Shoptet is authoritative — a
  product it has never priced has no comparison to fail) and neither does `InAgreement`.
- **Result shaping**: `DqtDriftResult`'s generic `HebloValue`/`ShoptetValue` columns are
  named for the Heblo-vs-Shoptet checks that came first. For this check, `ShoptetValue`
  carries the Shoptet price and `HebloValue` carries the Flexi price — the frontend labels
  these columns "Shoptet" and "Flexi", not "Heblo", for exactly that reason.
- **Dashboard tile**: `PriceComparisonStatusTile` (tile id `pricecomparisonstatus`, title
  "Kontrola cen"), reads the latest `PriceComparison` run and shows total checked / total
  mismatches with a status color (green/amber/red), drilling down to `/products/pricing`.

## Write-through price edit

`PUT /api/product-pricing/prices/{productCode}` (`SetProductPriceHandler`) writes a new
price to Shoptet, then to Flexi, in that order, with a pre-flight before either write:

1. **Read Shoptet's current price** via the single-product endpoint
   (`GET /api/pricelists/{id}?code=X` — see `docs/integrations/shoptet-api.md`) to record as
   `OldPriceWithVat` in the change log. A product with no row in the retail list cannot be
   priced at all and fails here.
2. **Pre-flight the Flexi leg** — resolve the Flexi ceník item id, confirm its price type is
   `bezDph` (refusing otherwise, see above), and resolve the product's VAT rate from Flexi's
   own VAT band (refusing when that band is unrecognised, see below). All of this
   runs *before* anything is written, on purpose: a missing ceník id, an unsupported price
   type, or a missing VAT rate is knowable up front and guarantees the Flexi leg cannot
   succeed, so discovering it only after Shoptet was already written would manufacture an
   avoidable divergence.
3. **Write Shoptet** (`PATCH /api/pricelists/{id}`, `priceWithVat.price`, never the flat
   `price` field — see the integration doc for the object-vs-scalar 422 gotcha).
4. **Write Flexi** (excl-VAT price, computed from the requested with-VAT price and the
   resolved VAT rate, rounded to 2 decimals, away-from-zero). A successful write evicts the
   Flexi ceník read's 5-minute memory-cache entry (`FlexiProductPriceWriter` →
   `FlexiProductPriceErpClient.CacheKey`). Without that, the refetch the frontend triggers
   immediately after a successful save would read Shoptet live (new price) and Flexi from
   cache (old price), and render a change that fully succeeded as a `FlexiDiffers`
   divergence.

There is no server-side price ceiling — any fixed ceiling would be an arbitrary magic
number. The frontend's own confirmation step (see below) is the only guard against a
mistyped price reaching the live shop.

### Partial failure: no rollback, no retry

If the Flexi write (step 4) fails after the Shoptet write (step 3) already succeeded, the
handler does **not** roll Shoptet back and does **not** retry Flexi. The two systems are
now genuinely divergent, by design: a rollback would itself be a second live write that can
fail independently, and a retry queue is more machinery than a once-daily comparison and a
manual retry justify. The response reports `ProductPriceFlexiWriteFailed`; the frontend
invalidates and refetches the divergence report on this specific error so the row reloads
from the live (now divergent) state instead of continuing to show the value the operator
just typed. The next nightly DQT run — or the operator reopening the pricing screen — is
what actually surfaces the divergence for a human to resolve, typically by re-editing the
price once Flexi is reachable again.

Every other failure mode (product not in Shoptet, Flexi read failing, Flexi item id
unknown, unsupported Flexi price type, unrecognised Flexi VAT band, Shoptet write itself
failing) leaves **nothing** written on either side.

A *failed* Flexi read and a product that genuinely has no ceník item / no recognisable VAT
band are reported as three different codes on purpose (`ProductPriceErpReadFailed`,
`ProductPriceFlexiItemIdUnknown`, `ProductPriceFlexiVatRateUnknown`). Collapsing them, as
an earlier revision did, tells every operator during a Flexi outage to go hunting in Flexi
for a ceník item that is actually there.

### Frontend: inline editing with confirmations

`PriceDivergenceReport.tsx` (rendered by `ProductPricingPage` at `/products/pricing`) is a
single screen — the summary counts, the divergent-only filter, and inline price editing all
live together; there is no separate read-only tab. Whether the pencil/edit affordance shows
at all is gated by `products.catalog.write` (`usePermissionsContext`); read-only viewers see
the full comparison with a banner explaining nothing is written from this view.

Before saving an edited price, the operator is always asked to confirm:

- **Normal edit**: if the new price differs from the current Shoptet price by more than
  **50%**, a confirmation dialog names both numbers and requires an explicit "yes."
- **First price** (row has no existing Shoptet price, e.g. a `MissingInShoptet` row): always
  confirmed, regardless of magnitude — there is no ratio to gate on, and the dialog says
  explicitly that this sets the *first* price directly on the live shop.

Client-side, a draft price below 0.01 is rejected before it ever reaches the network — this
mirrors the backend's own `GreaterThanOrEqualTo(0.01m)` validator, avoiding a round-trip to
two live systems for an obviously invalid amount (an empty field coerces to `0` via
`Number("")`, which would otherwise slip past a naive guard).

A write banner is shown honestly: writers see a warning that saving writes directly to the
live Shoptet store and the live Flexi ERP (neither has a test environment); read-only users
see a banner confirming nothing is written from this screen.

## Append-only change log

Every call to `SetProductPriceHandler` — success, partial failure, or full failure — appends
one `ProductPriceChangeLog` row: product code, old and new price, who made the change, a
timestamp, whether each of the Shoptet and Flexi writes succeeded, and an error message when
applicable. The log is:

- **Append-only** — never updated, never read to decide anything. It exists purely as
  history and as the only durable record of a partial-failure state (Shoptet accepted,
  Flexi did not).
- **Best-effort on top of an already-decided outcome** — if the INSERT itself fails, the
  failure is logged and swallowed rather than surfaced to the caller. Both remote writes (on
  the success path) have already landed by the time the log append runs; letting a failed
  INSERT turn a completed price change into a reported error would be wrong, and on a
  failure path it would mask the real error code.
- **Not a record of edits made outside Heblo** — a price changed directly in Shoptet's own
  admin never touches this log. The live comparison (not this log) is what catches those.
- Always appended with `CancellationToken.None`, not the request's own token: once the
  Shoptet write has landed, this row is the only record of a possible partial-failure state
  and must not be lost to the same cancellation that aborted the Flexi call.

## API summary

| Endpoint | Method | Auth | Description |
|---|---|---|---|
| `/api/product-pricing/divergence` | GET | `Products_Catalog` (read) | Live Shoptet-vs-Flexi comparison. Never writes anywhere. |
| `/api/product-pricing/prices/{productCode}` | PUT | `Products_Catalog` (write) | Write-through price edit: Shoptet, then Flexi, with pre-flight. |

## Error codes (36XX module range)

| Code | Meaning |
|---|---|
| `ProductPriceNotFoundInShoptet` | Product is not in the Shoptet retail price list; nothing written. |
| `ProductPriceFlexiItemIdUnknown` | No Flexi ceník item (or VAT rate) resolvable for the product; nothing written. |
| `ProductPriceShoptetWriteFailed` | The Shoptet write itself failed; nothing written. |
| `ProductPriceFlexiWriteFailed` | Shoptet was written successfully but the Flexi write failed — **the two systems now diverge**. |
| `ProductPriceFlexiPriceTypeUnsupported` | Item's Flexi price type is not `bezDph`; refused outright, nothing written. |
| `ProductPriceFlexiVatRateUnknown` | Flexi's VAT band for the item was not one the adapter recognises, so no rate can be trusted; refused outright, nothing written. |
| `ProductPriceErpReadFailed` | The Flexi read itself failed (outage, timeout, 5xx) — distinct from any fact about the product; nothing written. |

## Known constraints

- No sandbox for either Shoptet or Flexi — every write from this feature hits the live
  e-shop and the live ERP.
- Writes only ever target Flexi items whose price type is `bezDph`. `sDph` items can be
  *read* and compared but never written through this feature.
- No server-side price ceiling; the frontend confirmation dialogs are the only guard.
- A Flexi write failure leaves a permanent divergence until an operator re-edits the price
  or fixes it directly; there is no automated retry or rollback.
