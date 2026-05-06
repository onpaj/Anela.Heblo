The implementation is complete and committed.

---

# Implementation: Expedition list per-order page layout tweaks

## What was implemented

Three changes to `ExpeditionProtocolDocument.cs`:

**FR-1** — Notes block moved below the items table in `ComposeOrderBlock`. The old `PaddingTop(2)` spacer between customer line and items was removed; the `PaddingTop(2)` separator now sits above the notes block instead.

**FR-2** — Per-order items table `Pozice` column replaced with `Cena`. Header text changed, column definition uses new `CenaCol = 2f` constant, and both regular-item and set-sub-item row cells now render `FormatPrice(item.UnitPrice)`. `BuildSummaryTable` is unchanged.

**FR-3** — `FormatPrice` helper added (`price == 0m` → empty string; otherwise `"{N0} Kč"` in cs-CZ culture). `CzechCulture` static field holds the explicit `CultureInfo` instance. `using System.Globalization;` added.

## Files modified

- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` — all three FRs implemented
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ExpeditionProtocolDocumentTests.cs` — 3 new smoke tests added (zero price, non-zero price, notes with remarks)

## Tests

All 12 tests pass (9 pre-existing + 3 new). New tests are smoke tests verifying `Generate()` doesn't throw for: set sub-items with `UnitPrice=0`, regular items with `UnitPrice=1999m`, and orders that have both `CustomerRemark` and `EshopRemark`.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ExpeditionProtocolDocument"
```

For visual verification: trigger `PrintPickingListJob` locally; the `FileSystemPrintQueueSink` writes the PDF to disk. Confirm per-order pages show `Cena` header with prices, notes below the table; summary page retains `Pozice`.

## Notes

`PoziceCol` constant is kept — `BuildSummaryTable` still references it. `CenaCol = 2f` has the same width, preserving total relative column width (15.5f) so the summary table layout is unaffected.

## PR Summary

Adjusts the warehouse-picking PDF per-order page layout: moves customer/internal notes below the items table (aligning the picker's reading flow top-to-bottom), and replaces the `Pozice` column with `Cena` so pickers can sanity-check order value per-item. The aggregated summary page is untouched and retains `Pozice` with warehouse positions. A `FormatPrice` helper formats Czech koruna amounts using explicit `cs-CZ` culture to ensure deterministic output across environments.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` — `FormatPrice` helper, `CenaCol` constant, `ComposeOrderBlock` reordering, `BuildItemsTable` column swap
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ExpeditionProtocolDocumentTests.cs` — 3 smoke tests for zero price, non-zero price, and notes-below-table layout

## Status
DONE