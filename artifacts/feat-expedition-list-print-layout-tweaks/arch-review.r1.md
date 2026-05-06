# Architecture Review: Expedition list print — layout tweaks

## Architectural Fit Assessment

The change is a **single-file, layout-only edit** in the `Anela.Heblo.Adapters.ShoptetApi` adapter. It introduces no new architectural concepts: no new types, no DI registrations, no contracts, no migrations, no API surface. It is a pure presentation tweak inside an existing QuestPDF document composer (`ExpeditionProtocolDocument`).

Integration points are minimal and already in place:
- **Data source.** `ExpeditionOrderItem.UnitPrice` is already populated in `ShoptetApiExpeditionListSource.MapOrderItems` (line 267), with set sub-items pinned to `UnitPrice = 0m` (line 286). No producer change needed.
- **Consumer pipeline.** `ExpeditionProtocolDocument.Generate(...)` is invoked from `ExpeditionListService.PrintPickingListAsync` and the scheduled `PrintPickingListJob`. Neither needs to change — they consume the resulting `byte[]` opaquely.
- **Existing tests.** Contrary to the spec's claim that "no tests currently cover `ExpeditionProtocolDocument` directly", `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ExpeditionProtocolDocumentTests.cs` exists with smoke tests and visual-inspection tests already exercising `UnitPrice` and notes. This file must be updated, not ignored. (See Specification Amendments.)

The proposed change aligns with existing conventions: column-width constants stay grouped at the top, formatters stay private static, cell-style helpers are reused, and the code remains framework-idiomatic QuestPDF Fluent API usage.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────┐
│ ExpeditionListService.PrintPickingListAsync (unchanged)          │
│   └─► ExpeditionProtocolDocument.Generate(data)                  │
│         │                                                        │
│         ├─ Compose() ─► page.Content().Column                    │
│         │      ├─ ComposeOrderBlock(order) [TOUCHED]             │
│         │      │     1. heading                                  │
│         │      │     2. barcode                                  │
│         │      │     3. customer line                            │
│         │      │     4. BuildItemsTable [TOUCHED: Pozice→Cena]   │
│         │      │     5. notes block [MOVED below table]          │
│         │      │                                                 │
│         │      └─ ComposeSummaryPage()                           │
│         │            └─ BuildSummaryTable [UNCHANGED: Pozice]    │
│         │                                                        │
│         └─ FormatPrice(decimal) [NEW private static helper]      │
│              + CzechCulture (private static readonly)            │
└──────────────────────────────────────────────────────────────────┘
```

The asymmetry between `BuildItemsTable` (per-order, now `Cena`) and `BuildSummaryTable` (aggregated, still `Pozice`) is intentional and documented in the spec. The two methods already accept different row types (`ExpeditionOrderItem` vs. `SummaryRow`), which makes the divergence safe — there is no shared column-rendering code to fork.

### Key Design Decisions

#### Decision 1: Keep two table builders distinct (no shared column abstraction)
**Options considered:**
- (A) Introduce a shared `RenderRow(...)` helper parameterised over which column to render last.
- (B) Leave `BuildItemsTable` and `BuildSummaryTable` as-is; only edit `BuildItemsTable`.

**Chosen approach:** B.
**Rationale:** YAGNI. The two tables already diverge (per-order has `Sada:` sub-headers and per-order rows vs. summary's grouped/sorted rows), and they render different domain types. Introducing an abstraction now would couple two intentionally distinct presentations and make this targeted layout edit harder. Three similar cell calls is fine.

#### Decision 2: `FormatPrice` is a private static helper, not a domain service
**Options considered:**
- (A) Move price formatting into a shared formatting utility under the domain or a Common project.
- (B) Inline as a `private static` method in `ExpeditionProtocolDocument` next to `FormatAmount` / `FormatVariant`.

**Chosen approach:** B.
**Rationale:** Same reasoning as `FormatAmount`/`FormatVariant`: PDF-document-local presentation concern. No other caller needs cs-CZ price formatting today. If a second caller appears, extract then.

#### Decision 3: Empty cell semantics driven by `UnitPrice == 0m`, not by `SetName != null`
**Options considered:**
- (A) Branch on `IsFromSet` / `SetName != null` in the cell render call.
- (B) Encode "zero means empty" in `FormatPrice` itself and rely on the upstream invariant (set sub-items always have `UnitPrice = 0m`).

**Chosen approach:** B (matches the spec).
**Rationale:** Keeps the rendering symmetric across the two row types in `BuildItemsTable` (regular items + set sub-items use the same call site). The invariant is enforced upstream in `ShoptetApiExpeditionListSource.MapOrderItems`. Risk: a future legitimate "free" regular item (genuine `UnitPrice == 0m`) would render an empty cell — see Risks.

#### Decision 4: Preserve `cs-CZ` culture via explicit `CultureInfo`, not thread/process culture
**Chosen approach:** Hold `CzechCulture` as a `private static readonly CultureInfo` and pass it explicitly to `decimal.ToString("N0", CzechCulture)`.
**Rationale:** Containerised production hosts may default to `Invariant` or `en-US`. Explicit culture makes output deterministic and matches the existing `CultureInfo.InvariantCulture` discipline already practiced in `ShoptetApiExpeditionListSource` (line 267).

## Implementation Guidance

### Directory / Module Structure

Single file modified — no new files, no new folders:

```
backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/
  └── ExpeditionProtocolDocument.cs   ← modified
```

Tests updated in:

```
backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/
  └── ExpeditionProtocolDocumentTests.cs   ← extended (see Specification Amendments)
```

### Interfaces and Contracts

No public interface changes. Internal contract changes (file-private):

1. **New constant** — `private const float CenaCol = 2f;` declared adjacent to existing `KodCol`/`PopisCol`/`MnozstviCol`/`PoziceCol`/`StavCol`.
   - `PoziceCol` **stays declared** because `BuildSummaryTable` still uses it. Do not delete.

2. **New helpers** (file-private):
   ```csharp
   private static readonly CultureInfo CzechCulture = CultureInfo.GetCultureInfo("cs-CZ");
   private static string FormatPrice(decimal price) =>
       price == 0m ? string.Empty : $"{price.ToString("N0", CzechCulture)} Kč";
   ```

3. **`ComposeOrderBlock` body order** — heading, barcode, customer line, items table, conditional notes block. The currently-existing `orderCol.Item().PaddingTop(2);` spacer that sits between customer line and table (line 109) should be deleted; the new `PaddingTop(2)` belongs above the relocated notes block (preserving the existing styling pattern of the notes block from line 98).

4. **`BuildItemsTable` ColumnsDefinition** — replace the third `RelativeColumn(PoziceCol)` call with `RelativeColumn(CenaCol)`. `BuildSummaryTable.ColumnsDefinition` is **unchanged**.

5. **`using System.Globalization;`** must be added at the top of the file (currently absent — confirmed by reading lines 1–8).

### Data Flow

For the per-order pages (the only altered surface):

```
ExpeditionOrderItem.UnitPrice  ──►  FormatPrice(decimal)
   (decimal, populated upstream         │
    from itemPriceWithVat;              ├─ 0m       → string.Empty   (set sub-items)
    set sub-items pinned to 0m)         └─ non-zero → "1 234 Kč"     (cs-CZ N0)
                                        │
                                        ▼
                                  CenteredDataCell in column 4
                                  of the per-order items table
```

The notes block consumes the same `order.CustomerRemark` / `order.EshopRemark` fields as today; only its position in the parent `Column` changes.

The summary page data flow (`Orders → SelectMany → GroupBy → SummaryRow → BuildSummaryTable`) is untouched.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing test file `ExpeditionProtocolDocumentTests.cs` is not mentioned in the spec; if its visual-inspection PDFs are not refreshed, manual regression checks will compare against stale output. | Medium | Spec amendment: extend the existing test file with `FormatPrice` unit tests and re-run the visual-inspection tests (`Generate_SampleData_SavesToDiskForVisualInspection`, `Generate_Order126000038_WithNotes_SavesToDiskForVisualInspection`) to produce updated reference PDFs. |
| `FormatPrice` returning empty for any `UnitPrice == 0m` will hide legitimate zero-price regular items if/when they appear (e.g., a free promo item shipped via the eshop). The current invariant is implicit in upstream mapping. | Low | Document the invariant inline at the helper site with a single-line comment, *or* render `"0 Kč"` for non-set rows and empty only for set sub-items. Recommend keeping the spec's chosen behaviour (zero ⇒ empty) and adding a one-line comment naming the upstream invariant — promotion of the dependency from implicit to acknowledged. |
| Column width `CenaCol = 2f` is identical to `PoziceCol`. For very large carrier-wide orders with prices like `"12 345 Kč"`, the cell may wrap. | Low | Observe via the visual smoke test on `BuildSampleData()` (already includes `1.00m` and `340.00m`); if wrapping occurs, bump to `2.5f`. Defer until evidence appears. |
| `cs-CZ` `CultureInfo.GetCultureInfo` requires ICU/globalization support in the container. App is already on .NET 8 in a Linux container; `cs-CZ` should resolve via ICU (default in .NET 8 Linux images). | Low | Verified by the existing app already using `CultureInfo.InvariantCulture` parsing; a quick grep should confirm no `InvariantGlobalization=true` is set in the csproj. If `InvariantGlobalization` were enabled, `CultureInfo.GetCultureInfo("cs-CZ")` would throw — pre-flight check during implementation. |
| `using System.Globalization;` not yet imported in `ExpeditionProtocolDocument.cs`; missing directive will fail the build. | Low | Add the directive; also covered by `dotnet build` as a hard gate. |
| QuestPDF `RelativeColumn` total — adding `Cena` (2f) replaces `Pozice` (2f), so total relative width is preserved (15.5f). No layout regression on summary table because its `ColumnsDefinition` is untouched. | None | No action — verified by reading the code. |

## Specification Amendments

1. **Test file exists; spec must acknowledge it.** The spec's *Verification* section (point 2) reads "*No tests currently cover `ExpeditionProtocolDocument` directly*". This is **incorrect**. The file `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ExpeditionProtocolDocumentTests.cs` already contains unit tests using `UnitPrice` data and a visual-inspection test (`Generate_Order126000038_WithNotes_SavesToDiskForVisualInspection`) that pins notes layout. The spec must require:
   - **Add unit tests for `FormatPrice`** covering the three FR-3 acceptance cases (`0m` → empty, `299m` → `"299 Kč"`, `1999m` → `"1 999 Kč"`). Because `FormatPrice` is `private static`, either (a) make it `internal static` and add `[InternalsVisibleTo("Anela.Heblo.Tests")]` to the adapter project (preferred — already idiomatic in this repo if the attribute is present elsewhere; otherwise added to the csproj), or (b) test indirectly by asserting the rendered PDF byte stream contains the expected substring after extracting text. Option (a) is cheaper, deterministic, and matches the project's xUnit/FluentAssertions discipline.
   - **Refresh the visual-inspection tests** so the reference PDFs at `<temp>/ExpeditionList_Sample.pdf` and `<temp>/ExpeditionList_126000038_WithNotes.pdf` reflect the new layout for manual sign-off.

2. **Clarify behaviour for genuine zero-price regular items.** The spec says set sub-items render an empty cell because `UnitPrice == 0m`. It should explicitly state: *"Any regular item with `UnitPrice == 0m` will also render an empty cell. Today this case does not occur because Shoptet always returns a non-zero `itemPriceWithVat` for regular items; this dependency is acceptable."* This makes the implicit invariant explicit.

3. **Note that `using System.Globalization;` is not currently imported** in `ExpeditionProtocolDocument.cs` — the brief and spec hedge ("if missing"); confirmed missing. Phrase as a hard requirement in the spec.

4. **Pre-flight check on globalization mode.** Before implementation, confirm the adapter csproj does not set `<InvariantGlobalization>true</InvariantGlobalization>`. If it does, `CultureInfo.GetCultureInfo("cs-CZ")` will throw at runtime and the spec's NFR-3 fails. Add this as a single-line verification step.

## Prerequisites

None — no infrastructure, configuration, migration, or upstream change is required. All needed data (`UnitPrice`) is already populated in production.

Pre-implementation checks (cheap, ~1 minute total):
1. Confirm `<InvariantGlobalization>` is **not** set to `true` in `Anela.Heblo.Adapters.ShoptetApi.csproj` (or any inherited `Directory.Build.props`).
2. Confirm `Anela.Heblo.Tests` already has access to `Anela.Heblo.Adapters.ShoptetApi` internals (existing tests reference `ExpeditionProtocolDocument` publicly, so a switch to `internal` for `FormatPrice` requires `InternalsVisibleTo`); decide upfront whether to test `FormatPrice` directly (preferred) or indirectly.
3. Run `dotnet build` and `dotnet test` once on the unmodified branch to capture a green baseline before edits.