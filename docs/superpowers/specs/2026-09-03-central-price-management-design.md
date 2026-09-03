# Central Retail Price Management (Heblo as master)

**Date:** 2026-09-03
**Status:** Approved design, ready for implementation planning
**Scope:** Spec 1 of 2. This spec covers the single retail price and its
synchronisation. Multiple price lists, pricing rules and XLS export are
Spec 2 and are explicitly out of scope here.

---

## 1. Problem

Retail prices are currently maintained by hand in **two** systems:

- **ABRA Flexi** (`cenik.cenaZakl`) — needed for invoicing.
- **Shoptet** (default price list) — what the customer sees.

A price change means typing the same number twice, and the two systems drift
apart with no mechanism to notice. There is no single place to answer "what is
this product's price?".

Heblo already *reads* both, but only to display them:

| Concern | Today |
|---|---|
| Read ERP price | `FlexiProductPriceErpClient` via user query `41` (`kod`, `cena`, `cenanakup`, `typszbdphk`, `idKusovnik`) |
| Read e-shop price | `ShoptetPriceClient` via a CSV product export URL (windows-1250, `;`, columns addressed by index) |
| Merge | `CatalogMergeService` sets `CatalogAggregate.EshopPrice` / `.ErpPrice` |
| Resolve | Fallback: `CurrentSellingPrice => EshopPrice?.PriceWithVat ?? ErpPrice?.PriceWithoutVat` |
| Write | `ShoptetPriceClient.SetAllAsync` writes a CSV to a temp file. **Nothing calls it.** Dead path. |

## 2. Decision: Heblo owns the price

The user edits the price **once, in Heblo**. Heblo pushes it to Shoptet and
Flexi. Neither downstream system is the source of truth.

This was chosen over "Flexi is the source of truth" or "Shoptet is the source of
truth" because of what Spec 2 will need — several price lists (wholesale,
Ampler, …) whose prices are hand-set and are **never sold on the web**:

| | ABRA Flexi | Shoptet |
|---|---|---|
| Read prices | `GET /c/{firma}/cenik` | `GET /api/pricelists/{id}/snapshot` (100/page) |
| Write prices | Full REST CRUD on `cenik` (`cenaZakl`, `cenaNakup`, `szbDph`) | `PATCH /api/pricelists/{id}`; async bulk `PATCH /api/pricelists/{id}/batch` |
| Multiple price lists | **Rule-based** — `cenova-uroven` (Business/Premium only): margin/rebate/discount %, 4 quantity tiers, rounding, goods-group × company-group | **Explicit** — n price lists, explicit per-item price rows, assigned to customer groups |
| Explicit per-partner price | `/c/{firma}/adresar/{id}/individualni-cenik`, `/c/{firma}/cenikova-skupina/{id}/individualni-cenik`; `/c/{firma}/individualni-cenik` is a computed overview | Per-price-list rows, plus `commonPrice`, `actionPrice`, `sales` |
| Latency | Slowest dependency in the system: p95 6.7 s, p99 23 s blended (`docs/integrations/flexibee-api.md` §2) | Normal REST latency |

Flexi models extra price lists as **rules** computed at read time; Shoptet models
them as **explicit rows**. The requirement is hand-tuned explicit prices, some for
partners with no e-shop presence at all. Neither system is a natural home for
that, so the master lives in Heblo's own database and both external systems
become downstream targets.

## 3. Scope

**In scope**

- A `ProductPrices` table in Heblo, one row per product code, holding the retail
  price and its VAT rate.
- Editing that price in the Heblo UI.
- Push to Shoptet's default price list and to Flexi's `cenik`.
- Three-way drift detection with human conflict resolution.
- A one-time seeding/reconciliation run.

**Out of scope (Spec 2)**

- Multiple price lists per product (wholesale, Ampler, …).
- Pricing rules (derive one list from another).
- XLS price-list export.

Non-retail price lists will be **XLS-export only** — they are never pushed to
Shoptet or Flexi. This is a confirmed product decision and is why Spec 1 needs
only a single sync path.

**Out of scope (not planned)**

- Variant-level pricing.
- Writing purchase prices to either system.

## 4. Assumptions

- **A1 — Canonical form is price *with* VAT.** That is the number a human sets
  and rounds. The VAT rate is read from Flexi (`typszbdphk` → 0 / 15 / 21, as
  `ProductPriceFlexiDto.Vat` already maps). Price-without-VAT is *derived* when
  writing to Flexi.
- **A2 — Heblo never writes purchase price.** `cenaNakup` stays computed by
  Flexi from the BoM and flows into Heblo as it does today (the existing
  `IProductPriceErpClient.RecalculatePurchasePrice` path is untouched). Shoptet's
  `buyPrice` is never written.
- **A3 — Priced product types are `Product`, `Goods`, `Set`.** `Material` and
  `SemiProduct` have no selling price and are excluded from the table and from
  every sync run.
- **A4 — One price per `ProductCode`.** `CatalogAggregate` is keyed by a single
  product code and Shoptet variants appear only as display text in the expedition
  protocol, so there is no variant-level pricing to model.
- **A5 — Sync is a background job, never write-through.** Flexi's p95 is 6.7 s
  and Shoptet's bulk path is async; a UI save must not block on either. Saving a
  price in Heblo marks it `Pending` and returns immediately.

## 5. Data model

New EF Core entities and configurations under
`backend/src/Anela.Heblo.Persistence/ProductPricing/`, with one migration.

### 5.1 `ProductPrices`

| Column | Type | Notes |
|---|---|---|
| `ProductCode` | `text` PK | Matches `CatalogAggregate.ProductCode` |
| `PriceWithVat` | `numeric(18,4)` | Canonical value, per A1 |
| `VatRate` | `numeric(5,2)` | 0 / 15 / 21, sourced from Flexi |
| `ModifiedAt` | `timestamp without time zone` | |
| `ModifiedBy` | `text` | User id |

> `timestamp without time zone` is deliberate — see the raw-SQL/timestamptz
> gotcha already recorded for this repo. All EF access here is through the
> DbContext, so no raw-SQL parameter typing is involved.

### 5.2 `ProductPriceSyncStates`

One row per (product, target).

| Column | Type | Notes |
|---|---|---|
| `ProductCode` | `text` | Composite PK part 1, FK → `ProductPrices` |
| `Target` | `int` | Composite PK part 2. `PriceSyncTarget { Shoptet = 1, Flexi = 2 }` |
| `LastPushedPriceWithVat` | `numeric(18,4)?` | **Load-bearing.** Null until first successful push |
| `LastPushedAt` | `timestamp?` | |
| `Status` | `int` | `PriceSyncStatus { InSync, Pending, Conflict, Failed }` |
| `RemoteValueAtConflict` | `numeric(18,4)?` | The downstream value that caused the conflict |
| `ConflictDetectedAt` | `timestamp?` | |
| `LastError` | `text?` | Populated on `Failed` |

`LastPushedPriceWithVat` is what makes "stop and ask" possible. Comparing Heblo
to the remote tells you only *that* they differ; comparing both against the last
value Heblo pushed tells you *who moved*.

### 5.3 Relationship to the existing catalog

`CatalogMergeService`, `CatalogAggregate.EshopPrice` and `.ErpPrice` are
**unchanged**. The catalog continues to reflect *observed reality* in both
systems; the new `ProductPricing` slice owns *intent*. Keeping them separate is
what lets drift detection work at all — if the catalog were rewritten to read
from `ProductPrices`, there would be nothing left to compare against.

**Known follow-up, deliberately not done here:** once Heblo owns the price,
`CatalogAggregate.CurrentSellingPrice`'s eshop-then-ERP fallback
(`CatalogAggregate.cs:186-189`) is misleading. Changing it touches margin
calculation, analytics and the financial overview, so it is left alone in this
spec and should be revisited after the sync has been running cleanly.

## 6. Sync algorithm

`ProductPriceSyncJob : IRecurringJob`, registered alongside the existing
recurring jobs so it appears in the current jobs UI. It also runs on demand via
`TriggerPriceSync`.

Per run, per target:

1. **Bulk read** the remote prices — one call set per system, not per product.
2. For each in-scope product, run the three-way compare:

| Heblo vs `LastPushed` | Remote vs `LastPushed` | Action |
|---|---|---|
| same | same | Nothing. `InSync`. |
| **changed** | same | Push. On success, `LastPushed` = pushed value, `InSync`. |
| same | **changed** | `Conflict`. Record `RemoteValueAtConflict`. Push nothing. |
| **changed** | **changed** | `Conflict`. Record `RemoteValueAtConflict`. Push nothing. |

3. `LastPushed` is null (never pushed) and a remote value exists → apply the
   seeding rule of §7 **to that product**. This is not only a first-run case: a
   product added to the catalogue later arrives with a null `LastPushed` and is
   seeded the same way, individually.
4. A product missing from the remote system → `Failed` with an explanatory
   `LastError`. Never create the product downstream.
5. One product's failure never aborts the run. Each product's outcome is written
   independently.

Decimal comparison uses an exact `decimal` equality after rounding both sides to
2 decimal places, since both remotes return prices as 2-decimal strings.

### 6.1 Conflict resolution

A conflict blocks that product's sync **for that target only** until a human
resolves it in Heblo. Two one-click actions:

- **Keep Heblo's price** — set `LastPushed` = `RemoteValueAtConflict`, status
  `Pending`. The next run then sees "Heblo changed, remote didn't" and overwrites.
- **Accept the remote price** — write `RemoteValueAtConflict` into
  `ProductPrices.PriceWithVat`, set `LastPushed` = same value, status `InSync`.

Either way the sync unblocks itself on the next run with no further intervention.

## 7. Seeding / first run

Seeding is a per-product rule, not a one-off script. It happens to apply to
every product on the first run because `ProductPrices` starts empty, and applies
to individual products later as they appear in the catalogue. The rule:

1. Seeds `ProductPrices` from **Shoptet's default price list** — today's retail
   truth — including `VatRate` from the Flexi read.
2. Sets the Shoptet sync state to `InSync` with `LastPushed` = the seeded value.
3. For each product where **Flexi disagrees** with the seeded price, opens a
   `Conflict` on the Flexi target rather than silently overwriting Flexi.

The result is a one-time worklist of exactly the products the double-entry has
already desynchronised — information worth surfacing rather than discarding.

Products present in Flexi but absent from Shoptet's price list are seeded from
Flexi with a `Failed` Shoptet state, so they show up as needing attention.

## 8. Adapters

### 8.1 Shoptet — `ShoptetPriceListClient`

New client in `Anela.Heblo.Adapters.ShoptetApi/Pricing/`, using the existing
token-authenticated `HttpClient` registration (`ShoptetApiSettings.ApiToken`).

- **Resolve the list:** `GET /api/pricelists` → the default price list id.
  Overridable via config `Shoptet:DefaultPriceListId`.
- **Read:** `GET /api/pricelists/{id}/snapshot`, paginated, `itemsPerPage` max
  100, `page` from 1.
- **Write:** per-item `PATCH /api/pricelists/{id}`, sending `priceWithVat` and
  letting Shoptet recalculate the stored form.

**Why per-item and not `PATCH /api/pricelists/{id}/batch`:** the batch endpoint
is asynchronous and **requires a registered `job:finished` webhook** — without
it, Shoptet returns 403 and never queues the job. That would make Heblo depend on
a public inbound webhook endpoint. Only *changed* prices are ever pushed, which
is a handful per run, so the batch path buys nothing here. It remains the
documented escape hatch if a bulk repricing is ever needed, and would then also
need the `GET /api/system/jobs/{jobId}` polling path.

**Zero/null gotcha:** from 2026-09-14 (feature-flagged per e-shop) a literal `0`
in a price field means a genuine zero price, and only `null` clears the price.
This client only ever sends real prices and skips products with no price, so it
is unaffected by the flag in either state — but it must never be "optimised" into
sending `0` for absent values.

**Retiring the CSV path:** `ShoptetPriceClient` (CSV export URL, windows-1250,
index-addressed columns) and its dead `SetAllAsync` are replaced. The
`IProductPriceEshopClient` implementation backing `CatalogDataRefreshService`
moves to the REST snapshot read, so the catalog and the sync see the same data.

`IProductPriceEshopClient` itself changes: `SetAllAsync` is **removed from the
interface** — the write path now belongs to the sync job, which needs per-product
results rather than a whole-catalogue CSV blob. `SetProductPricesResultDto` and
`ProductPriceOptions.ProductExportUrl` are deleted along with it. The read method
`GetAllAsync` keeps its signature, so `CatalogDataRefreshService` and
`CatalogMergeService` need no changes at all.

### 8.2 Flexi — `FlexiProductPriceWriter`

New writer in `Anela.Heblo.Adapters.Flexi/Price/`.

- **Write:** `PUT /c/{firma}/cenik/{idcenik}.json` with
  `{"winstrom":{"cenik":{"cenaZakl":"<price without VAT>"}}}`.
- **Addressed by internal `idcenik`**, already read as
  `ProductPriceFlexiDto.ProductId`. **Never by `code:`** — Flexi treats a write to
  a non-existent code as a *create*, so a code typo would silently invent a price
  list item. A product with no known `idcenik` is a `Failed` sync, never a create.
- Price sent is derived from the canonical with-VAT value using the product's
  `VatRate`, rounded to 2 decimals.
- **Read stays as-is:** user query `41` via `FlexiProductPriceErpClient`.
- `cenaNakup` is never written (A2).

## 9. Application surface

New vertical slice `backend/src/Anela.Heblo.Application/Features/ProductPricing/`
with MediatR use cases and an MVC controller, following the existing module
conventions:

| Use case | Purpose |
|---|---|
| `GetProductPrices` | Grid: product, price, VAT, per-target sync status |
| `SetProductPrice` | Edit one price; marks both targets `Pending` |
| `GetPriceSyncConflicts` | The conflict worklist |
| `ResolvePriceSyncConflict` | Keep-Heblo / accept-remote, per §6.1 |
| `TriggerPriceSync` | Manual run of the job |

Conventions this slice must follow, per gotchas already recorded for this repo:

- DTOs are **classes, never records** (OpenAPI generator mishandles record
  parameter order).
- Every `*Response` inherits `BaseResponse`, or the reflection contract test fails
  in CI.
- New `ErrorCode` values need the `ErrorHandlingTests` module-range bucket entry
  **and** a Czech translation in `i18n.ts`, or two tests fail.
- Validators are registered manually per module — there is no
  `AddValidatorsFromAssembly`.

Frontend: a price grid with inline edit and a conflicts view. API hooks construct
absolute URLs as `${apiClient.baseUrl}${relativeUrl}`. Job status is already
surfaced by the existing recurring-jobs UI, so no new job plumbing is needed.

## 10. Error handling

- Every remote call is wrapped; a failure sets `Status = Failed` with
  `LastError` and is retried on the next run.
- Bulk reads that fail abort *that target's* run only, leaving states untouched
  rather than mass-marking `Failed`.
- Flexi calls respect the existing timeout/circuit-breaker configuration in the
  Flexi adapter — no new timeout policy is introduced.
- User-facing messages use the standard `ErrorCode` mechanism; raw exception text
  is logged, not returned.

## 11. Testing

- **Unit — the three-way compare.** This is the real logic and gets exhaustive
  coverage: all four table rows × both targets × never-pushed × remote-missing ×
  `idcenik`-missing × both conflict-resolution actions.
- **Unit — adapters** with mocked `HttpClient`: pagination of the Shoptet
  snapshot, `priceWithVat` payload shape, Flexi `idcenik` addressing and the
  refusal to write without one, VAT derivation and rounding.
- **Unit — seeding**, including the Flexi-disagreement conflict path.
- **Integration** tests against the live systems marked `Category=Integration`,
  which CI excludes.
- **Frontend** tests for the grid and conflict resolution. Shell-component tests
  must mock any new context, or existing tests break.

## 12. Documentation tasks (do these first)

This repo requires external-API findings to be documented **before** code relies
on them:

- `docs/integrations/shoptet-api.md` — the price-list endpoints (`GET
  /api/pricelists`, `GET /api/pricelists/{id}/snapshot` with its 100/page
  paginator, `PATCH /api/pricelists/{id}`), the `priceWithVat` /
  `priceWithoutVat` / `price` distinction, the `buyPrice`-only-on-default-list
  restriction, the 2026-09-14 zero/null semantics change, and the
  `job:finished` webhook precondition on every async endpoint.
- `docs/integrations/flexibee-api.md` — the `cenik` write path, `cenaZakl` vs
  `cenaNakup`, and the create-on-unknown-`code:` behaviour that forces
  `idcenik` addressing.

## 13. Sources

- Shoptet price lists API — https://api.docs.shoptet.com/shoptet-api/openapi/price-lists
- Shoptet batch pricelist update — https://api.docs.shoptet.com/shoptet-api/openapi/price-lists/updatepricelistbatch
- Shoptet price update changes — https://developers.shoptet.com/price-update-made-easier/
- Shoptet asynchronous requests — https://developers.shoptet.com/asynchronous-requests/
- Shoptet API release news, 2026-08-20 — https://developers.shoptet.com/api-release-news-from-august-20-2026/
- ABRA Flexi individual price lists via REST — http://podpora.flexibee.eu/cs/articles/4744337-individualni-cenik-rest-api
- ABRA Flexi pricing tiers — http://podpora.flexibee.eu/en/articles/4556411-pricing-tiers
- ABRA Flexi record identifiers — https://podpora.flexibee.eu/en/articles/4725798-record-identifiers
