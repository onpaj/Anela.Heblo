### task: add-layout-constants-to-expedition-document

**Goal:** Introduce a centralized block of layout constants at the top of `ExpeditionProtocolDocument` so subsequent layout work has a single source of truth for sizing, spacing, color, and column ratios.

**Context:**

Target file (only file touched by this and all subsequent tasks):
`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs`

The current document hardcodes layout values inline across multiple `Table(...)` lambdas. The redesign requires shared values used by both per-order and summary tables, and by a new bordered order block:

- Outer border around each order block: thickness `1.5f`, color `Colors.Grey.Darken2`.
- Inner padding inside the bordered order block: `4`.
- Vertical gap between consecutive order blocks: `6`.
- Variant sub-line under product name: font size `7f`, color `Colors.Grey.Darken1`.
- Column relative widths (must sum reasonably on A4): `Kód=2`, `Popis=8`, `Množství=1.5`, `Pozice=2`, `Stav=2`.

Public surface (`ctor`, `Generate`, `Compose`, `GetMetadata`, `GetSettings`) is unchanged. No new files.

**Files to create/modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` — add a private constants block at the top of the class.

**Implementation steps:**

1. Open `ExpeditionProtocolDocument.cs`. Locate the class declaration `public class ExpeditionProtocolDocument : IDocument` (or equivalent).
2. Immediately after the opening brace of the class, before any existing fields/constructor, add the following block exactly:

```csharp
// Layout constants — single source of truth for visual tuning.
private const float BorderThickness  = 1.5f;
private const float BorderPadding    = 4f;
private const float OrderGap         = 6f;
private const float VariantFontSize  = 7f;
private static readonly string VariantColor = Colors.Grey.Darken1;

// Column relative widths for both per-order and summary tables.
private const float KodCol      = 2f;
private const float PopisCol    = 8f;
private const float MnozstviCol = 1.5f;
private const float PoziceCol   = 2f;
private const float StavCol     = 2f;
```

3. Ensure `using QuestPDF.Helpers;` (or the namespace exposing `Colors`) is already imported; if not, add it. Do not change any existing logic in this task — only add the constants.
4. Run `dotnet format` against the file.
5. Run `dotnet build` from the backend solution root and confirm zero new warnings/errors.

**Tests to write:**

No new automated tests for this task — adding unused constants has no observable behavior. Verification is the build step in step 5.

**Acceptance criteria:**

- The class contains the constants block above, located at the top of the class body, before existing members.
- `dotnet build` succeeds with no new warnings or errors.
- `dotnet format` reports no diagnostics on the modified file.
- No public API surface changed.

---

### task: extract-cell-style-helpers-to-private-statics

**Goal:** Promote the duplicated cell-style local functions (`HeaderCell`, `HeaderCellCenter`, `DataCell`, `CenteredDataCell`, `SetHeaderCell`) from inside the table-rendering lambdas to class-level `private static` methods so they are shared by per-order and summary tables.

**Context:**

The class currently defines near-identical local functions inside two `Table(...)` lambda blocks. These represent QuestPDF cell wrappers that apply borders and padding for header cells, header-center cells, body data cells, body data center cells, and the "Sada: …" set sub-header cell. They each take an `IContainer` and return an `IContainer` after applying styles. Example shape (already present in the file as a local):

```csharp
static IContainer HeaderCell(IContainer container) =>
    container.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
             .Background(Colors.Grey.Lighten3).Padding(2);
```

This task:
- Locates each existing local helper.
- Extracts them as `private static` methods on the class with identical signatures and bodies.
- Removes the now-duplicate local definitions.
- Replaces all callsites inside the existing `Compose(...)` lambda with calls to the class-level methods.

No styling change — same border thickness (`0.5f`), same colors, same padding values. Public surface unchanged.

**Files to create/modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` — extract helpers.

**Implementation steps:**

1. In `ExpeditionProtocolDocument.cs`, search for every `static IContainer HeaderCell` / `HeaderCellCenter` / `DataCell` / `CenteredDataCell` / `SetHeaderCell` defined as a static local function inside a lambda. Record their exact bodies (border thickness, border color, background, padding) — these are the canonical implementations.
2. Add the following five methods as `private static` members of the class, placed below the existing helper methods (`FormatAmount`, `FormatVariant`, `GenerateBarcode`) or in a clearly grouped region. Use the **exact** styling from the existing locals — do not invent new values. Skeleton (replace bodies with what exists in-source):

```csharp
private static IContainer HeaderCell(IContainer container) =>
    container.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
             .Background(Colors.Grey.Lighten3).Padding(2);

private static IContainer HeaderCellCenter(IContainer container) =>
    HeaderCell(container).AlignCenter();

private static IContainer DataCell(IContainer container) =>
    container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2);

private static IContainer CenteredDataCell(IContainer container) =>
    DataCell(container).AlignCenter();

private static IContainer SetHeaderCell(IContainer container) =>
    container.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
             .Background(Colors.Grey.Lighten4).Padding(2);
```

If existing locals diverge from these defaults, prefer the existing values — the goal is byte-equivalent rendering, not redesign.

3. Delete every duplicate static local function from inside lambdas. Each call site that previously invoked the local now invokes the class-level method (calling syntax is identical, e.g. `.Element(HeaderCell)`).
4. Compile (`dotnet build`). Resolve any stale references.
5. Run `dotnet format`.
6. Generate one sample PDF locally (any existing dev path that calls `ExpeditionProtocolDocument.Generate(...)`) and visually compare against a PDF generated from `main` for the same input. They should be byte-equivalent or visually indistinguishable.

**Tests to write:**

If `backend/test` already contains a test that calls `ExpeditionProtocolDocument` or `.Generate(`, run it (`dotnet test --filter`) and confirm it still passes. If a snapshot test exists, the bytes should not change as a result of this task — if they do, that indicates an accidental styling change and must be fixed before proceeding.

If no such test exists, no new test is required — this is a pure refactor.

**Acceptance criteria:**

- Five `private static` cell-style methods exist on the class with the styling values originally found in the locals.
- No `static IContainer ...Cell(...)` local functions remain inside any lambda in the file.
- `dotnet build` succeeds; `dotnet format` is clean.
- A sample PDF generated post-change is visually indistinguishable from one generated pre-change for identical input.

---

### task: introduce-render-description-cell

**Goal:** Add a single shared `RenderDescriptionCell(...)` method that renders a product's name and optional variant in one cell — used by every description-cell callsite in the document.

**Context:**

The redesign collapses the former `Popis položky` and `Varianta` columns into a single description cell that renders:
- Line 1: the product `Name` at default body size.
- Line 2 (only if non-empty): `FormatVariant(variant)` at `VariantFontSize` (7f) in `VariantColor` (`Colors.Grey.Darken1`).
- Both lines italic when `italic == true` (for set components and summary rows where `IsFromSet == true`).

`FormatVariant(string?)` already exists on the class and returns either the cleaned variant text or empty string.

This is the **only** path that may render a description cell after this work — explicit FR-5 from the architecture review:
> Per-order regular rows, per-order set rows, and summary rows MUST all render the description cell through one shared method or lambda. No per-callsite duplication of name + variant rendering is permitted.

QuestPDF idiom for in-cell typographic variation is a multi-span `Text(t => { ... })`, with conditional emission of the variant line.

The constants `VariantFontSize` and `VariantColor` were added in the constants task and are available.

**Files to create/modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` — add the new method.

**Implementation steps:**

1. Add the following `private static` method to the class, placed near the cell-style helpers:

```csharp
private static void RenderDescriptionCell(
    IContainer cell,
    string name,
    string? variant,
    bool italic)
{
    var formattedVariant = FormatVariant(variant);

    cell.Text(text =>
    {
        var nameSpan = text.Span(name);
        if (italic) nameSpan.Italic();

        if (!string.IsNullOrEmpty(formattedVariant))
        {
            text.Line(string.Empty); // forces line break before variant
            var variantSpan = text
                .Span(formattedVariant)
                .FontSize(VariantFontSize)
                .FontColor(VariantColor);
            if (italic) variantSpan.Italic();
        }
    });
}
```

Notes on the implementation:
- `text.Span(name)` is used (not `text.Line(name)`) because QuestPDF's `Line(...)` returns void in some versions; using `Span` then a separator `Line(string.Empty)` keeps the API consistent and lets us style per span.
- If the file is on a QuestPDF version where `text.Line(name)` returns a `TextSpanDescriptor`, replace `text.Span(name)` + `text.Line(string.Empty)` with `text.Line(name)` directly to keep code idiomatic. Verify by checking the QuestPDF version in the .csproj for this adapter; use whichever variant compiles.
- Whitespace-only second line is forbidden: only emit the second line when `formattedVariant` is non-empty.

2. Compile (`dotnet build`). If the QuestPDF API exposes a different way to break to a new line within `Text(t => ...)`, adapt to that API while preserving the contract above.

3. Run `dotnet format`.

**Tests to write:**

Manual verification only at this stage — no callsites yet. The method is exercised by the next two tasks.

**Acceptance criteria:**

- The method `private static void RenderDescriptionCell(IContainer cell, string name, string? variant, bool italic)` exists on the class.
- Method renders `name` then a conditional second line with `FormatVariant(variant)` at `VariantFontSize` and `VariantColor`.
- When `italic` is `true`, italic is applied to both name and variant spans.
- When `FormatVariant(variant)` returns empty/null, no second line and no extra whitespace are rendered.
- `dotnet build` succeeds; `dotnet format` is clean.

---

### task: refactor-compose-into-order-block-and-summary-page

**Goal:** Split the existing `Compose(IDocumentContainer)` body into orchestration plus three private composition methods (`ComposeOrderBlock`, `ComposeSummaryPage`, plus retained `BuildItemsTable` / `BuildSummaryTable` from later tasks), preserving identical output for now.

**Context:**

The current `Compose(...)` method does everything inline: page setup, iteration over `_data.Orders`, per-order heading/barcode/customer block, items table, inter-order `LineHorizontal` divider, and summary page rendering. This task is a **pure structural refactor** — no visual change yet (the bordered box, removal of the `LineHorizontal`, and column merge come in subsequent tasks). The goal is to make the next tasks small.

Public surface unchanged: `Compose(IDocumentContainer)` remains the entry point invoked by QuestPDF.

After this task, `Compose` should look approximately like:

```
Compose(container)
├── container.Page(page => { ... PageSize/Margin/DefaultTextStyle/Header/Content/Footer ... })
└── inside Content:
    └── column =>
        ├── foreach (order in _data.Orders) ComposeOrderBlock(column.Item(), order);
        └── if (_data.Orders.Any()) ComposeSummaryPage(column.Item());
```

`ComposeOrderBlock` initially contains the **exact existing** per-order rendering (heading, barcode, customer line, optional notes, items table, and the existing `LineHorizontal` divider). `ComposeSummaryPage` contains the existing summary-page rendering. We are only moving code, not changing it.

**Files to create/modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` — extract methods.

**Implementation steps:**

1. Read the current body of `Compose(IDocumentContainer container)`. Identify the per-order rendering region (everything done once per `ExpeditionOrder`) and the summary-page region (rendered once after the loop, currently introduced with `column.Item().PageBreak()` or similar).
2. Add two new private methods on the class:

```csharp
private void ComposeOrderBlock(IContainer container, ExpeditionOrder order)
{
    // MOVE the existing per-order rendering body here verbatim.
    // It currently builds: heading row + barcode + customer line + optional notes + items table
    // + (existing) LineHorizontal divider.
    // Use 'container' as the parent. Use 'order' instead of the loop variable.
}

private void ComposeSummaryPage(IContainer container)
{
    // MOVE the existing summary-page rendering body here verbatim.
    // It currently aggregates _data.Orders into rows and renders a table.
}
```

3. Replace the original inline rendering inside `Compose(...)` with calls:

```csharp
foreach (var order in _data.Orders)
{
    ComposeOrderBlock(column.Item(), order);
}

if (_data.Orders.Any())
{
    ComposeSummaryPage(column.Item());
}
```

(Adjust to match the existing column variable name and existing guard conditions for the summary page.)

4. Compile and run `dotnet format`.
5. Generate a sample PDF and compare to a pre-refactor PDF for the same input — they must be visually indistinguishable.

**Tests to write:**

If a snapshot test exists in `backend/test` for this document, run it. The bytes should not change. If they do, the move was not faithful — fix before proceeding.

If no snapshot test exists, manually compare two generated PDFs (pre/post refactor) for equivalence. No new automated tests required.

**Acceptance criteria:**

- `Compose(IDocumentContainer)` is reduced to page setup + iteration + summary call.
- `ComposeOrderBlock(IContainer, ExpeditionOrder)` and `ComposeSummaryPage(IContainer)` exist as private instance methods and contain the moved rendering code.
- Generated PDF for identical input is visually indistinguishable from pre-refactor output.
- `dotnet build` succeeds; `dotnet format` is clean.

---

### task: extract-build-items-table-and-build-summary-table

**Goal:** Extract the per-order items-table rendering and the summary-table rendering from `ComposeOrderBlock` / `ComposeSummaryPage` into dedicated `BuildItemsTable` / `BuildSummaryTable` private methods. Still no visual change.

**Context:**

After the previous task, `ComposeOrderBlock` and `ComposeSummaryPage` each contain a large `Table(...)` definition. We want each to become a thin wrapper that delegates the table itself to a `BuildItemsTable` / `BuildSummaryTable` method. This isolates the column-merge + description-cell-routing changes that follow.

Both tables currently render six columns (`Kód`, `Popis položky`, `Varianta`, `Množství`, `Pozice`, `Stav skladu`) — this task does NOT yet change that. The merge to five columns happens in a later task.

`SummaryRow` is currently inlined as an anonymous type in the summary lambda; for this task you may keep it as an anonymous type, or — preferred — promote it to a private nested class:

```csharp
private sealed class SummaryRow
{
    public string ProductCode        { get; init; } = string.Empty;
    public string Name               { get; init; } = string.Empty;
    public string? Variant           { get; init; }
    public string? WarehousePosition { get; init; }
    public string? Unit              { get; init; }
    public decimal TotalQuantity     { get; init; }
    public int StockCount            { get; init; }
    public bool IsFromSet            { get; init; }
}
```

Aggregation logic (must be preserved exactly): `_data.Orders.SelectMany(o => o.Items).GroupBy(i => i.ProductCode)`, summing `Quantity`, taking the first `Name`/`Variant`/`WarehousePosition`/`Unit`, setting `IsFromSet` if **any** item in the group has `IsFromSet == true`. Sorted by `WarehousePosition`.

**Files to create/modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` — extract methods and (optionally) add `SummaryRow` nested class.

**Implementation steps:**

1. Add the nested `SummaryRow` class shown above (if not already present) inside `ExpeditionProtocolDocument`, near the bottom of the class body.
2. Add two new private methods:

```csharp
private void BuildItemsTable(IContainer container, IReadOnlyList<ExpeditionOrderItem> items)
{
    // MOVE the existing Table(...) definition for per-order items here verbatim.
    // Six-column shape unchanged for now: Kód | Popis | Varianta | Množství | Pozice | Stav.
    // Includes regular rows and 'Sada: {SetName}' grouped rows.
}

private void BuildSummaryTable(IContainer container, IReadOnlyList<SummaryRow> rows)
{
    // MOVE the existing Table(...) definition for the summary page here verbatim.
    // Six-column shape unchanged for now.
}
```

3. In `ComposeOrderBlock`, replace the inline items table with `BuildItemsTable(column.Item(), order.Items.ToList());` (or pass `order.Items` directly if it is already an `IReadOnlyList<ExpeditionOrderItem>`).
4. In `ComposeSummaryPage`, perform the aggregation into `IReadOnlyList<SummaryRow>` and then call `BuildSummaryTable(column.Item(), rows);`. Aggregation example:

```csharp
var rows = _data.Orders
    .SelectMany(o => o.Items)
    .GroupBy(i => i.ProductCode)
    .Select(g => new SummaryRow
    {
        ProductCode       = g.Key,
        Name              = g.First().Name,
        Variant           = g.First().Variant,
        WarehousePosition = g.First().WarehousePosition,
        Unit              = g.First().Unit,
        TotalQuantity     = g.Sum(x => x.Quantity),
        StockCount        = g.First().StockCount,
        IsFromSet         = g.Any(x => x.IsFromSet),
    })
    .OrderBy(r => r.WarehousePosition)
    .ToList();
```

If the existing aggregation differs (e.g., ordering or selection of `StockCount`), preserve the existing behavior — do not change semantics here.

5. Compile, format, and generate a sample PDF; compare to pre-task output — must be visually indistinguishable.

**Tests to write:**

If a snapshot test exists for `ExpeditionProtocolDocument`, run it. Bytes must not change. Otherwise manual visual comparison.

**Acceptance criteria:**

- `BuildItemsTable(IContainer, IReadOnlyList<ExpeditionOrderItem>)` and `BuildSummaryTable(IContainer, IReadOnlyList<SummaryRow>)` exist as private instance methods.
- `ComposeOrderBlock` and `ComposeSummaryPage` each call their respective `Build*Table` method instead of defining the table inline.
- `SummaryRow` is a private nested class with the listed properties, OR aggregation continues to use a typed shape that exposes the same fields.
- Generated PDF for identical input is visually indistinguishable from pre-task output.
- `dotnet build` and `dotnet format` are clean.

---

### task: route-description-cells-through-render-description-cell

**Goal:** Replace every per-callsite description rendering inside `BuildItemsTable` and `BuildSummaryTable` with calls to `RenderDescriptionCell(...)`. Still six columns at this point — only the rendering of the existing `Popis položky` cell changes.

**Context:**

There are currently three places that render a product description cell:
1. Per-order regular rows in `BuildItemsTable` — non-italic.
2. Per-order set-component rows in `BuildItemsTable` — italic.
3. Summary rows in `BuildSummaryTable` — italic when `row.IsFromSet`.

Each currently calls `.Element(DataCell).Text(item.Name)` (or similar) for the `Popis položky` cell, and renders `Varianta` separately in its own cell. This task changes only the `Popis položky` cell — the description cell — to call `RenderDescriptionCell(...)`. The `Varianta` cell remains for now (it gets removed in the next task when columns are merged).

This means after this task, the variant text is rendered **twice** in each row temporarily — once inline by `RenderDescriptionCell` (as a sub-line under the name) and once in the standalone `Varianta` cell. That is intentional and short-lived: the next task removes the standalone `Varianta` column and resolves the duplication.

`RenderDescriptionCell` signature (already added in earlier task):
```csharp
private static void RenderDescriptionCell(IContainer cell, string name, string? variant, bool italic);
```

**Files to create/modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` — replace description cell rendering at three callsites.

**Implementation steps:**

1. In `BuildItemsTable`, find the regular-row block. Locate the cell that renders `item.Name` (the current `Popis položky` column). Replace it:

```csharp
// Before:
table.Cell().Element(DataCell).Text(item.Name);

// After:
table.Cell().Element(c => DataCell(c)).Element(c => { RenderDescriptionCell(c, item.Name, item.Variant, italic: false); return c; });
```

Because `RenderDescriptionCell` returns `void` and operates on the container directly, the cleanest call shape is:

```csharp
table.Cell().Element(DataCell).Element(c =>
{
    RenderDescriptionCell(c, item.Name, item.Variant, italic: false);
    return c;
});
```

If QuestPDF's `Element(...)` only accepts `Func<IContainer, IContainer>`, use that pattern. If it accepts `Action<IContainer>`, prefer that:

```csharp
table.Cell().Element(DataCell).Element(c => RenderDescriptionCell(c, item.Name, item.Variant, italic: false));
```

Use whichever the existing codebase already uses for `DataCell` consumers.

2. In the same `BuildItemsTable`, find the set-component row block (rows under a `Sada: {SetName}` header). Replace its description cell the same way but with `italic: true`:

```csharp
RenderDescriptionCell(c, item.Name, item.Variant, italic: true);
```

3. In `BuildSummaryTable`, find the row description cell and replace it with:

```csharp
RenderDescriptionCell(c, row.Name, row.Variant, italic: row.IsFromSet);
```

4. Do **not** remove or change the `Varianta` column or its cell rendering yet — that is the next task.
5. Compile, format. Generate a sample PDF: visually verify that the description cell now shows name + small grey variant line (where applicable), and the standalone `Varianta` column still also shows the variant text (temporary duplication is expected).

**Tests to write:**

Manual visual verification only. If a snapshot test exists, it will fail because byte output changes — regenerate the snapshot baseline as part of this PR with a clear commit message: "Snapshot regenerated: description cell now renders name + variant; Varianta column will be removed in the next task." Do not skip the test.

**Acceptance criteria:**

- All three description-cell callsites (regular per-order, set per-order, summary) invoke `RenderDescriptionCell(...)` and pass the correct `italic` value.
- No description cell remains that calls `.Text(item.Name)` directly.
- Generated sample PDF shows: name on first line at body size, variant on second line at 7pt grey (`Colors.Grey.Darken1`), italic when row is a set component or summary row with `IsFromSet`.
- The standalone `Varianta` column is still present (intentional, short-lived).
- `dotnet build` and `dotnet format` are clean.

---

### task: merge-popis-and-varianta-columns-to-five-column-layout

**Goal:** Remove the standalone `Varianta` column from both tables, leaving a five-column layout (`Kód`, `Popis položky`, `Množství`, `Pozice`, `Stav skladu`) where `Popis položky` carries name + variant via `RenderDescriptionCell`.

**Context:**

Final column shape after this task (both tables identical):

| # | Header (CZ) | Relative width | Notes |
|---|-------------|----------------|-------|
| 1 | Kód | `KodCol` = 2 | Product code |
| 2 | Popis položky | `PopisCol` = 8 | Name + optional variant line; rendered by `RenderDescriptionCell` |
| 3 | Množství | `MnozstviCol` = 1.5 | Quantity (existing 11pt bold styling preserved) |
| 4 | Pozice | `PoziceCol` = 2 | Warehouse position |
| 5 | Stav skladu | `StavCol` = 2 | Stock count |

Removed:
- The `Varianta` header cell.
- All per-row `Varianta` data cells (regular rows, set-component rows, summary rows).
- The relative-width entry for the removed column from the `ColumnsDefinition`.

Set sub-header `Sada: {SetName}` rows currently use `ColumnSpan(6)` — change to `ColumnSpan(5)`.

Use the constants (`KodCol`, `PopisCol`, `MnozstviCol`, `PoziceCol`, `StavCol`) when defining columns:

```csharp
table.ColumnsDefinition(columns =>
{
    columns.RelativeColumn(KodCol);
    columns.RelativeColumn(PopisCol);
    columns.RelativeColumn(MnozstviCol);
    columns.RelativeColumn(PoziceCol);
    columns.RelativeColumn(StavCol);
});
```

Header row likewise loses the `Varianta` header cell. Existing per-cell styling (`HeaderCell`, `HeaderCellCenter`, `DataCell`, `CenteredDataCell`, `SetHeaderCell`, the 11pt bold quantity styling, etc.) is reused unchanged — only counts and ordering change.

**Files to create/modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` — change column definitions, header row, all data rows, and set sub-header `ColumnSpan` in both `BuildItemsTable` and `BuildSummaryTable`.

**Implementation steps:**

1. In `BuildItemsTable`:
   a. Replace the `ColumnsDefinition` block with the five-column definition shown above.
   b. In the header row, remove the `Varianta` `HeaderCellCenter` cell. The header row now contains exactly: `Kód`, `Popis položky`, `Množství` (centered), `Pozice` (centered), `Stav skladu` (centered).
   c. In each regular row, remove the standalone variant cell (the one that previously called `.Text(FormatVariant(item.Variant))`).
   d. In each set-component row, remove the standalone variant cell.
   e. Find the set sub-header row (`Sada: {SetName}`). Change `ColumnSpan(6)` to `ColumnSpan(5)`.
2. In `BuildSummaryTable`:
   a. Replace the `ColumnsDefinition` block with the five-column definition.
   b. Remove the `Varianta` header cell.
   c. Remove the standalone variant cell from each summary row.
3. Verify no callsite remains that renders a standalone variant cell. Grep the file for `FormatVariant(` — only the call inside `RenderDescriptionCell` should remain.
4. Compile and run `dotnet format`.
5. Generate a sample PDF with realistic data covering: ≥2 orders, ≥1 multi-item order, ≥1 set group, ≥1 item with a long product name, ≥1 item with no variant. Visually verify:
   - Five columns total, in the order above.
   - Variant text appears only as the small grey sub-line under the name, never in its own column.
   - Long product names wrap inside the `Popis položky` cell without overflow.
   - Quantity column remains 11pt bold.
   - Set sub-header bar spans the full table width.
   - If any column feels too narrow/wide, tune `KodCol`/`PopisCol`/`MnozstviCol`/`PoziceCol`/`StavCol` (the only place to change is the constants block).

**Tests to write:**

If a snapshot test exists, regenerate the baseline as part of this PR with commit message noting the column-merge layout change. Manual visual verification with the cases above is required.

**Acceptance criteria:**

- Both tables render exactly five columns matching the table above.
- No `Varianta` header or standalone variant data cell exists anywhere in the file.
- Set sub-header rows use `ColumnSpan(5)`.
- `FormatVariant(` is referenced only from `RenderDescriptionCell`.
- Sample PDF visually meets all five bullet points in step 5.
- `dotnet build` and `dotnet format` are clean.

---

### task: wrap-each-order-in-bordered-block-and-remove-divider

**Goal:** Replace the inline `LineHorizontal` divider between orders with a bordered, padded box around each order, separated from the next box by a fixed gap.

**Context:**

After the previous tasks, `ComposeOrderBlock(IContainer container, ExpeditionOrder order)` renders one order's heading + barcode + customer line + optional notes + items table, followed by a `LineHorizontal(...)` divider as the visual order separator.

The redesign replaces the divider with a bordered box around the entire order block, plus a fixed gap between consecutive boxes:

- Outer wrapper: `container.Border(BorderThickness).BorderColor(Colors.Grey.Darken2).Padding(BorderPadding)`.
- After the box: `column.Item().PaddingBottom(OrderGap)` between boxes (or use `PaddingBottom(OrderGap)` on the box itself — pick whichever produces correct spacing in QuestPDF).
- The `LineHorizontal(...)` divider previously emitted at the end of each order block is **removed entirely**.
- No `ShowEntire()` call is added — accept QuestPDF default page-break behavior. The border may render on both halves when an order splits across pages; this is acceptable per the architecture review (Decision 5).

The bordered box wraps **everything** in the order block: heading, barcode, customer line, notes, and items table. It does NOT wrap the summary page.

**Files to create/modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` — modify `ComposeOrderBlock` and the `Compose` loop or `ComposeOrderBlock` itself to insert the bottom gap.

**Implementation steps:**

1. In `ComposeOrderBlock(IContainer container, ExpeditionOrder order)`, wrap the existing inner `Column` body in `Border` + `Padding`:

```csharp
private void ComposeOrderBlock(IContainer container, ExpeditionOrder order)
{
    container
        .PaddingBottom(OrderGap)
        .Border(BorderThickness)
        .BorderColor(Colors.Grey.Darken2)
        .Padding(BorderPadding)
        .Column(column =>
        {
            // existing heading row
            // existing barcode
            // existing customer line
            // existing optional notes
            BuildItemsTable(column.Item(), order.Items.ToList());
            // (LineHorizontal divider previously here — REMOVED)
        });
}
```

If `PaddingBottom` ordering relative to `Border` causes the gap to render *inside* the border, swap the order so `PaddingBottom` is the outermost modifier — verify visually.

2. Remove any `LineHorizontal(...)` call that previously sat at the end of the per-order column. Grep the file for `LineHorizontal` to confirm zero remaining calls inside the per-order block. (If `LineHorizontal` is used elsewhere — e.g., the summary page — leave those alone.)
3. Verify `Compose` still iterates orders correctly and that no extra padding/divider sits between orders outside of `ComposeOrderBlock`. The loop should now look like:

```csharp
foreach (var order in _data.Orders)
{
    ComposeOrderBlock(column.Item(), order);
}
```

with no additional `column.Item().LineHorizontal(...)` or `PaddingBottom(...)` after each call — the gap belongs to `ComposeOrderBlock`.

4. Compile and run `dotnet format`.
5. Generate a sample PDF with realistic data covering: ≥2 orders (to verify gap), ≥1 order with many items (to observe page-break behavior on a bordered box), ≥1 short order. Visually verify:
   - Each order is fully enclosed in a 1.5pt grey box.
   - Inner padding around the box content is `BorderPadding` (4) on all sides.
   - There is a `OrderGap` (6) gap between consecutive boxes.
   - No horizontal divider line appears between order boxes.
   - On a long order that splits across pages, the border is drawn correctly on both halves (acceptable per Decision 5).

**Tests to write:**

If a snapshot test exists, regenerate the baseline as part of this PR with a commit message noting the bordered-box visual change. Manual visual verification with the cases above is required.

**Acceptance criteria:**

- Each rendered order is wrapped in a `1.5f` grey-darken2 border with `4` padding inside.
- Consecutive order boxes are separated by a `6`-unit gap (no horizontal line).
- `LineHorizontal` is no longer invoked inside `ComposeOrderBlock`.
- Long orders that exceed a page split with the border drawn on both halves; no `ShowEntire()` is used.
- The summary page rendering is unaffected (no border around it).
- `dotnet build` and `dotnet format` are clean.

---

### task: visual-qa-and-snapshot-baseline-update

**Goal:** Run the manual visual QA pass required by the architecture review and refresh any existing snapshot baseline before merge.

**Context:**

Prerequisite from the architecture review:
> Before merging: grep `backend/test` for any reference to `ExpeditionProtocolDocument` or `.Generate(`. If a snapshot/golden-PDF baseline exists, regenerate it in the same PR. If none exists, perform a manual visual review of a generated sample PDF covering: ≥1 multi-item order, ≥1 set, ≥1 item with a long name, ≥1 item with no variant, ≥2 orders.

This task gates merge — it confirms the four open questions from the spec are answered correctly:
- Page-break behavior on bordered boxes is acceptable (or escalate).
- Column widths render legibly with realistic data (or tune constants).
- Variant color (`Colors.Grey.Darken1`) reads clearly at 7pt for warehouse pickers.
- Inter-box gap (`OrderGap = 6`) is neither cramped nor sparse.

**Files to create/modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` — only if visual QA reveals a tuning need (constants only).
- Any existing snapshot baseline file under `backend/test` — regenerate if present.

**Implementation steps:**

1. Run a repository-wide search for references that might be snapshot tests:

```bash
grep -R "ExpeditionProtocolDocument" backend/test
grep -R "ExpeditionProtocolData" backend/test
```

   For every hit, open the test and determine whether it asserts on PDF bytes, text content, or page count. If it does, treat it as a baseline test.

2. If a baseline test exists:
   a. Run it (`dotnet test --filter <test-name>`). Expect it to fail because layout changed.
   b. Regenerate the baseline by following whatever convention the test uses (often: delete the old `.pdf` / `.bin` baseline file and re-run the test in "update" mode, or run a small program that re-emits the expected bytes). Use the exact mechanism the existing baseline uses — do not invent a new one.
   c. Re-run the test; it must now pass.
   d. Inspect the regenerated baseline visually — open it as a PDF — to confirm the new output is what we want, not just stable.

3. If no baseline test exists, generate a sample PDF manually using whichever existing dev path calls `ExpeditionProtocolDocument.Generate(...)`. Build a representative `ExpeditionProtocolData` with:
   - ≥2 orders.
   - ≥1 multi-item order (5+ items).
   - ≥1 order containing a set (so a `Sada: {SetName}` sub-header renders).
   - ≥1 item with a very long product name (50+ chars).
   - ≥1 item with no variant.
   - ≥1 item with a long variant string.

4. Open the generated PDF and verify:
   - Each order is enclosed in a 1.5pt grey-darken2 border with consistent inner padding.
   - Boxes are separated by clear vertical whitespace (the `OrderGap = 6` gap) with no divider line.
   - Tables show exactly five columns: `Kód`, `Popis položky`, `Množství`, `Pozice`, `Stav skladu`.
   - Variant text appears as a smaller grey sub-line beneath the product name; never in its own column.
   - Set components render in italic across all visible columns.
   - Long product names wrap inside `Popis položky` without overflowing into other columns or breaking row alignment.
   - Quantity column is 11pt bold (preserved from existing styling).
   - Set sub-header `Sada: {SetName}` spans the full five-column width.
   - Summary page (last page) renders the same five-column shape; rows from set items render in italic.
   - For an order with many items, the bordered box may split across pages; the border draws on both halves and the layout remains readable.

5. If any visual issue is observed:
   - Column proportions look off → tune `KodCol`/`PopisCol`/`MnozstviCol`/`PoziceCol`/`StavCol`.
   - Variant line illegible → bump `VariantFontSize` to 7.5 or 8 (cap at 8).
   - Boxes feel cramped/sparse → tune `OrderGap` and/or `BorderPadding`.
   - Border too heavy/light → tune `BorderThickness`.

   Apply changes only to the constants block; re-run from step 3.

6. Run `dotnet format` and `dotnet build` once more.

**Tests to write:**

No new automated tests are introduced by this task. The only test artifact that may change is an existing snapshot baseline, regenerated in step 2.

**Acceptance criteria:**

- If a snapshot test existed, its baseline is regenerated and the test passes against the new output.
- A sample PDF generated from the realistic dataset described in step 3 satisfies every bullet point in step 4.
- Any tuning needed was applied via the constants block only — no changes to layout logic.
- `dotnet build` and `dotnet format` are clean on the modified file.
- The change is ready for merge with no `LineHorizontal` between orders, no standalone `Varianta` column, and a bordered box around every order.