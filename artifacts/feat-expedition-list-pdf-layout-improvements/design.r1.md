# Design: Expedition List PDF Layout Improvements

## Component Design

### `ExpeditionProtocolDocument` (single modified file)

**File:** `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs`

No new files. All changes are internal to this class. Public surface is unchanged.

---

### Class structure (top to bottom)

```
ExpeditionProtocolDocument
│
├── Layout Constants (NEW — top of class)
│   ├── BorderThickness  : float  = 1.5f
│   ├── BorderPadding    : float  = 4f
│   ├── OrderGap         : float  = 6f
│   ├── VariantFontSize  : float  = 7f
│   ├── VariantColor     : string = Colors.Grey.Darken1
│   └── Column ratios: KodCol=2, PopisCol=8, MnozstviCol=1.5, PoziceCol=2, StavCol=2
│
├── Public surface (UNCHANGED)
│   ├── ctor(ExpeditionProtocolData data)
│   ├── static byte[] Generate(ExpeditionProtocolData data)
│   ├── void Compose(IDocumentContainer)   — orchestration only; delegates to private methods
│   ├── GetMetadata()
│   └── GetSettings()
│
├── Private composition methods (NEW — extracted from Compose)
│   ├── ComposeOrderBlock(IContainer container, ExpeditionOrder order)
│   │   Wraps one order in Border→Padding→Column; calls BuildItemsTable
│   ├── ComposeSummaryPage(IContainer container)
│   │   Renders the summary page; calls BuildSummaryTable
│   ├── BuildItemsTable(IContainer container, IEnumerable<ExpeditionOrderItem> items)
│   │   5-column table (Kód, Popis, Množství, Pozice, Stav); handles regular + set rows
│   └── BuildSummaryTable(IContainer container, IEnumerable<SummaryRow> rows)
│       5-column table with same shape; uses aggregated SummaryRow
│
├── Shared description cell renderer (NEW)
│   └── static void RenderDescriptionCell(IContainer cell, string name, string? variant, bool italic)
│       Single rendering path for all three callsites (per-order regular, per-order set, summary)
│
├── Cell-style primitives (EXTRACTED from duplicate locals → class-level private static)
│   ├── static IContainer HeaderCell(IContainer c)
│   ├── static IContainer HeaderCellCenter(IContainer c)
│   ├── static IContainer DataCell(IContainer c)
│   ├── static IContainer CenteredDataCell(IContainer c)
│   └── static IContainer SetHeaderCell(IContainer c)
│
└── Existing helpers (UNCHANGED)
    ├── static string FormatAmount(decimal qty, string? unit)
    ├── static string FormatVariant(string? variant)
    └── static byte[] GenerateBarcode(string code)
```

---

### Component responsibilities

| Component | Responsibility |
|-----------|---------------|
| `Compose` | Orchestration only: iterates orders → `ComposeOrderBlock`; appends `ComposeSummaryPage` |
| `ComposeOrderBlock` | Wraps one `ExpeditionOrder` in `Border(1.5f).BorderColor(Grey.Darken2).Padding(4)`; removes old `LineHorizontal` divider; adds `PaddingBottom(OrderGap)` between boxes |
| `BuildItemsTable` | 5-column table; splits items into regular / set groups; routes description cells through `RenderDescriptionCell` |
| `BuildSummaryTable` | 5-column table over aggregated `SummaryRow` list; routes description cells through `RenderDescriptionCell` |
| `RenderDescriptionCell` | Emits primary name line + conditional variant line (smaller font, muted color); applies italic to both lines when `italic=true` |
| Cell-style primitives | Border + padding style per cell role; shared by both tables |

---

### `RenderDescriptionCell` contract

```csharp
private static void RenderDescriptionCell(
    IContainer cell,
    string name,
    string? variant,
    bool italic)
```

Behaviour:
- Renders `name` as the primary line at default body size (8pt).
- Calls `FormatVariant(variant)`. If the result is non-empty, renders it as a second line at `VariantFontSize` in `VariantColor`. If empty, no second line and no whitespace are emitted.
- When `italic == true`, italic is applied to both lines.
- Must be the **only** place in the class that renders a description cell — no per-callsite duplication is permitted (FR-5 intent from arch-review amendment).

---

### Internal aggregate type

Used inside `BuildSummaryTable`; not exposed publicly.

```csharp
private sealed class SummaryRow
{
    public string ProductCode      { get; init; }
    public string Name             { get; init; }
    public string? Variant         { get; init; }
    public string? WarehousePosition { get; init; }
    public string? Unit            { get; init; }
    public decimal TotalQuantity   { get; init; }
    public int StockCount          { get; init; }
    public bool IsFromSet          { get; init; }
}
```

Aggregation: `_data.Orders.SelectMany(o => o.Items).GroupBy(i => i.ProductCode)`, summing `Quantity`, taking first `Name`/`Variant`/`WarehousePosition`/`Unit`, setting `IsFromSet` if any item in the group has `IsFromSet == true`. Sorted by `WarehousePosition` (unchanged from current logic).

---

## Data Schemas

### Existing data model (no changes)

```csharp
// Input — unchanged
class ExpeditionProtocolData
{
    string CarrierDisplayName { get; }
    IReadOnlyList<ExpeditionOrder> Orders { get; }
}

class ExpeditionOrder
{
    string Code              { get; }
    string CustomerName      { get; }
    string Address           { get; }
    string Phone             { get; }
    string? CustomerRemark   { get; }
    string? EshopRemark      { get; }
    IReadOnlyList<ExpeditionOrderItem> Items { get; }
}

class ExpeditionOrderItem
{
    string ProductCode        { get; }
    string Name               { get; }
    string? Variant           { get; }   // already populated; FormatVariant() normalises
    decimal Quantity          { get; }
    string? Unit              { get; }
    string? WarehousePosition { get; }
    int StockCount            { get; }
    string? SetName           { get; }
    bool IsFromSet            { get; }
}
```

### Public interface (no changes)

```csharp
// Unchanged — only output PDF bytes; byte[] shape is identical
public static byte[] Generate(ExpeditionProtocolData data)
```

### Layout constants schema

```csharp
// All private const / private static readonly at top of class
private const float  BorderThickness  = 1.5f;
private const float  BorderPadding    = 4f;
private const float  OrderGap         = 6f;
private const float  VariantFontSize  = 7f;
private static readonly string VariantColor = Colors.Grey.Darken1;

// Column relative widths — final values may be tuned visually on A4
private const float KodCol       = 2f;
private const float PopisCol     = 8f;
private const float MnozstviCol  = 1.5f;
private const float PoziceCol    = 2f;
private const float StavCol      = 2f;
```

### PDF column shape (both tables — after change)

| Column | Header (CZ) | Relative width | Notes |
|--------|-------------|----------------|-------|
| 1 | Kód | 2 | Product code |
| 2 | Popis položky | 8 | Name + optional variant line; wraps; was split across 2 columns |
| 3 | Množství | 1.5 | Quantity (11pt bold) |
| 4 | Pozice | 2 | Warehouse position |
| 5 | Stav skladu | 2 | Stock count |

**Removed:** column 3 `Varianta` (relative width 3) present in the current 6-column layout.

`Sada: {SetName}` sub-header `ColumnSpan` changes from `6` → `5`.

### Verification prerequisite (no new automated tests required)

Before merging: grep `backend/test` for any reference to `ExpeditionProtocolDocument` or `.Generate(`. If a snapshot/golden-PDF baseline exists, regenerate it in the same PR. If none exists, perform a manual visual review of a generated sample PDF covering: ≥1 multi-item order, ≥1 set, ≥1 item with a long name, ≥1 item with no variant, ≥2 orders (to verify box spacing and page breaks).