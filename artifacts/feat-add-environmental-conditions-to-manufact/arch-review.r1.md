# Architecture Review: Environmental Conditions in Manufacture Protocol PDF

## Skip Design: true

The change adds Czech labels and a 6-column QuestPDF table to a printed paper protocol — there are no new screens, no new web components, no new design tokens, and the visual styling reuses existing QuestPDF cell helpers (`HeaderCell` / `DataCell`). The frontend `ConditionsReadingsSection.tsx` already establishes the column titles and stage labels; the PDF is just propagating that already-decided design to a different rendering target.

## Architectural Fit Assessment

This is a low-risk, additive change that fits the existing pattern cleanly:

- **Clean Architecture boundaries are preserved.** `ManufactureProtocolData` already lives in `Application/Features/Manufacture/UseCases/GetManufactureProtocol/` as a plain transport class for handler→renderer flow. The `ManufactureProtocolDocument` (API/PDFPrints) consumes it via the `IManufactureProtocolRenderer` interface defined in Application. Adding a nested DTO + property keeps the same shape used by `ManufactureProtocolSemiProduct`, `ManufactureProtocolProduct`, `ManufactureProtocolErpDocument`, `ManufactureProtocolNote`.
- **Domain enums (`ManufactureOrderState`, `ConditionsReadingSource`) are already referenced** elsewhere in the Application/API layers — the Application protocol contract already imports `Anela.Heblo.Domain.Features.Manufacture` (see existing `using` in `ManufactureProtocolData.cs`). No new cross-layer coupling.
- **Repository already eager-loads `ConditionsReadings`** (per spec FR-2). No persistence or query changes.
- **Renderer abstraction unchanged.** Only the QuestPDF document implementation grows a new section; the `IManufactureProtocolRenderer.Render(data)` signature is unaffected.

The one place this *deviates* from the existing convention is intentional and documented: the FE `ConditionsReadingsSection` uses colored badges for `Source != Live`; the PDF uses a textual suffix on the stage cell. This is explicitly the right call — QuestPDF colored badges add visual complexity for no reader benefit on a printed paper protocol.

## Proposed Architecture

### Component Overview

```
[Controller]
  └─ GET /api/manufactureorder/{id}/protocol.pdf
     │
     ▼
[Application]                          (cross-layer DTOs only — no record types)
  GetManufactureProtocolHandler ── uses ──► ManufactureProtocolData
                                              ├─ ...existing nested DTOs
                                              └─ ConditionsReadings: List<ManufactureProtocolConditionsReading>  ◄── NEW
                │
                │ Render(data)
                ▼
        IManufactureProtocolRenderer
                ▲
                │ implements
                │
[API/PDFPrints]
  QuestPdfManufactureProtocolRenderer
                │ delegates to
                ▼
  ManufactureProtocolDocument
     └─ Compose() ── new section: "Podmínky výroby" (between basic-info row and Polotovar)
```

### Key Design Decisions

#### Decision 1: Place `ManufactureProtocolConditionsReading` alongside other protocol DTOs in `ManufactureProtocolData.cs`
**Options considered:**
- A) New nested class in the existing `ManufactureProtocolData.cs` file.
- B) Separate file per DTO (one-type-per-file).

**Chosen approach:** A — append to the existing file.

**Rationale:** The current convention in this exact file is "all protocol DTOs in one file" (`ManufactureProtocolSemiProduct`, `ManufactureProtocolProduct`, `ManufactureProtocolErpDocument`, `ManufactureProtocolNote` are all already co-located). Splitting now would create inconsistency for no benefit. The DTOs are tightly coupled — they all only exist to feed one renderer.

#### Decision 2: Use `class` (not `record`) for `ManufactureProtocolConditionsReading`
**Options considered:**
- A) `class` with `{ get; set; }` properties (matches sibling DTOs).
- B) `record` with positional or `init` properties (matches generic C# coding-style preference for immutable models).

**Chosen approach:** A — `class` with mutable properties.

**Rationale:** Project rule (`CLAUDE.md` + `docs/architecture/development_guidelines.md`) explicitly forbids C# records for DTOs that may cross the OpenAPI generator surface. Although `ManufactureProtocolData` is internal-only today, every existing sibling DTO in this file is a class, so consistency wins. The C# coding-style preference for records is overridden by this project's documented rule.

#### Decision 3: Keep stage label + source annotation as a single composed text cell
**Options considered:**
- A) One stage cell containing `"Polotovar (Částečné)"` etc.
- B) Two separate columns (stage + source).
- C) Stage cell + a separate icon/badge column.

**Chosen approach:** A.

**Rationale:** Mirrors the FE behavior closely (badge appears next to stage label), keeps the table at 6 columns matching the FE schema, and avoids the complexity of styled badges in QuestPDF. The cost — Czech suffix in parentheses — is the right tradeoff for a paper document. Spec FR-3 already pins this.

#### Decision 4: Do not wrap the stage→Czech label mapping in a shared helper
**Options considered:**
- A) Inline `switch` or expression in the Document.
- B) Extract a shared `ManufactureOrderStateLabels` helper in Application or Domain.

**Chosen approach:** A — inline in `ManufactureProtocolDocument`.

**Rationale:** YAGNI. The only place this mapping is needed in the BE is the protocol PDF. The FE has its own `STAGE_LABELS` map; sharing across layers would force a new contract surface for two strings. Inline a private static method (`StageLabel`) inside the Document and let it stay there until a second caller emerges.

#### Decision 5: Skip both heading and table when `ConditionsReadings.Count == 0`
**Options considered:**
- A) Skip the whole section silently (no heading).
- B) Render the heading with an "—" / "Žádné záznamy" placeholder.

**Chosen approach:** A — skip entirely.

**Rationale:** Matches the existing pattern in the same Document: the "Polotovar", "ABRA Flexi doklady", and "Poznámky" sections all already use `if (… != null)` / `if (….Count > 0)` guards (see lines 63, 130, 184). Stay consistent. Spec FR-3 already pins this.

## Implementation Guidance

### Directory / Module Structure

No new files. Modify in place:

```
backend/
  src/
    Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/
      ManufactureProtocolData.cs                      ← add nested type + property
      GetManufactureProtocolHandler.cs                ← add mapping in Handle()
    Anela.Heblo.API/PDFPrints/
      ManufactureProtocolDocument.cs                  ← add section between line 58 and 62
  test/
    Anela.Heblo.Tests/Features/Manufacture/
      GetManufactureProtocolHandlerTests.cs           ← extend BuildCompletedOrder + new test
      ManufactureProtocolRendererSmokeTests.cs        ← extend "full data" fixture
```

### Interfaces and Contracts

**New Application-layer DTO (append to `ManufactureProtocolData.cs`):**

```csharp
public class ManufactureProtocolConditionsReading
{
    public ManufactureOrderState Stage { get; set; }
    public decimal? InnerTemperature { get; set; }
    public decimal? InnerHumidity { get; set; }
    public decimal? OuterTemperature { get; set; }
    public decimal? OuterHumidity { get; set; }
    public DateTime RecordedAt { get; set; }
    public ConditionsReadingSource Source { get; set; }
}
```

**New property on `ManufactureProtocolData`** (placed after `Notes`, before `GeneratedAt`):

```csharp
public List<ManufactureProtocolConditionsReading> ConditionsReadings { get; set; } = new();
```

The class will need `using Anela.Heblo.Domain.Features.Manufacture.Conditions;` added at the top of `ManufactureProtocolData.cs` for `ConditionsReadingSource` (the file already imports `Anela.Heblo.Domain.Features.Manufacture` for `ManufactureType`).

**No changes to:**
- `IManufactureProtocolRenderer`
- `GetManufactureProtocolRequest` / `GetManufactureProtocolResponse`
- Any controller, MediatR pipeline, or OpenAPI surface

### Data Flow

```
GET /api/manufactureorder/{id}/protocol.pdf
  ──► Controller dispatches MediatR request
       ──► GetManufactureProtocolHandler.Handle
            ├─ _repository.GetOrderByIdAsync(id, ct)
            │     └─ EF Core eager-loads .ConditionsReadings (already ordered by RecordedAt)
            │
            ├─ Validate order.State == Completed       (existing)
            ├─ BuildErpDocumentsAsync                  (existing)
            │
            ├─ Build ManufactureProtocolData
            │     ├─ ...existing field mapping
            │     └─ ConditionsReadings = order.ConditionsReadings
            │            .OrderBy(r => r.RecordedAt)   ← explicit defense even though already ordered
            │            .Select(r => new ManufactureProtocolConditionsReading { ... })
            │            .ToList()
            │
            └─ _renderer.Render(data)
                  └─ QuestPdfManufactureProtocolRenderer.Render
                       └─ new ManufactureProtocolDocument(data).GeneratePdf()
                            └─ Compose():
                                 ├─ Header
                                 ├─ Basic-info row
                                 ├─ if (data.ConditionsReadings.Count > 0)    ◄── NEW
                                 │     ├─ "Podmínky výroby" heading (FontSize 11, Bold)
                                 │     └─ 6-col Table: Fáze | T vnitřní | RH vnitřní | T venkovní | RH venkovní | Zaznamenáno
                                 │           reusing HeaderCell / DataCell helpers
                                 ├─ Polotovar section
                                 ├─ Výrobky table
                                 ├─ ABRA Flexi doklady
                                 └─ Poznámky
```

**Cell formatting contract (must match):**
- Numeric: `value?.ToString("0.0", CultureInfo.InvariantCulture) ?? "—"` — note: `CultureInfo.InvariantCulture` will render `.` as the decimal separator, **not** Czech `,`. This is intentional and matches the FE `toFixed(1)` behavior; spec FR-3 explicitly pins `InvariantCulture`. The Document will need `using System.Globalization;`.
- Date: `r.RecordedAt.ToString("dd.MM.yyyy HH:mm")` — matches the existing `GeneratedAt` and `Notes.CreatedAt` formatting in the document.
- Stage label: private static helper inside the Document, returning `"Polotovar" | "Dokončeno" | <enum name>`.
- Source suffix: appended to the stage cell text; empty when `Source == Live`, `" (HA nedostupný)"` for `Unavailable`, `" (Částečné)"` for `Partial`. Built as a single `Text(stageLabel + suffix)` call (no `Span` styling needed).

**Column proportions (recommended starting point — adjust on visual inspection):**
```csharp
cols.RelativeColumn(3);     // Fáze (needs room for "Dokončeno (HA nedostupný)")
cols.RelativeColumn(2);     // T vnitřní
cols.RelativeColumn(2);     // RH vnitřní
cols.RelativeColumn(2);     // T venkovní
cols.RelativeColumn(2);     // RH venkovní
cols.RelativeColumn(3);     // Zaznamenáno (dd.MM.yyyy HH:mm)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Czech decimal comma vs invariant decimal point mismatch confuses operators (PDF shows `21.5`, FE may show `21,5`). | LOW | Spec FR-3 pins `InvariantCulture`. The FE also uses `toFixed(1)` which emits a `.`, so the surfaces are consistent. Document this in inline comment if it surprises a future reader; otherwise leave. |
| Stage column overflow when source annotation is appended ("Dokončeno (HA nedostupný)"). | LOW | Use the recommended `RelativeColumn(3)` for Fáze; QuestPDF wraps text by default. Manual PDF inspection in verification step will surface any wrap issues. |
| Renderer smoke test's text-search assertion (`"Podmínky výroby"` in PDF bytes) flakes due to PDF stream compression. | MEDIUM | Spec FR-5 already accepts this — fall back to magic-byte assertion + "render doesn't throw". Do not invest in PDF parsing here; the handler unit test already proves the data is passed correctly. |
| Adding a 6-column table between the basic-info row and Polotovar shifts subsequent content down a page, causing pagination regressions in long orders. | LOW | QuestPDF reflows automatically; the page footer with `CurrentPageNumber/TotalPages` handles pagination. Manual PDF inspection in verification covers this. |
| Future addition of new `ManufactureOrderState` values (e.g. another stage that captures readings) silently falls back to the enum name in the PDF. | LOW | Acceptable — the inline `StageLabel` helper falls through to `state.ToString()` for unknown values, matching spec FR-3. Document via the spec, not a code comment. |
| Cross-layer leak: `ConditionsReadingSource` enum (Domain) reaches Application DTOs. | LOW (already accepted) | Already established — `ManufactureProtocolData` references Domain types (`ManufactureType`, `ManufactureErpDocumentItem`). This is the project's existing pattern for protocol-internal DTOs; no new architectural debt introduced. |

## Specification Amendments

The spec is already complete and tightly scoped. Two minor clarifications worth pinning before implementation begins:

1. **FR-3 column proportions.** Spec describes columns but not relative widths. Pin: Fáze=3, four numeric=2 each, Zaznamenáno=3 (rationale above). Adjust during manual PDF inspection if needed.
2. **FR-3 `using System.Globalization;`** must be added to `ManufactureProtocolDocument.cs` — the file does not currently import it (only `QuestPDF.*` is imported). Trivial but worth noting because the spec calls for `InvariantCulture` formatting.
3. **FR-1 `using` for `ConditionsReadingSource`.** `ManufactureProtocolData.cs` currently imports `Anela.Heblo.Domain.Features.Manufacture` only; the new enum lives in `Anela.Heblo.Domain.Features.Manufacture.Conditions`. Add the `Conditions` sub-namespace import.

No structural amendments to the spec are required.

## Prerequisites

None. All required infrastructure exists:

- ✓ `ManufactureOrderConditionsReading` entity (Domain)
- ✓ `ConditionsReadingSource` enum (Domain)
- ✓ `ManufactureOrderState` enum (Domain)
- ✓ Eager-loading of `ConditionsReadings` in `ManufactureOrderRepository.GetOrderByIdAsync` (line 120)
- ✓ QuestPDF dependency + license registration in `QuestPdfManufactureProtocolRenderer`
- ✓ Renderer mock pattern in `GetManufactureProtocolHandlerTests`
- ✓ Smoke-test scaffold in `ManufactureProtocolRendererSmokeTests`

Implementation can begin immediately. No DB migration, no config change, no DI registration update, no OpenAPI regeneration, no FE work.