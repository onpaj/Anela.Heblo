# Feature Brief: Expedition List PDF Layout Improvements

## Problem Statement
The current expedition list PDF is visually harder to read during picking — individual orders are not clearly separated, and the product variant is shown in a dedicated column instead of beneath the product name. Workers comparing the printout to the photo reference find the old layout more efficient.

## Goals
- Each order block is visually separated by a clear bordered box, matching the orange-bordered style of old expedition lists.
- The variant (e.g. "30 ml") appears beneath the product name inside the "Popis položky" cell, not in a separate column.

## Functional Requirements
- **Order border**: Wrap each per-order section (from order heading through the items table) in a visible rectangular border (1–2pt, dark grey or black). The border must encompass the full order block, not just the table.
- **Variant under product name**: Remove the standalone "Varianta" column from the items table. Render the variant as a smaller, secondary line of text directly beneath the product name within the "Popis položky" cell. Apply consistently to both regular items and set components, and to the summary page.
- **No column count change on summary page**: The summary page should also lose the Varianta column, with variant shown inline under the product name, keeping column widths adjusted accordingly.

## Non-Functional Requirements
- PDF layout must remain within A4 margins with no overflow.
- Font sizes and spacing should stay consistent with the existing design (body 8pt, quantity 11pt bold).

## Technical Constraints
- PDF generated via QuestPDF in `ExpeditionProtocolDocument.cs` (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/`).
- Data model in `ExpeditionProtocolData.cs` — `ExpeditionOrderItem.Variant` already carries the variant string.
- `FormatVariant()` helper already strips the "Obsah: " prefix — reuse it.
- Stock already uses `Stock.Eshop` (line 132 of `ShoptetApiExpeditionListSource.cs`) — no change needed.
- Notes already propagate (`CustomerRemark`, `EshopRemark`) — no change needed.

## Out of Scope
- Truncating the product description/name (showing only the short marketing name without subtitle).
- Any changes to the summary page logic or sorting.
- Frontend or API changes.

## Success Criteria
- Each order on the PDF is enclosed in a visible rectangle, making order boundaries unambiguous at a glance.
- Variant text appears on a second line under the product name, using smaller font (≤7pt), in all tables (per-order and summary).
- The "Varianta" header column is gone.
- Build passes (`dotnet build`) with no warnings introduced.
- Existing MCP and expedition list tests continue to pass.

## Additional Context
File to modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs`

Current order block structure (lines 49–173):
- `col.Item().Column(orderCol => { ... })` with no outer border
- Items table has 6 columns: Kód, Popis položky, Varianta, Množství, Pozice, Stav skladu

Target structure:
- Wrap `orderCol` in a `.Border(1.5f).BorderColor(Colors.Grey.Darken2).Padding(4)` container
- Merge "Popis položky" + "Varianta" → single cell rendering `item.Name` then `FormatVariant(item.Variant)` on a new line with smaller font
- Drop from 6 columns to 5: Kód, Popis položky (+ varianta), Množství, Pozice, Stav skladu
- Adjust relative column widths to compensate for removed Varianta column