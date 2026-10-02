---
process: feed-manufacture-to-flexi
kind: feed
module: manufacture
summary: Posts a confirmed manufacture to ABRA Flexi as stock documents — material issue + semi-product receipt for the bulk phase, then semi-product/material issue + product receipt (and a bulk-sale issue) for the filling phase — with FEFO lot allocation, cost carried into the receipt price, and a BoM rewrite from the actual semi-product yield.
owns:
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ResidueDistributionCalculator.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IResidueDistributionCalculator.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/SubmitManufactureRequestItem.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ConfirmSemiProductManufacture/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ConfirmProductCompletion/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateBoMIngredientAmount/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/Confirm*.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ResidueDistributionDto.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ProductActualQuantityRequest.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureErpResilienceService.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/Exceptions/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Configuration/ManufactureErpOptions.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureMessages.cs
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureClient.cs
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/SubmitManufactureClient*.cs
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/ResidueDistribution.cs
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureTemplate.cs
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/Ingredient.cs
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/ErpManufactureType.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureException.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiExtensions.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/**
verified_at: "5e993f9e2"
related:
  - flow-manufacture-order
  - flow-manufactured-inventory
  - sync-manufacture-conditions
  - calc-margins
---

# Manufacture posting to Flexi

## Purpose
When production confirms a step of a manufacture order (výrobní příkaz), Heblo writes the
stock movements into ABRA Flexi so the ERP knows which materials were used up and what was
made: materials leave the material warehouse, bulk semi-product (meziprodukt / polotovar)
enters and later leaves the semi-product warehouse, and finished products (výrobky) enter the
products warehouse with a lot (šarže), expiration and a cost price. Flexi is the stock and
accounting system of record — these documents drive stock levels, the manufacture history
that the margin calculation (`calc-margins`) reads, and the cost of every manufactured piece.

It also rewrites the bill of materials (kusovník) in Flexi so the next batch plans with the
semi-product amount per piece that was really achieved.

Staff trigger it on the order detail page `/manufacturing/orders/:id`; the result (document
codes, notes, "manual action required" flag) is visible there and in the protocol PDF.

## Trigger
On demand, two buttons on the order detail (permission `Manufacture_ManufactureOrders` write):

| Step | Endpoint | Allowed from | Moves order to |
|---|---|---|---|
| Confirm semi-product (Potvrdit výrobu polotovaru) — multi-phase only | `POST /api/ManufactureOrder/{id}/confirm-semi-product` `{actualQuantity, changeReason?}` | Planned | SemiProductManufactured |
| Confirm products (Dokončit výrobu) | `POST /api/ManufactureOrder/{id}/confirm-products` `{products:[{id, actualQuantity}], overrideConfirmed, changeReason?}` | SemiProductManufactured (multi-phase) or Planned (single-phase) | Completed |

No scheduled job. The state change itself and its other side effects are in
`flow-manufacture-order`.

## Data flow
**Phase A — semi-product (`ConfirmSemiProductManufactureWorkflow`)**
1. Save `ActualQuantity` (grams) on `ManufactureOrderSemiProducts` via `UpdateManufactureOrderRequest`.
2. `SubmitManufactureRequest` (type `SemiProduct`, one item = semi-product code × actual qty,
   lot/expiration from the semi-product row) → `SubmitManufactureHandler` → circuit breaker →
   `FlexiManufactureClient.SubmitManufactureAsync` (aggregated path):
   1. Requirements: semi-product BoM (Flexi kusovník, cached 5 min) scaled by `actual / BoM header amount`.
   2. Stock pre-check against Flexi stock-to-date per warehouse (always on, see Logic).
   3. Lots: Flexi lots of each lot-tracked ingredient with amount > 0; FEFO allocation.
   4. **Issue document** `V-VYDEJ-MATERIAL` (direction Out) per source warehouse; unit price = Flexi stock price on the posting date.
   5. **Receipt document** `V-PRIJEM-POLOTOVAR` (In) into warehouse 20, unit price = total issued cost ÷ total semi-product amount, lot + expiration of the order.
3. Store document codes on `ManufactureOrders` (`DocMaterialIssueForSemiProduct`, `DocSemiProductReceipt` + `…Date`), then move the order to SemiProductManufactured with
   `ErpOrderNumberSemiproduct` = the order number and a note.

**Phase B — products (`ConfirmProductCompletionWorkflow`)**
1. Save `ActualQuantity` (pieces) on each `ManufactureOrderProducts` row.
2. Residue distribution (multi-phase only, see Logic) → if outside the allowed residue % and the
   user has not confirmed, return `requiresConfirmation` + the distribution; the UI asks and
   re-sends with `overrideConfirmed=true`.
3. `SubmitManufactureRequest` (type `Product`): items = product rows with qty > 0, excluding the
   "direct semi-product output" row (code = semi-product code, multi-phase only) → per-product path:
   1. Requirements per product from each product's BoM, scaled by `pieces / BoM header amount`;
      the semi-product line is replaced by the distribution's `AdjustedConsumption`.
   2. Stock pre-check on the **sum** of all products' requirements.
   3. Lots loaded once and drawn down product by product (FEFO), so two products sharing a material never get the same lot quantity.
   4. **Issue documents**, one per source warehouse: `V-VYDEJ-POLOTOVAR` (warehouse 20) and/or `V-VYDEJ-MATERIAL` (warehouse 5, packaging/labels etc.).
   5. **Receipt document** `V-PRIJEM-VYROBEK` (In) into warehouse 4 with all products; per product unit price = its consumed cost ÷ its pieces; lot/expiration from the semi-product row.
   6. If the direct output amount > 0: **issue document** `V-VYDEJ-POLOTOVAR` from warehouse 20 for the bulk sold as-is.
4. Store codes (`DocSemiProductIssueForProduct`, `DocMaterialIssueForProduct`, `DocProductReceipt`, `ErpDiscardResidueDocumentNumber`).
5. On success, for each product in the distribution: `UpdateBoMIngredientAmount` → Flexi
   kusovník line (product, semi-product ingredient) amount = `AdjustedGramsPerUnit`.
6. Move the order to Completed (`ErpOrderNumberProduct`, `WeightWithinTolerance`, `WeightDifference`, note) — this also writes
   the products into sklad výroby (`flow-manufactured-inventory`) and captures room conditions (`sync-manufacture-conditions`).

Every document carries: `Note` = order number (MO-YYYY-NNN), `Description` = manufacture name
(below), accounting + issue date = confirmation time (UTC), `CreatedBy` = current user.

## Logic & formulas
- **Warehouses** (`FlexiStockClient`): 5 = material, 20 = semi-products, 4 = products. An
  ingredient's warehouse comes from its Flexi product type (Material → 5, SemiProduct → 20,
  Product/Goods → 4, anything else → 5). BoM lines whose type is undefined are ignored.
- **Manufacture name** (`ManufactureNameBuilder`, max 40 chars): phase A `"{first 6 chars of semi code}M {short name}"`;
  phase B `"{6 chars} {short name}"`, or just the semi-product code if every product row is the
  semi-product itself. Phase-B description is `"{code} - {name}"` when exactly one product.
- **Scaling**: required amount = BoM line amount × (manufactured amount ÷ BoM header amount), in the BoM's units (grams for bulk, pieces for packaging).
- **FEFO**: lots sorted by expiration (none = last), then lot id; tolerance 0.001 — a remainder above it throws an allocation error.
  Items without lot tracking (HasLots from Flexi stock) are issued as one line without lot.
- **Amounts** are truncated (not rounded) to 6 decimals before sending, so Flexi never gets more than the lot holds.
- **Stock pre-check** (`ValidateIngredientStock = true` since #4304, 2026-09-24): required − available
  stock > 0.001 for any ingredient → nothing is posted, error `ManufactureInsufficientMaterialStock`
  with a Czech list of shortages, and the order **stays in its state**. It exists because Flexi
  saves an issue line it cannot cover with 0 issued and still reports success.
- **Cost**: issue price = Flexi stock-to-date unit price of the item (0 if not found).
  Phase A receipt price = Σ(issued qty × price) ÷ Σ semi-product amount. Phase B receipt price
  per product = that product's issued cost ÷ its pieces. Prices are without VAT (stock prices).
- **Residue distribution** (`ResidueDistributionCalculator`, multi-phase only, skipped when
  every product row is the semi-product): actual bulk = semi-product actual qty − direct-output
  qty; theoretical per product = BoM semi-product grams per piece × actual pieces;
  difference = actual − Σ theoretical; % = |difference| ÷ Σ theoretical × 100; within threshold
  if % ≤ the semi-product's `AllowedResiduePercentage` (Flexi product attribute via catalog; 0 if
  missing, so any difference asks). Each product gets `actual bulk × its theoretical share`
  (rounded to 4 dp, rounding remainder added to the largest), and
  `AdjustedGramsPerUnit = AdjustedConsumption ÷ pieces` — that is what is issued and written to the BoM.
- **Failure handling**: a shortage (pre-check or FEFO) returns an error and changes nothing.
  Any other ERP failure (Flexi rejects a document, circuit breaker open, BoM write fails) still
  moves the order forward, with `ManualActionRequired = true` and the Czech error
  (`ManufactureErrorTransformer` filters) as a note. Staff fix Flexi by hand and clear the flag
  with "Resolve manual action" (`flow-manufacture-order`). A timeout returns an error without a state change.
- **Timeout & breaker**: each submit is cancelled after `ErpTimeoutSeconds`; a Polly circuit
  breaker opens when ≥ 50 % of ≥ 2 calls in 15 min failed (HTTP error or own timeout) and then
  fails fast for 30 s (`ErpCircuitOpenFilter` message).

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `ManufactureErp:ErpTimeoutSeconds` | 60 | Per-submit timeout; 0 disables |
| `ManufactureErp:ErpCircuitBreakerMinimumThroughput` | 2 (class default) | Calls in the window before the ratio counts |
| `ManufactureErp:ErpCircuitBreakerFailureRatio` | 0.5 | Failure share that opens the breaker |
| `ManufactureErp:ErpCircuitBreakerSamplingDurationSeconds` | 900 | Window for the ratio |
| `ManufactureErp:ErpCircuitBreakerBreakDurationSeconds` | 30 | Fail-fast period |
| Flexi product attribute "allowed residue %" | per semi-product | Residue tolerance (via catalog `Properties.AllowedResiduePercentage`) |
| Template cache TTL | 5 min (constant in `ManufactureTemplateCache`) | BoM + HasLots snapshot per product code |

## Runtime facts
- Heblo's `V-VYDEJ-*` documents started at full volume on 2026-03-25 — memory note gotcha_flexi_issue_doc_silently_skips_short_lines — 2026-10-01.
- Before the pre-check existed, an audit found 188 issue lines / 113 docs / 91 orders since 2026-03-25 where Flexi issued 0 (e.g. MO-2026-404 / M-00584/2026, labels ETI098 + ETI274, 700 on stock vs 728 needed); on re-evaluation only ~5 lines (EMU016, EOL049) + 8 pending lines still need manual correction — same memory note — 2026-10-01.

## Known quirks
- **Flexi "success" can hide an unissued line.** The pre-check only compares totals at
  submission time; a race (another document consuming the same stock between check and save)
  can still produce a line with `mnozMjPlan > mnozMj`. Heblo does not read the document back.
- **Partial postings are not rolled back.** Documents are saved one by one (issue per warehouse,
  then receipt, then bulk issue). If a later one fails, the earlier ones stay in Flexi; the
  order moves on with `ManualActionRequired` but the codes of the documents that *did* save are
  not stored (codes are persisted only on full success) — look them up in Flexi by the order number in the note field.
- **Circuit-breaker-open still advances the order.** When the breaker is open no call is made,
  yet the workflow treats it like any ERP failure: the order moves to SemiProductManufactured /
  Completed with `ManualActionRequired`, and on Completed the products are written into sklad
  výroby although nothing was received in Flexi.
- **A timeout leaves the outcome unknown.** The request is cancelled after 60 s, the order keeps
  its state, but Flexi may have saved some documents. Confirming again can post them twice —
  check Flexi for documents with the order number before retrying.
- **Reverting the order does not reverse Flexi.** Going back from Completed or
  SemiProductManufactured (allowed by the state machine) cancels no document; confirming again
  posts a second set.
- **A Product/Goods ingredient breaks the issue step (latent).** Such a line is grouped under
  warehouse 4, but `GetConsumptionDocumentType` knows only warehouses 5 and 20 and throws
  "Unknown warehouse" — after earlier warehouse groups may already be saved. No current BoM is
  known to contain one (read from code, not observed).
- **BoM rewrite is per order.** Each multi-phase completion overwrites the product's
  semi-product grams per piece with this batch's actual average, so one bad weighing changes the
  recipe used by the batch calculator, batch planning and later postings. Failures are listed
  in the order note (`BoM update failures: …`) and set `ManualActionRequired`.
- **Single-phase orders** have a placeholder semi-product row pointing at the first product;
  residue distribution and direct output are skipped for them.
- **`SetBomItemsOrderAsync` is obsolete**; ordering + phase labels of BoM lines are written by
  `SetBomItemsOrderAndPhaseAsync`, called by Catalog's `UpdateProductCompositionOrder`, not by this flow.
  The same `IManufactureClient` also serves Catalog (`FindByIngredientAsync` for product usage)
  and Logistics (`GetSetPartsAsync` for gift packages).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs` — phase A
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs` — phase B, override, BoM update, notes
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ResidueDistributionCalculator.cs` — residue % and adjusted grams
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs` — timeout, breaker, doc-code persistence, shortage error
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs` — aggregated vs per-product posting
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureDocumentService.cs` — document types, warehouses, prices
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FefoConsumptionAllocator.cs` — FEFO
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiIngredientStockValidator.cs` — pre-check
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs` — BoM read, phase type, HasLots
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureErpResilienceService.cs` — circuit breaker
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/ManufactureErrorTransformer.cs` — Czech error messages
