# Specification: Expedition list print — layout tweaks

## Summary
Adjust the warehouse-picking PDF (`Objednávky k expedici – {Carrier}`) so that on the per-order pages, customer/internal notes appear below the items table and the items table shows `Cena` (item price) instead of `Pozice`. The aggregated summary page that follows the page break is unchanged: it still shows `Pozice` because that section is the picker's walk-through guide.

## Background
The expedition protocol PDF is produced server-side by QuestPDF in `ExpeditionProtocolDocument.cs`. Pickers in the warehouse use it to verify orders and walk the floor. Two cosmetic issues impede the workflow:

1. Notes (customer remark, internal/eshop remark) currently render above the items table. Pickers read the notes, look down at items, then look back up — an awkward visual flow. Moving notes below the items table aligns the reading path with the natural top-to-bottom order.
2. The `Pozice` column on the per-order page duplicates information already shown on the aggregated summary page (the picker's actual walk-the-warehouse guide). Per-order, pickers would rather see `Cena` to sanity-check order value.

The data needed for `Cena` already exists: `ExpeditionOrderItem.UnitPrice` is populated from Shoptet's `itemPriceWithVat`. Set sub-items already carry `UnitPrice == 0m` and should render as an empty cell.

## Functional Requirements

### FR-1: Move notes block below the items table on per-order pages
Within `ComposeOrderBlock` in `ExpeditionProtocolDocument.cs`, change the per-order block layout so the notes block (containing `CustomerRemark` and/or `EshopRemark`) renders **after** the items table instead of before it.

New per-order block order:
1. Heading (`Objednávka {Code}`)
2. Barcode
3. Customer line (right-aligned)
4. Items table
5. Notes block

Visual styling of the notes block (italic, font 8, conditional rendering when at least one remark is non-empty) is preserved. A small `PaddingTop(2)` separator must sit above the notes block to visually separate it from the items table.

**Acceptance criteria:**
- On per-order pages, when a `CustomerRemark` and/or `EshopRemark` is set, the corresponding `Poznámka zákazníka` / `Interní poznámka` text appears below the items table.
- When both `CustomerRemark` and `EshopRemark` are empty/null, the notes block is not rendered at all.
- Notes styling (italic, font size 8) is unchanged from current output.
- A small vertical gap (`PaddingTop(2)`) separates the items table from the notes block.

### FR-2: Replace `Pozice` with `Cena` on the per-order items table
In `BuildItemsTable`, swap the `Pozice` column for a `Cena` column on the per-order items table. The header cell text changes from `"Pozice"` to `"Cena"` (centered, using existing `HeaderCellCenter` styling). Each row cell renders the formatted price via the new `FormatPrice` helper applied to `item.UnitPrice`.

Column constant: introduce `CenaCol = 2f` declared alongside the other column-width constants. Width `2f` is sufficient for values up to `"1 999 Kč"`.

`BuildSummaryTable` (used by the post-page-break summary) is **not modified**: that table keeps `Pozice` and `SummaryRow.WarehousePosition` rendering.

**Acceptance criteria:**
- Per-order items table header reads `Cena` instead of `Pozice`, with header centered.
- Regular item rows display prices formatted as `"299 Kč"` (cs-CZ grouping, whole koruna, no decimals; e.g., `"1 234 Kč"`).
- Set sub-item rows (where `UnitPrice == 0m`) display an empty cell in the `Cena` column.
- The summary page (after `PageBreak()`, titled `Položky objednávek`) still shows the `Pozice` column with `WarehousePosition` values rendered exactly as before.

### FR-3: Add `FormatPrice` helper
Add a private static helper in `ExpeditionProtocolDocument` next to `FormatAmount` / `FormatVariant`:

```csharp
private static readonly CultureInfo CzechCulture = CultureInfo.GetCultureInfo("cs-CZ");

private static string FormatPrice(decimal price) =>
    price == 0m ? string.Empty : $"{price.ToString("N0", CzechCulture)} Kč";
```

Add `using System.Globalization;` to the file's using directives if it is not already present.

**Acceptance criteria:**
- `FormatPrice(0m)` returns `string.Empty`.
- `FormatPrice(299m)` returns `"299 Kč"`.
- `FormatPrice(1999m)` returns `"1 999 Kč"` (cs-CZ grouping separator).
- Helper is reachable only from `ExpeditionProtocolDocument` (private static).

## Non-Functional Requirements

### NFR-1: Performance
PDF generation latency must be unchanged — this is a pure layout/string-formatting change, no additional data fetches or N+1 work. Per-row work adds at most one `decimal.ToString("N0", CzechCulture)` call.

### NFR-2: Security
No new endpoints, no new inputs from external sources, no auth surface changes. The change touches only PDF rendering of data already loaded by the existing pipeline.

### NFR-3: Localization
Price formatting uses `CultureInfo.GetCultureInfo("cs-CZ")` explicitly, independent of the host process culture (the production container may not have cs-CZ as default), so output is deterministic across environments.

### NFR-4: Backward compatibility
Previously generated PDFs in storage are not regenerated. Only newly generated PDFs reflect the new layout. No data migration is required.

## Data Model
No data model changes.

Existing types referenced (read-only):
- `ExpeditionOrderItem.UnitPrice` (decimal) — already populated from Shoptet `itemPriceWithVat` in `ShoptetApiExpeditionListSource.cs`. Set sub-items are already assigned `UnitPrice = 0m`.
- `ExpeditionOrderItem.WarehousePosition` — still used by `BuildSummaryTable` via `SummaryRow.WarehousePosition`; no longer used by `BuildItemsTable`.
- `ExpeditionOrder.CustomerRemark`, `ExpeditionOrder.EshopRemark` — still consumed by the relocated notes block.

`SummaryRow` is unchanged and does not need a price field.

## API / Interface Design
No API surface changes. No new endpoints, controllers, MediatR handlers, DTOs, or events. No frontend work.

The only observable change is the visual layout of the generated PDF binary returned by the existing print pipeline:
- `ExpeditionListService.PrintPickingListAsync`
- `PrintPickingListJob` (scheduled job)
- `FileSystemPrintQueueSink` (local development sink — used for visual smoke testing)

## Dependencies
- **QuestPDF** — already in use, no version change required.
- `System.Globalization` (BCL) — used for the `cs-CZ` `CultureInfo`.

No new packages, no service dependencies, no migrations.

## File Scope
Single file modified:
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs`

## Verification

1. **Build & format:** `dotnet build` and `dotnet format` from repo root must succeed.
2. **Existing tests:** `dotnet test` passes. (No tests currently cover `ExpeditionProtocolDocument` directly; this is a sanity check for regressions elsewhere.)
3. **Visual smoke test of the PDF:**
   - Trigger the existing print pipeline (e.g., run `PrintPickingListJob` locally, or invoke the endpoint that calls `ExpeditionListService.PrintPickingListAsync`). The local `FileSystemPrintQueueSink` writes the PDF to disk for inspection.
   - Open the resulting PDF and confirm:
     - **Per-order pages:**
       - Items table header reads `Cena` (centered).
       - Regular rows show prices like `299 Kč` / `1 234 Kč`; set sub-item rows show an empty `Cena` cell.
       - `Poznámka zákazníka` / `Interní poznámka` blocks (when present) appear **below** the items table, not above.
       - Notes block is omitted entirely when both remarks are empty/null.
     - **Summary page** (after `PageBreak()`, titled `Položky objednávek`):
       - Column header is still `Pozice`.
       - Position values render as before.

## Out of Scope
- Adding a price column to the aggregated summary page.
- Sorting/reordering of per-order rows (current iteration order is preserved).
- Enriching set-component sub-items with catalog selling prices (the agreed UX is an empty cell for set sub-items).
- Changes to data sources, mappers, DTOs, the API surface, or the frontend.
- Database migrations or backfills.
- Regenerating PDFs that already exist in storage.

## Open Questions
None.

## Status: COMPLETE