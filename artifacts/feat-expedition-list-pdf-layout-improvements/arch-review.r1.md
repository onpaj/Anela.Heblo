```markdown
# Architecture Review: Expedition List PDF Layout Improvements

## Architectural Fit Assessment

This is a low-risk, contained presentation-layer change with **excellent architectural fit**:

- **Single-file scope**: Only `ExpeditionProtocolDocument.cs` in the Adapters layer. No domain, application, persistence, API, or frontend layers are touched.
- **Vertical slice integrity preserved**: The Expedition slice's domain model (`ExpeditionOrderItem.Variant`, `FormatVariant()`) already supports the target rendering — no contract drift.
- **No cross-cutting concerns**: No new dependencies, no auth changes, no MediatR handlers, no configuration. QuestPDF's existing fluent API covers everything required.
- **Integration points**: Two — (1) consumers of `ExpeditionProtocolDocument.Generate(...)` via the expedition list endpoint, which receive the same `byte[]` PDF (output shape unchanged); (2) any golden-PDF/snapshot test that asserts byte-level or text-level layout (will need baseline regeneration).

The one architectural smell already present in the file — three near-duplicate row-rendering blocks (regular items, set components, summary rows) — should be eliminated as part of this work, not deferred. The spec already calls this out in NFR-4.

## Proposed Architecture

### Component Overview

```
ExpeditionProtocolDocument (IDocument)
│
├── Constants (NEW: top-of-class private const block)
│   ├── BorderThickness = 1.5f
│   ├── BorderPadding   = 4f
│   ├── OrderGap        = 6f
│   ├── VariantFontSize = 7f
│   ├── VariantColor    = Colors.Grey.Darken1
│   └── Column ratios: KodCol, PopisCol, MnozstviCol, PoziceCol, StavCol
│
├── Compose(IDocumentContainer)              [orchestration only]
│   ├── ComposeOrderBlock(IContainer, ExpeditionOrder)   [NEW private]
│   │   └── inside: Border → Padding → Column
│   │       ├── heading + barcode + customer line + notes
│   │       └── BuildItemsTable(...)                     [NEW private]
│   │           └── uses RenderDescriptionCell(...)      [NEW shared lambda/method]
│   │
│   └── ComposeSummaryPage(IContainer)                   [NEW private]
│       └── BuildSummaryTable(...)                       [NEW private]
│           └── uses RenderDescriptionCell(...)          [shared with per-order]
│
├── Existing helpers (unchanged): FormatAmount, FormatVariant, GenerateBarcode
└── Helpers used by both tables (NEW): HeaderCell, DataCell, CenteredDataCell
    extracted to private static methods at class level (currently duplicated as locals)
```

### Key Design Decisions

#### Decision 1: Extract layout primitives to class-level private static methods, not local functions
**Options considered:**
- (A) Keep static-local helpers inside each `Table(...)` lambda (status quo, just dropping one column).
- (B) Extract `HeaderCell`, `DataCell`, `CenteredDataCell`, and a new `RenderDescriptionCell` to private static methods on the class.
- (C) Introduce a separate `ExpeditionTableStyles` static class.

**Chosen approach:** (B).

**Rationale:** Per-order and summary tables currently duplicate identical cell styles via copy-paste local functions. With the column merge, the *description cell rendering* becomes the third duplicated concern — three drift points is one too many. Class-level private statics give one source of truth and stay encapsulated within the document. (C) is over-abstraction (YAGNI) — there is exactly one consumer.

#### Decision 2: Render name + variant as a single multi-span `Text` block, not nested `Column`
**Options considered:**
- (A) Multi-span `Text(t => { t.Line(name); t.Span(variant)... })` inside one cell.
- (B) Nested `Column` with two `Item().Text(...)` rows.
- (C) Concatenated single-line string with separator.

**Chosen approach:** (A) using `Text(t => { ... })` with two `Line(...)`/`Span(...)` calls and conditional emission of the variant line.

**Rationale:** Multi-span `Text` is the QuestPDF idiom for in-cell typographic variation; it gives correct line-height behavior, avoids extra container nesting, and keeps wrapping coherent when the product name itself wraps. Nested `Column` introduces redundant boxes that complicate row height in tables. (C) loses the visual hierarchy the brief explicitly requires. When `FormatVariant(variant)` is empty, emit only the name span — no whitespace-only second line.

#### Decision 3: Italic styling propagates through `RenderDescriptionCell` via a parameter, not via callsite duplication
**Options considered:**
- (A) `RenderDescriptionCell(IContainer cell, string name, string? variant, bool italic)`.
- (B) Two methods: `RenderDescriptionCell` and `RenderDescriptionCellItalic`.
- (C) Caller wraps the result in `.Italic()` (won't work — `Text(t => ...)` styling is per-span).

**Chosen approach:** (A).

**Rationale:** One parameter, one method, one rendering path. Set components and summary `IsFromSet` rows pass `italic: true`; everyone else passes `italic: false`. This is the smallest change that closes the three-way drift hole.

#### Decision 4: Drop the inter-order `LineHorizontal` divider; use `PaddingBottom` on the bordered box for spacing
**Options considered:**
- (A) Keep the horizontal line *and* add the border (visually noisy, two separators).
- (B) Replace the line with `PaddingBottom(6)` after each bordered order block.

**Chosen approach:** (B).

**Rationale:** The brief explicitly says the bordered box replaces the divider as the visual separator. Keeping both is redundant and crowds the page.

#### Decision 5: No explicit `ShowEntire()` on the order block — accept QuestPDF default page-break behavior
**Options considered:**
- (A) Add `ShowEntire()` so a too-tall order box pushes entirely to the next page.
- (B) Let QuestPDF split the box across pages, drawing the border on both halves.

**Chosen approach:** (B), revisit only if QA flags it.

**Rationale:** Some orders have many items and `ShowEntire()` could waste a full page. QuestPDF correctly draws partial borders. The brief says nothing about page-break behavior; adding `ShowEntire()` is speculative complexity. Document this as an explicit assumption in the implementation comment so future maintainers don't have to rediscover the trade-off.

## Implementation Guidance

### Directory / Module Structure

**No new files.** All changes are inside:
```
backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs
```

Internal class structure after refactor (top to bottom):

```
ExpeditionProtocolDocument
├── private const fields                       [NEW — layout constants]
├── ctor + Generate + GetMetadata + GetSettings  [unchanged]
├── public  void Compose(IDocumentContainer)   [shrunk to orchestration]
├── private void ComposeOrderBlock(...)        [NEW]
├── private void ComposeSummaryPage(...)       [NEW]
├── private void BuildItemsTable(...)          [NEW]
├── private void BuildSummaryTable(...)        [NEW]
├── private static void RenderDescriptionCell( [NEW]
│         IContainer cell, string name, string? variant, bool italic)
├── private static IContainer HeaderCell(...)        [extracted]
├── private static IContainer HeaderCellCenter(...)  [extracted]
├── private static IContainer DataCell(...)          [extracted]
├── private static IContainer CenteredDataCell(...)  [extracted]
├── private static IContainer SetHeaderCell(...)     [extracted]
├── private static string FormatAmount(...)          [unchanged]
├── private static string FormatVariant(...)         [unchanged]
└── private static byte[] GenerateBarcode(...)       [unchanged]
```

### Interfaces and Contracts

**No public contract changes.** The class is consumed via two public surfaces, both unchanged:
- `public ExpeditionProtocolDocument(ExpeditionProtocolData data)`
- `public static byte[] Generate(ExpeditionProtocolData data)`

**Internal contract (new):**

```csharp
private static void RenderDescriptionCell(
    IContainer cell,
    string name,
    string? variant,
    bool italic);
```

Contract:
- Renders `name` as the primary line at default body size.
- If `FormatVariant(variant)` returns non-empty, renders it on a second line at `VariantFontSize` in `VariantColor`.
- If empty, renders only the name line — no trailing whitespace.
- When `italic == true`, applies italic to **both** lines.

**Layout constants (new, all `private const` or `private static readonly` at top of class):**

```csharp
private const float BorderThickness  = 1.5f;
private const float BorderPadding    = 4f;
private const float OrderGap         = 6f;
private const float VariantFontSize  = 7f;
private static readonly string VariantColor = Colors.Grey.Darken1;

// Column ratios — must sum reasonably; final tuning during impl
private const float KodCol      = 2f;
private const float PopisCol    = 8f;   // absorbs former Varianta column
private const float MnozstviCol = 1.5f;
private const float PoziceCol   = 2f;
private const float StavCol     = 2f;
```

### Data Flow

For both render paths the data flow is unchanged at the boundary; only the internal composition changes.

**Per-order block (one iteration of `_data.Orders`):**

```
ExpeditionOrder
   │
   ▼
ComposeOrderBlock(col.Item(), order)
   │
   ├─► outer: Border(1.5f).BorderColor(Grey.Darken2).Padding(4)
   │
   └─► inner Column:
         heading → barcode → customer line → optional notes
         └─► BuildItemsTable(orderCol.Item(), order.Items)
               │
               ├─ split into regularItems + setGroups
               │
               ├─ for each regularItem:
               │     ProductCode | RenderDescriptionCell(name, variant, italic:false) | Qty | Pos | Stock
               │
               └─ for each setGroup:
                     ColumnSpan(5) "Sada: {key}" header
                     for each item:
                        ProductCode (italic) | RenderDescriptionCell(name, variant, italic:true) | Qty (italic bold) | Pos (italic) | Stock (italic)
   │
   ▼
col.Item().PaddingBottom(OrderGap)   [gap before next order's box]
```

**Summary page:**

```
_data.Orders → SelectMany(Items) → GroupBy(ProductCode)
   │
   ▼
List<aggregated row {Code, Name, Variant, Pos, Unit, TotalQty, StockCount, IsFromSet}>
   │
   ▼
BuildSummaryTable(col.Item(), aggregated)
   │
   └─ same 5-column shape; for each row:
        ProductCode | RenderDescriptionCell(Name, Variant, italic: row.IsFromSet) | TotalQty | Pos | Stock
```

The two table builders share `RenderDescriptionCell` — this is the structural payoff that eliminates the existing three-way drift risk.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Bordered order box splits awkwardly across pages mid-table, breaking visual cohesion | Medium | Accept QuestPDF default first. Generate a sample PDF with ≥2 long-item orders during implementation. If QA flags it, switch one location to `ShowEntire()` — gated by visual review, not preemptively. |
| Long product names + freed-up `PopisCol` width still wrap awkwardly with set/non-set mix | Medium | Generate sample PDF with realistic worst-case names (longest in production data). Tune `PopisCol` ratio. Constants live at the top of the class, so re-tuning is one diff. |
| Existing snapshot/golden-PDF test (if any) breaks because the byte output changes | Medium | Before coding, grep `backend/test` for any test that hits `ExpeditionProtocolDocument` or `Generate(` on it. If found, regenerate the baseline as part of this PR with a clear commit message; do not skip the test. If none exists, proceed. |
| `Text(t => { t.Line(name); t.Span(variant)... })` rendering edge case where `name` itself contains a newline — unlikely but possible from upstream data | Low | Trust upstream sanitization; `item.Name` is a marketing string from Shoptet which historically has no newlines. No defensive code unless QA shows a real case — YAGNI. |
| Italic propagation via boolean parameter forgotten on one of the two callsites | Low | The shared `RenderDescriptionCell` *requires* the `italic` parameter — it is impossible to call without specifying it. Compile-time guarantee. |
| Border + cell-level borders (existing 0.5pt grey on every cell) look visually busy | Low | Outer border is 1.5pt darken2; inner cells stay 0.5pt lighten1 — the contrast in weight+shade keeps the hierarchy clear. Verify visually; if too busy, drop inner cell borders to top/bottom only — out of scope unless QA flags it. |
| Variant line uses 7pt — readability concern for warehouse pickers | Low | Brief explicitly says ≤7pt. `VariantFontSize` is a named constant, easy to bump to 7.5 or 8 if pickers complain. |

## Specification Amendments

The spec is well-formed. Three small additions / clarifications recommended:

1. **Make NFR-4 prescriptive about extraction targets.** Beyond the constants and `RenderDescriptionCell`, explicitly require extracting the cell-style helpers (`HeaderCell`, `DataCell`, `CenteredDataCell`, `SetHeaderCell`) to class-level private static methods so per-order and summary tables share them. Today they are duplicated as locals in two `Table(...)` lambdas; the new column-merge change is a natural moment to fix this and aligns with NFR-4's stated maintainability goal.

2. **Add an explicit FR-5: `RenderDescriptionCell` is the single rendering path.** The spec mentions extracting the helper as a recommendation. Promote it to a functional requirement so reviewers can enforce it: *"Per-order regular rows, per-order set rows, and summary rows MUST all render the description cell through one shared method or lambda. No per-callsite duplication of name + variant rendering is permitted."*

3. **Resolve Open Question 5 (test strategy) with a concrete instruction.** The spec leaves it as an assumption. Make it: *"Before implementation, search `backend/test` for any test that invokes `ExpeditionProtocolDocument` or its `Generate` method. If a snapshot/baseline test exists, regenerate the baseline in the same PR. If none exists, no new tests are required — verification is a manual visual review of a sample PDF generated with realistic data (≥1 multi-item order, ≥1 set, ≥1 item with long name, ≥1 item with no variant)."* This converts a vague "if exists" into an actionable first step.

The other four open questions (page-break behavior, exact column widths, variant color, inter-box gap) are correctly left as implementation-time visual judgment calls — do not pre-decide them.

## Prerequisites

**None.** This change ships standalone.

- No database migrations.
- No configuration changes (no new appsettings keys, no environment variables, no feature flags).
- No infrastructure changes (no NuGet bumps; QuestPDF already provides every fluent API used).
- No upstream data backfill (`Variant` is already populated where applicable, normalized by existing `FormatVariant()`).
- No coordination with frontend or API consumers — output remains `byte[]` PDF with the same MIME type and same endpoint.

The only operational prerequisite is a one-time **visual QA pass** of a freshly generated sample PDF after implementation, before merging — to validate column widths, page-break behavior, and variant-line legibility against the four open questions in the spec.
```