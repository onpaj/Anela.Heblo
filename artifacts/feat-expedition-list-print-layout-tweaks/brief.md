# Expedition list print — layout tweaks

## Context

The warehouse-picking PDF (`Objednávky k expedici – {Carrier}`) is generated server-side with QuestPDF. Two cosmetic problems on the per-order pages (the "first" portion of the document):

1. **Customer / internal notes are at the top of each order block** (right under the customer line, above the items table). The picker has to read them, then look down at the items, then scroll back up to confirm — confusing flow.
2. **The per-order items table has a `Pozice` column.** The position is already shown on the summary "second page" which is the actual picking guide. On the per-order page the picker would rather see `Cena` (item price) — useful for verifying the order value at a glance.

The aggregated summary page (after the explicit `PageBreak()`) must remain unchanged: it keeps `Pozice` because that's where the picker walks the warehouse.

User-confirmed decisions:
- Use existing `ExpeditionOrderItem.UnitPrice` (already populated from Shoptet `itemPriceWithVat`). No data-layer change.
- Format: `"299 Kč"` — cs-CZ culture, whole koruna, no decimals.
- Set components (`UnitPrice == 0`, hard-coded for set sub-items): render an empty cell.

## Scope

Single-file change. No DTO change, no data source change, no migrations, no API change, no frontend change.

**File to modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs`

## Implementation

### 1. Move notes below the items table

In `ComposeOrderBlock` (currently lines 68–115):

Current order inside the order box:
1. Heading (`Objednávka {Code}`)
2. Barcode
3. Customer line (right-aligned)
4. **Notes** ← currently here (lines 93–107)
5. Items table

New order:
1. Heading
2. Barcode
3. Customer line
4. Items table
5. **Notes** ← move here, with a small `PaddingTop(2)` separator above

Notes block stays conditional on at least one of `CustomerRemark` / `EshopRemark` being non-empty. Italic, font 8 — unchanged.

### 2. Swap `Pozice` → `Cena` on the per-order items table only

In `BuildItemsTable` (lines 151–211):

- Replace the `PoziceCol` reference in `ColumnsDefinition` with a new `CenaCol = 2f` constant declared next to the other column constants (lines 19–23). Width 2f is enough for `"1 999 Kč"`.
- Header cell text: `"Pozice"` → `"Cena"` (line 170). Keep `HeaderCellCenter` style.
- Regular item cell (lines 186–187): replace `item.WarehousePosition ?? string.Empty` with `FormatPrice(item.UnitPrice)`.
- Set component cell (lines 204–205): replace `item.WarehousePosition ?? string.Empty` with `FormatPrice(item.UnitPrice)` — for set sub-items `UnitPrice == 0m`, so the helper returns empty (matches user choice).

`BuildSummaryTable` (lines 213–261) is **untouched** — the summary page keeps `Pozice`.

### 3. New `FormatPrice` helper

Add next to `FormatAmount` / `FormatVariant`:

```csharp
private static readonly CultureInfo CzechCulture = CultureInfo.GetCultureInfo("cs-CZ");

private static string FormatPrice(decimal price) =>
    price == 0m ? string.Empty : $"{price.ToString("N0", CzechCulture)} Kč";
```

`"N0"` gives whole-koruna grouping using cs-CZ separators (`1 234 Kč`). Empty string for zero handles set sub-items per the agreed UX.

`using System.Globalization;` is already implicitly available via existing `decimal.TryParse` usages elsewhere — add the directive at the top of the file if missing.

## Reuse / non-changes

- `ExpeditionOrderItem.UnitPrice` already exists and is already populated from `itemPriceWithVat` (`ShoptetApiExpeditionListSource.cs:267`). No mapping changes.
- Set sub-items already get `UnitPrice = 0m` (`ShoptetApiExpeditionListSource.cs:286`). No special branching needed in the document — the formatter handles the zero case.
- The summary page renders position from `SummaryRow.WarehousePosition` (lines 244, 255) — left alone. `SummaryRow` does not need a price field.

## Verification

1. **Build & format:** `dotnet build` and `dotnet format` from repo root must succeed.
2. **Visual smoke test of the PDF** (no E2E, no unit tests for QuestPDF rendering exist):
   - Trigger the existing print pipeline. Easiest path: run the `PrintPickingListJob` once locally, or hit the endpoint that calls `ExpeditionListService.PrintPickingListAsync` (the file-system sink at `FileSystemPrintQueueSink.cs` writes the PDF to disk for inspection).
   - Open the resulting PDF and confirm on the **per-order pages**:
     - Items table column header now reads `Cena`.
     - Regular rows show prices like `299 Kč`; set sub-rows show empty `Cena` cell.
     - `Poznámka zákazníka` / `Interní poznámka` blocks (when present) appear **below** the items table, not above.
     - Notes block is omitted when both remarks are empty.
   - Confirm on the **summary page** (after the page break, titled `Položky objednávek`):
     - Column header is still `Pozice`.
     - Position values render as before.
3. **Regression checks:** existing test suite — no test currently covers `ExpeditionProtocolDocument`, but `dotnet test` should still pass (sanity check).

## Out of scope

- Adding a price column to the summary page.
- Sorting the per-order rows (currently iteration order — unchanged).
- Catalog selling-price enrichment (would only matter if we wanted set-component prices; user agreed empty cell is correct).
- Frontend changes (the React archive page only lists previously-generated PDFs).
