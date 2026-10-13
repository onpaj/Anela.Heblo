---
process: flow-manufacture-pdfs
kind: workflow
module: manufacture
summary: Generates the two manufacture PDFs — the batch protocol of a completed order (quantities, lots, room conditions, notes and the exact Flexi document lines with consumed lots) and the semi-product recipe sheet (ingredients scaled to a batch, full and half batch, by phase).
owns:
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/**
  - backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetSemiproductRecipePdf/**
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureErpDocumentItem.cs
  - backend/src/Anela.Heblo.API/PDFPrints/**
verified_at: "5e993f9e2"
related:
  - flow-manufacture-order
  - feed-manufacture-to-flexi
  - sync-manufacture-conditions
  - calc-batch-calculation
---

# Manufacture protocol and recipe PDFs

## Purpose
- **Manufacture protocol** (výrobní protokol) — the batch record of one completed order for
  quality/traceability: what was planned and actually made, lots and expirations, the room
  conditions at each stage, all notes, and every Flexi stock document with its lines — which
  material lots were consumed and which lots were received. Printed from the order detail
  ("Tisk protokolu", only for Completed orders).
- **Semi-product recipe** (receptura) — a sheet for the person mixing the bulk: each
  ingredient's amount for the chosen batch size and for half of it, its share in %, grouped
  by phase letter. Printed from the batch calculator.

Nothing is stored or sent anywhere; the PDF is streamed to the browser.

## Trigger
On demand:
- `GET /api/ManufactureOrder/{id}/protocol.pdf` (permission `Manufacture_ManufactureOrders`) →
  `ManufactureProtocol-{orderNumber}.pdf`; refused with `ManufactureOrderNotCompleted` unless the order is Completed.
- `GET /api/manufacture-batch/recipe-pdf/{productCode}?batchSize=` (permission
  `Manufacture_BatchPlanning`) → `receptura-{productCode}.pdf`.

## Data flow
**Protocol** (`GetManufactureProtocolHandler` → `QuestPdfManufactureProtocolRenderer`):
1. Order with semi-product, products, notes and conditions readings from the Heblo DB.
2. For each stored document code on the order — material issue (polotovar), semi-product
   receipt, semi-product issue (výrobek), material issue (výrobek), product receipt — read its
   lines from Flexi stock movements (`IManufactureClient.GetErpDocumentItemsAsync`, code filter,
   date window now − 5 years … now + 1 day), in parallel: product code, name, amount, lot, expiration.
3. Product name suffixes (Flexi `cenik.popisC`) for all codes on those lines from the catalog.
4. Render: header, conditions table (stage, inner/outer T and RH, time, source suffix),
   semi-product and products tables, Flexi documents with lines, notes.

**Recipe** (`GetSemiproductRecipePdfHandler` → `QuestPdfSemiproductRecipeRenderer`):
1. Flexi BoM of the product (5-min template cache) + catalog MMQ and expiration months.
2. Scale factor = `batchSize ÷ BoM header amount` (1 when no batch size); per ingredient full =
   round(amount × factor, 3), half = full ÷ 2, % = BoM amount ÷ Σ BoM ingredient amounts × 100.
3. Sort by Flexi order (unordered last), then name; group rows under "Fáze A/B/…" headings.

## Logic & formulas
- Protocol amounts are exactly what Flexi holds on the documents (not the Heblo order
  quantities), so a line Flexi did not issue shows up as missing or 0 there.
- Recipe % uses the unscaled BoM amounts and the sum of ingredient lines (not the header
  amount), so it adds up to 100 % even if the BoM header differs from the ingredient total.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| — | — | No settings; QuestPDF renderers registered in `ServiceCollectionExtensions` |

## Runtime facts
None.

## Known quirks
- **The bulk-sale document is not printed.** The direct semi-product output issue
  (`ErpDiscardResidueDocumentNumber`) is not among the documents the protocol reads.
- **Documents saved before a failure are missing** — only codes persisted on the order are
  printed; after a partial Flexi posting (see `feed-manufacture-to-flexi`) the protocol is incomplete.
- **Times are printed as stored (UTC)** — "Vygenerováno", conditions "Zaznamenáno" and note
  times are not converted to Prague time, so they read 1–2 h early (read from code, not observed).
- The protocol is available only in Completed; an order reverted from Completed loses access until it is completed again.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs` — data assembly, Flexi document lines
- `backend/src/Anela.Heblo.API/PDFPrints/ManufactureProtocolDocument.cs` — protocol layout
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetSemiproductRecipePdf/GetSemiproductRecipePdfHandler.cs` — recipe scaling
- `backend/src/Anela.Heblo.API/PDFPrints/SemiproductRecipeDocument.cs` — recipe layout
