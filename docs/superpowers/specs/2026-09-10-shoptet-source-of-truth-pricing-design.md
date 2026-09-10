# Shoptet as the price source of truth

**Date:** 2026-09-10
**Status:** Approved, not yet implemented
**Supersedes:** [2026-09-03-central-price-management-design.md](2026-09-03-central-price-management-design.md)

## Why this replaces the previous design

The previous design made Heblo the master of retail prices: a `ProductPrices` table held the
authoritative value, and a sync service pushed it outward to Shoptet and Flexi. Because both
downstream systems can also be edited directly, that design needed a three-way comparison
(Heblo value vs. last-pushed value vs. current remote value) to work out *who moved*, plus a
per-product state machine — `Pending`, `InSync`, `Conflict`, `Failed` — plus a conflict
resolution use case and UI to let a human arbitrate.

All of that machinery exists only to answer a question that disappears once one system is
simply declared authoritative. Shoptet is where retail prices are actually managed, so
Shoptet becomes the source of truth. Heblo stops owning a price value at all.

## What the system does after this change

1. **Compare.** Read prices from Shoptet and Flexi, compare them, show the mismatches.
2. **Alert.** A nightly check records a run and lights a dashboard tile when they disagree.
3. **Edit.** A price edit writes straight to Shoptet; once that succeeds, it writes to Flexi.

Nothing stored in Heblo is ever consulted to decide what a price should be.

## Decisions taken

| # | Decision | Rationale |
|---|---|---|
| D1 | Shoptet is the source of truth; Heblo holds no master price | Removes the entire "who moved" problem |
| D2 | On a partial write failure (Shoptet ok, Flexi fails), report it and stop — no retry queue, no rollback | The comparison view is already the safety net for exactly this state; a retry outbox would reintroduce the state machinery being removed |
| D3 | Heblo persists an append-only change log and nothing else | Only record of D2's partial-failure state, and answers "who changed this price"; Shoptet's own history is not exposed through its API |
| D4 | The comparison joins the existing DataQuality (DQT) feature as a new test type | Shoptet-vs-Flexi drift is the same family as the stock and invoice drift checks already there; reuses `DqtRun`, its repository, tile shape and job wiring instead of duplicating them |
| D5 | Writes go Shoptet first, then Flexi | Shoptet is authoritative and customer-facing; if only one system can end up correct, it must be that one |

## Architecture

Two independent flows. They share no state.

```
READ (comparison)                WRITE (edit)

Shoptet ──┐                      User
Flexi  ───┼──> compare             │
Catalog ──┘        │               v
                   │            Shoptet ──fail──> error, nothing written
           mismatch rows           │ ok
                   │               v
           nightly DQT run       Flexi ──fail──> error: "Shoptet updated, Flexi not"
                   │               │ ok
           dashboard tile          v
                              change-log row (written on every outcome)
```

### Module boundaries

The comparison's *alerting* lives in DataQuality; the pricing domain stays in ProductPricing.
The dependency inverts per the documented consumer-contract / provider-adapter pattern
(`ILeafletKnowledgeSource`, `development_guidelines.md`):

- **Consumer:** `DataQuality/Contracts/IPriceComparisonSource` — DataQuality owns it, and it
  exposes only what the check consumes (counts of products checked and products mismatched).
- **Provider:** `ProductPricing/Infrastructure/PriceComparisonDqtAdapter` — delegates to
  `PriceComparisonService`.
- **Registration:** `ProductPricingModule` registers the binding. DataQuality never touches it.

## Component changes

### Removed

Roughly 20 files, all belonging to the master-price reconciliation:

**Domain** (`Anela.Heblo.Domain/Features/ProductPricing/`)
`PriceSyncDecider`, `PriceSyncDecision`, `PriceSyncAction`, `PriceSyncStatus`,
`PriceSyncTarget`, `ProductPrice`, `ProductPriceSyncState`, `IProductPriceRepository`

**Application** (`Anela.Heblo.Application/Features/ProductPricing/`)
`Services/ProductPriceSyncService`, `Services/IProductPriceSyncService`,
`Services/PriceSyncRunResult`, `Infrastructure/Jobs/ProductPriceSyncJob`,
`UseCases/TriggerPriceSync/*`, `UseCases/GetPriceSyncConflicts/*`,
`UseCases/ResolvePriceSyncConflict/*`, `UseCases/GetProductPrices/*`,
`Contracts/PriceConflictResolution`, `Contracts/PriceSyncConflictDto`,
`Contracts/ProductPriceDto`

**Persistence** `ProductPriceConfiguration`, `ProductPriceSyncStateConfiguration`,
`ProductPriceRepository`

**API** `GET /api/product-pricing/prices`, `POST /api/product-pricing/sync`,
`GET /api/product-pricing/conflicts`, `POST /api/product-pricing/conflicts/resolve`

**Frontend** `ProductPriceGrid.tsx`, `PriceConflictBanner.tsx`, the two-tab page layout

**Database** a migration dropping `ProductPrices` and `ProductPriceSyncStates`

### Retained unchanged

`IEshopPriceListClient`, `ShoptetPriceListClient`, `IErpPriceWriter`,
`FlexiProductPriceWriter`, `IProductPriceErpClient`, `FlexiProductPriceErpClient`,
`IProductVatRateProvider`, `VatRateCalculator`.

The 2026-09-10 fix to `ShoptetPriceListClient.SetPriceWithVatAsync` (sending `priceWithVat`
as an object group rather than a flat string) remains necessary — the write path still uses it.

### Added

| Component | Location | Purpose |
|---|---|---|
| `PriceComparisonService` | `ProductPricing/Services/` | Renamed `PriceDivergenceReportService`, minus the master-price column and repository dependency |
| `ProductPriceChangeLog` | `Domain/Features/ProductPricing/` | Append-only history entity |
| `IProductPriceChangeLogRepository` | `Domain/Features/ProductPricing/` | `AppendAsync` only — no read method until something needs one |
| `PriceComparisonDqtAdapter` | `ProductPricing/Infrastructure/` | Implements DataQuality's contract |
| `IPriceComparisonSource` | `DataQuality/Contracts/` | Consumer-owned contract |
| `PriceComparisonDqtJob` | `DataQuality/Infrastructure/Jobs/` | Nightly run |
| `PriceComparisonStatusTile` | `DataQuality/DashboardTiles/` | Mismatch count + drill-down |

## Read path — the comparison

`PriceComparisonService` reads:

- Catalog, filtered to `Product`, `Goods`, `Set` (assumption A3: only sellable types carry a
  retail price) — unchanged from today
- Shoptet's retail price list, paged, via `IEshopPriceListClient.GetPricesWithVatAsync`
- Flexi prices via `IProductPriceErpClient.GetAllAsync`

It emits one row per in-scope product: code, name, Shoptet price with VAT, Flexi price with
and without VAT, Flexi price type, absolute and percentage difference, and a classification.

Row classification keeps today's precedence, with the Heblo-master notion removed:

1. `MissingInShoptet` — no price in the authoritative system; nothing to compare
2. `MissingInFlexi` — Shoptet has a price, the ERP read has nothing
3. `FlexiPriceTypeUnknown` — Flexi's with-VAT figure was derived from an assumed price type,
   so agreement cannot be trusted; reported even when the numbers happen to match
4. `FlexiDiffers` — both known, prices disagree beyond the tolerance below
5. `InAgreement` — both known, prices match

### Rounding tolerance (required, not optional)

Flexi stores `cenaZakl` excluding VAT and reconstructs the with-VAT price as
`cena * (100 + vat) / 100` on read. This does not round-trip: 190.00 stores as 157.02 and
reads back as 189.99. The removed sync service carried `FlexiRoundTripTolerance = 0.01m` for
exactly this reason.

**`PriceComparisonService` must apply the same 0.01 tolerance to the Shoptet-vs-Flexi
comparison.** Without it a large share of the catalogue reports as permanently divergent by
one haléř, and the dashboard tile is orange forever — which would make the alert worthless.
Shoptet stores the with-VAT price directly and gets no tolerance.

## Write path — editing a price

`SetProductPriceHandler` becomes write-through. Ordered steps, stopping at the first failure:

1. **Read the current Shoptet price** for the product: `GET /api/pricelists/{id}?code=X`
   (single-code filter, confirmed working; `codes=` is rejected). This both supplies the
   change log's "old value" and proves the product exists in the retail list.
   Absent → `ProductPriceNotFoundInShoptet`, nothing is written.
2. **Pre-flight the Flexi leg.** Resolve the numeric ceník id via `IProductPriceErpClient` and
   the VAT rate via `IProductVatRateProvider`, and compute the price excluding VAT, rounded to
   2 decimals away from zero. Either missing → `ProductPriceFlexiItemIdUnknown`, and **nothing
   is written anywhere**. Also refuse any item whose `ErpPriceType` is not exactly `"bezDph"` →
   `ProductPriceFlexiPriceTypeUnsupported`. `cenaZakl`'s VAT meaning depends on the item's own
   `typCenyDphK` — for an `"sDph"` item it holds the *with-VAT* price — so writing the
   excluding-VAT figure would silently underprice it in the live ERP by the VAT rate while
   reporting success. The read path already branches on this; the write path refuses rather than
   branching, because the `sDph` write semantics are unverified against a system with no sandbox.
3. **Write Shoptet.** `SetPriceWithVatAsync(productCode, priceWithVat)`. On failure →
   `ProductPriceShoptetWriteFailed`; nothing has changed anywhere.
4. **Write Flexi.** `SetPriceWithoutVatAsync(erpItemId, priceWithoutVat)`, addressed by the
   numeric ceník id. Never by product code — Flexi does not distinguish create from update, so
   a write by code silently creates a new item. On failure → `ProductPriceFlexiWriteFailed`.
5. **Append the change-log row** on every outcome, including failures.

**Why step 2 precedes the Shoptet write.** A missing ceník id or VAT rate is knowable before
anything is written, and it guarantees the Flexi leg cannot succeed. Discovering it after the
Shoptet write would manufacture a partial failure that was avoidable — so the only partial
failure this design can produce is a genuine Flexi write error (timeout, rejection, outage),
which is not knowable in advance.

The cost of this ordering: a product with no Flexi ceník item cannot have its price changed
through Heblo at all, and must be fixed in Flexi first or edited in Shoptet's own admin. That
is deliberate — such a product already reports as `MissingInFlexi` in the comparison, so
blocking the edit withholds no information. **If you would rather Shoptet always be updatable
regardless of Flexi's state, this is the one step to invert** — move the pre-flight back to
after the Shoptet write and accept the extra partial-failure case.

Both remote writes reject a non-positive price before dispatch: Shoptet treats a literal `0`
as a genuine zero price from 2026-09-14, not as "clear".

### Partial failure (D2)

When step 3 succeeds and step 4 fails, Shoptet carries the new price and Flexi the old one.
The handler returns `ProductPriceFlexiWriteFailed`, and the UI states plainly that Shoptet was
updated and Flexi was not. No retry is queued and Shoptet is not rolled back. The divergence
is then visible in the comparison — that is what the comparison is for — and re-saving the
same price re-attempts both legs.

### Change log

`ProductPriceChangeLogs`, append-only, never read by any decision:

| Column | Notes |
|---|---|
| `Id` | identity |
| `ProductCode` | |
| `OldPriceWithVat` | from step 1; null when Shoptet had no price |
| `NewPriceWithVat` | requested value |
| `ChangedAt`, `ChangedBy` | UTC; user email via `ICurrentUserService` (ADR-005) |
| `ShoptetSucceeded`, `FlexiSucceeded` | the two legs independently |
| `ErrorMessage` | truncated to the column length; null on full success |

It records only edits made through Heblo. A price changed directly in Shoptet's admin will not
appear — that is inherent to Shoptet owning the value, and the comparison, not the log, is what
catches such a change.

## Alerting

- `DqtTestType.PriceComparison = 5`
- `PriceComparisonDqtJob` runs nightly, calls `IPriceComparisonSource`, and records a `DqtRun`
  with `TotalChecked` = in-scope products and `TotalMismatches` = rows classified
  `FlexiDiffers`, `MissingInFlexi`, or `FlexiPriceTypeUnknown`.
  `DqtRun` requires a date range; a price snapshot has none, so both bounds are today — the
  same accommodation the other non-invoice test types already make.
- `PriceComparisonStatusTile` reads the latest run of that type: `success` at zero mismatches,
  `warning` above zero, `error` on a failed run, `no_data` before the first run. Drill-down
  targets the pricing screen.

Per-product divergence detail is not persisted. The screen always recomputes live, so what it
shows can never be stale — only the counts are stored, and only so the tile has something to
read.

## Frontend

`ProductPricingPage` loses its tab bar and becomes a single comparison screen. Columns: product
code, name, Shoptet price, Flexi price, difference (absolute and %), status. Filter by
classification; default to showing mismatches first.

Users holding `products.catalog.write` edit the Shoptet price inline. A successful save
invalidates the query so the row reloads from live data rather than trusting a local value.
The partial-failure case renders as a persistent row-level warning, not a dismissible toast —
it describes a state of the world that outlives the notification.

Deleted: `ProductPriceGrid`, `PriceConflictBanner`, `useProductPrices`, `useTriggerPriceSync`,
`useResolvePriceSyncConflict`.

## Error handling

New codes, all in the ProductPricing module range:

| Code | HTTP | Meaning |
|---|---|---|
| `ProductPriceNotFoundInShoptet` | 404 | Not in the retail price list; nothing written |
| `ProductPriceFlexiItemIdUnknown` | 422 | No numeric ceník id or no VAT rate; pre-flight abort, nothing written |
| `ProductPriceFlexiPriceTypeUnsupported` | 422 | Flexi price type is `sDph` or unknown; pre-flight abort, nothing written |
| `ProductPriceShoptetWriteFailed` | 502 | Shoptet rejected the write; nothing written |
| `ProductPriceFlexiWriteFailed` | 502 | Flexi rejected the write; **Shoptet already updated** — the only partial-failure state |

Each new code requires two further edits or the suite fails: the module-range bucket in
`ErrorHandlingTests`, and a Czech string in `i18n.ts`.

A failed bulk read on the comparison path aborts the whole comparison rather than reporting
every product as mismatched — a read failure says nothing about individual products.

## Testing

**Unit**
- Classification per kind, including the 0.01 Flexi tolerance in both directions
- `MissingInShoptet` takes precedence over every other kind
- Write ordering: Flexi is never called when the Shoptet write fails
- Pre-flight abort: neither remote is called when the ceník id or VAT rate is missing
- Each failure leg returns its own code and writes its own change-log row
- Non-positive prices are refused before any remote call
- Flexi is never written by product code

**Integration**
- The DQT adapter produces the counts the tile expects
- Tile states across zero mismatches, mismatches, failed run, and no run

**Adapter tests** assert outgoing request bodies structurally with `JsonDocument`, never with
substring matching. A `Contain("210.00")` assertion passed for a payload Shoptet rejected with
a 422 on 2026-09-10; these unit tests are the only contract check that exists, as there is no
Shoptet sandbox.

**Not covered by automated tests:** the live write path. Every call hits the production store
and the production ERP. First execution is a deliberate manual edit of one product, verified in
both systems by hand.

## Sequencing

Delete first, so nothing is left referencing the removed master table:

1. Remove the sync machinery, its use cases, endpoints and frontend components; add the
   drop migration
2. `PriceComparisonService` — rename, drop the master column, add the rounding tolerance
3. Change-log entity, repository, migration
4. Write-through `SetProductPriceHandler` plus the four error codes and their two companion edits
5. DQT contract, adapter, job, tile
6. Single-screen frontend with inline editing
7. Update `docs/features/` for the pricing feature to describe Shoptet-as-truth

## Open risks

- **Live-write blast radius.** The write path touches a production e-shop and ERP with no
  sandbox. Mitigation: the first real edit is manual and single-product.
- **VAT rate trust.** The without-VAT figure written to Flexi is derived from
  `IProductVatRateProvider`. A wrong rate writes a wrong base price that then reads back as a
  wrong with-VAT price. The comparison catches it on the next run, but only after the fact.
- **`FlexiPriceTypeUnknown` volume is unmeasured.** If a large share of the catalogue derives
  its with-VAT price from an assumed price type, the tile could be permanently orange for a
  reason unrelated to real divergence. Measure the count on the first run before deciding
  whether that kind belongs in the mismatch total.
