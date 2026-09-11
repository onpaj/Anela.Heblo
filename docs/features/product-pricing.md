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
  EF configuration. Migration `20260910185627_AddProductPriceChangeLog` creates the
  `ProductPriceChangeLogs` table in the `public` schema — the *only* table this feature owns.
  There is no master-price table: `20260903130253_AddProductPricing` created the old
  `ProductPrices` / `ProductPriceSyncStates` master tables and
  `20260910155013_DropProductPriceMasterTables` drops them again on this branch.
  **Migrations are applied by hand here**, so every table names its schema explicitly.
- **Adapters**: `ShoptetPriceListClient` (`Anela.Heblo.Adapters.ShoptetApi/Pricing/`) reads
  and writes the Shoptet retail price list; the Flexi side reuses the existing ERP price
  read/write clients.
- **API**: `ProductPricingController` — `GET /api/product-pricing/divergence`,
  `POST /api/product-pricing/sync`, `PUT /api/product-pricing/prices/{productCode}`. Gated by
  `Feature.Products_Catalog` (write requires `AccessLevel.Write`).
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
| `FlexiPriceTypeUnknown` | Both sides have a price, but Flexi's with-VAT figure was derived from an *assumed* price type — see [Flexi price type](#flexi-price-type-and-what-the-write-stores-in-cenazakl) below. Reported even when the numbers happen to agree, because that agreement cannot be trusted. |
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

### Flexi price type, and what the write stores in `cenaZakl`

Flexi's `cenaZakl` field means different things depending on the item's own price type:

- **`bezDph`** ("without VAT") — `cenaZakl` is the excl-VAT price; the with-VAT figure is
  derived by grossing it up with the item's VAT rate.
- **`sDph`** ("with VAT") — `cenaZakl` **is already** the with-VAT price.
- Unknown/unset — the ERP read did not expose a usable price type at all.

The comparison surfaces this as `FlexiPriceType` (`"bezDph"`, `"sDph"`, or `null`) on every
row so an operator can see it.

**The operator always enters a price including VAT, and the write says so explicitly.** The
PUT carries the price *and* the price-type flag together:

```json
{"winstrom": {"cenik": {"cenaZakl": "287.00", "typCenyDphK": "typCeny.sDph"}}}
```

`typCeny.sDph` declares "this number includes VAT", so Flexi stores it as entered instead of
reinterpreting it through whatever the item was previously configured as. The write therefore
needs **no VAT rate at all** and cannot be silently wrong because a rate was mis-read.

> **Two live-ERP findings got us here (2026-09-11).** Writing the converted excl-VAT figure
> put the wrong number in Flexi. Writing the with-VAT figure *without* the flag was no better:
> on a `bezDph` item, `cenaZakl = 287.00` was taken as a base price and shown as 347.27
> including VAT. Only writing both fields together makes the result independent of the item's
> existing configuration. Do not drop `typCenyDphK` from the payload.

**This changes ERP master data beyond the price:** an edited item's price type becomes
`sDph`. That is deliberate and was chosen knowingly — if every price is entered including
VAT, that is the setting those items should carry.

### The VAT band (read path only)

Flexi reports each item's VAT band in `typszbdphk`. Heblo recognises **two vocabularies**
for it — the enum form the rest of the Flexi adapter uses (`typSzbDph.dphZakl`,
`typSzbDph.dphSniz`, `typSzbDph.dphSniz2`, `typSzbDph.dphOsv`, and their bare `dphZakl`
forms) and the Czech labels (`základní`, `snížená`, `druhá snížená`, `osvobozeno`, with
accent-free spellings), matched case-insensitively after trimming. User query 41's
definition is not visible from this repository, so which vocabulary it actually returns is
not knowable here; recognising both removes the guess in either direction.

- **Read path** (`ProductPriceFlexiDto.Vat`, the comparison screen): an unrecognised band
  falls back to 21%, exactly as before, so the comparison behaves unchanged.
- **Write path**: unaffected — nothing is converted, so no rate is consulted and an
  unrecognised band can never block a price edit.
- **Catalog margins**: `IProductVatRateProvider` still feeds `ShoptetEshopPriceClient`, so a
  recognised-vs-unrecognised band still moves margin figures. That is why the band mapping
  recognises both vocabularies rather than falling through to 21% for everything.

The reason the write is stricter: a wrong VAT rate produces a wrong `cenaZakl`, which Flexi
then reconstructs at its *real* rate — a 12% item priced as if it were 21% ends up ~7%
under-priced in the ERP. The comparison cannot catch it either, because the read applies the
same wrong rate to the same `cenaZakl` and reproduces the requested price, marking the row
`InAgreement`. The provider also never recovers a rate arithmetically from the
with/without-VAT pair: both of those numbers are derived from the band in question, so the
result would be Heblo's own assumption returned as if it were a measurement.

An unrecognised `typszbdphk` value is logged once per run at warning level, raw — that log
line is how we find out which vocabulary Flexi really uses.

## Manual sync of the products on screen

The report is only as fresh as its two reads, and the Flexi leg is served from a five-minute
`IMemoryCache` — so a price changed directly in Flexi (or in Shoptet's own admin) can keep
rendering stale for minutes. `POST /api/product-pricing/sync` re-reads both systems for one
named selection of products and returns their fresh comparison rows.

It is a **read**, despite the POST: nothing is written to either system, and it stays on the
read permission alongside the divergence report. POST only because a selection of product
codes belongs in a body rather than a query string.

`PriceComparisonService.BuildScopedReportAsync` backs it, and differs from the unscoped
report in exactly two ways:

- **Flexi is read with `forceReload: true`**, bypassing the five-minute ceník cache. This is
  the whole point of the button; without it the operator would press sync and be shown the
  same stale numbers. The ERP read cannot be narrowed further — user query 41 is a
  whole-ceník read with no per-product form, so a selection scopes the rows, not the query.
- **Shoptet is read per product code** (`GetPriceWithVatAsync`, `?code=`) when the selection
  is at most `MaxProductsReadIndividually` (25) products. Beyond that, one request per
  product costs more than the paginated whole-list read, so it falls back to
  `GetPricesWithVatAsync` and filters — which is what keeps a sync with no filter applied
  from becoming one HTTP request per product in the catalogue. A cost heuristic only: both
  routes produce identical rows.

Codes that are not priced catalog products are ignored rather than rejected, so the caller
never has to keep its selection in step with the catalogue. The validator caps a request at
10,000 codes — deliberately far above the whole priced catalogue, so syncing with no filter
still works; it bounds one request, it is not a business limit on the selection.

`useSyncProductPrices` cancels any in-flight divergence refetch before writing the merged
report. A price save invalidates that query, and its whole-catalogue refetch reads Flexi from
the five-minute cache — landing after the sync it would silently replace the force-reloaded
prices with exactly the stale ones the sync exists to defeat.
`PriceDivergenceReport.syncRace.test.tsx` pins that ordering.

A Shoptet or Flexi read failure propagates, exactly as `GetPriceDivergenceReportHandler`
leaves it — there is no partial state to report and nothing was written, so no error code of
its own was added.

### Frontend: syncing what the filters left on screen

`PriceDivergenceReport` puts a `Synchronizovat (N)` button in the filter bar, where N is the
number of rows the name/code/`pouze rozdílné` filters left visible. Those codes are exactly
what the sync covers.

`useSyncProductPrices` folds the response into the cached report with `setQueryData`, not
`invalidateQueries`: invalidating would refetch the whole catalogue from both live systems
and throw away the very scoping the operator asked for by filtering. `mergeSyncedRows`
(`frontend/src/api/hooks/priceDivergenceMerge.ts`) replaces rows by product code, leaves
everything else identical, and re-tallies the six summary tiles over the merged rows — the
server-side summary counted the catalogue as it stood before the sync, so leaving it alone
would let a tile contradict the very row that was just refreshed. The tally counts the
`kind` the backend already assigned; it never decides for itself whether a row agrees, so
the comparison rules stay in `PriceComparisonService` alone.

A sync that finds both systems holding what the report already showed leaves the table
byte-for-byte identical — which is indistinguishable from a button that does nothing, and was
reported as exactly that. So a successful sync also writes a status line under the filter bar:
`Synchronizováno v HH:MM — beze změn`, or the number of rows that actually moved.
`countChangedRows` (same module as the merge) compares the synced rows against the ones the
report was showing on the two live prices, the Flexi price type and the backend's verdict; it
counts only rows the report holds, so it can never claim a change the operator cannot find in
the table. The line carries `role="status"`, since for an operator who cannot see the table
stay the same it is the only evidence the button did anything. A later failure clears it, so a
stale confirmation never sits next to a fresh error.

## Nightly DQT check and dashboard tile

The comparison also runs as a scheduled data-quality check, wired into the shared DQT
(Data Quality Tests) framework — see `docs/features/data-quality-dqt.md`.

- **Test type**: `DqtTestType.PriceComparison`, implemented by `PriceComparisonDqtComparer`
  (an `IDriftDqtComparer`). A price comparison is a snapshot, not a date-ranged query, so
  the DQT framework's from/to bounds are ignored, the same accommodation the other drift
  comparers make.
- **Job**: `PriceComparisonDqtJob`, Hangfire recurring job `daily-price-comparison-dqt`,
  `0 9 * * *` (09:00 daily). The DQT checks are deliberately staggered — 05:00 invoices,
  06:00 product pairing, 07:00 stock write-back, 08:00 lot stock — so this one runs after
  all of them rather than colliding with product pairing at 06:00.
- **Source**: `PriceComparisonDqtAdapter` implements DataQuality's consumer-owned
  `IPriceComparisonSource` contract, translating `PriceComparisonService`'s rows into
  `PriceDivergence` records. `FlexiDiffers`, `MissingInFlexi`, and `FlexiPriceTypeUnknown`
  all count as mismatches; `MissingInShoptet` does not (Shoptet is authoritative — a
  product it has never priced has no comparison to fail) and neither does `InAgreement`.
- **Guard against a green tile over nothing**: because `MissingInShoptet` is excluded from
  the mismatch count, an empty or truncated Shoptet read would classify *every* row that way,
  produce zero mismatches, complete the run, and render the tile green "vše OK" having
  compared nothing — reachable with no exception at all (a wrong or emptied
  `Shoptet:DefaultPriceListId`, a paginator truncating the read to the first 100 items, or
  every price being unreadable, which is logged rather than thrown). So
  `PriceComparisonDqtComparer` **throws** when not one in-scope product had a Shoptet price,
  which makes `DriftDqtJobRunner` record the run `Failed` and the tile go red — the same path
  a read *failure* already takes.
- **MissingInShoptet is recorded, not hidden**: those rows are persisted as *informational*
  drift results (`DriftComparisonResult.Informational`, `PriceComparisonMismatch
  .MissingInShoptet` = 4). `DriftDqtJobRunner` persists them alongside the mismatches but
  leaves them out of `TotalMismatches`, so they are listed in the run detail and counted on
  the tile without turning it amber on their own.
- **Result shaping**: `DqtDriftResult`'s generic `HebloValue`/`ShoptetValue` columns are
  named for the Heblo-vs-Shoptet checks that came first. For this check, `ShoptetValue`
  carries the Shoptet price and `HebloValue` carries the Flexi price — the frontend labels
  these columns "Shoptet" and "Flexi", not "Heblo", for exactly that reason.
- **Dashboard tile**: `PriceComparisonStatusTile` (tile id `pricecomparisonstatus`, title
  "Kontrola cen"), reads the latest `PriceComparison` run and shows total checked / total
  mismatches with a status color (green/amber/red), plus "N bez ceny v Shoptetu" whenever any
  product had no Shoptet price at all, drilling down to `/products/pricing`.

## Write-through price edit

`PUT /api/product-pricing/prices/{productCode}` (`SetProductPriceHandler`) writes a new
price to Shoptet, then to Flexi, in that order, with a pre-flight before either write:

1. **Read Shoptet's current price** via the single-product endpoint
   (`GET /api/pricelists/{id}?code=X` — see `docs/integrations/shoptet-api.md`) to record as
   `OldPriceWithVat` in the change log. A product with no row in the retail list cannot be
   priced at all and fails here.
2. **Pre-flight the Flexi leg** — resolve the Flexi ceník item id. This runs *before*
   anything is written, on purpose: a missing ceník id is knowable up front and guarantees the
   Flexi leg cannot succeed, so discovering it only after Shoptet was already written would
   manufacture an avoidable divergence.
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

A *failed* Flexi read and a product that genuinely has no ceník item are reported as two
different codes on purpose (`ProductPriceErpReadFailed`, `ProductPriceFlexiItemIdUnknown`).
Collapsing them, as an earlier revision did, tells every operator during a Flexi outage to go
hunting in Flexi for a ceník item that is actually there.

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
| `/api/product-pricing/sync` | POST | `Products_Catalog` (read) | Re-reads both systems for the named products, bypassing Flexi's ceník cache. Never writes anywhere. |
| `/api/product-pricing/prices/{productCode}` | PUT | `Products_Catalog` (write) | Write-through price edit: Shoptet, then Flexi, with pre-flight. |

## Error codes (36XX module range)

| Code | Meaning |
|---|---|
| `ProductPriceNotFoundInShoptet` | Product is not in the Shoptet retail price list; nothing written. |
| `ProductPriceFlexiItemIdUnknown` | No Flexi ceník item (or VAT rate) resolvable for the product; nothing written. |
| `ProductPriceShoptetWriteFailed` | The Shoptet write itself failed; nothing written. |
| `ProductPriceFlexiWriteFailed` | Shoptet was written successfully but the Flexi write failed — **the two systems now diverge**. |
| `ProductPriceErpReadFailed` | The Flexi read itself failed (outage, timeout, 5xx) — distinct from any fact about the product; nothing written. |

## Known constraints

- No sandbox for either Shoptet or Flexi — every write from this feature hits the live
  e-shop and the live ERP.
- Writes only ever target Flexi items whose price type is `bezDph`. `sDph` items can be
  *read* and compared but never written through this feature.
- No server-side price ceiling; the frontend confirmation dialogs are the only guard.
- A Flexi write failure leaves a permanent divergence until an operator re-edits the price
  or fixes it directly; there is no automated retry or rollback.
