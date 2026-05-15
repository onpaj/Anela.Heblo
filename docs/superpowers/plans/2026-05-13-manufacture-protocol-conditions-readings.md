# Manufacture Protocol — Environmental Conditions Section Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render a "Podmínky výroby" (Manufacturing Conditions) table in the printed manufacture protocol PDF, displaying the per-stage environmental sensor readings (inner/outer temperature and humidity) already captured during the order workflow.

**Architecture:** Backend-only, additive change. Extends the in-process `ManufactureProtocolData` transport class with a new nested DTO + collection, maps the already-eager-loaded `ManufactureOrder.ConditionsReadings` in `GetManufactureProtocolHandler`, and renders a conditional 6-column QuestPDF table in `ManufactureProtocolDocument` between the basic-info row and the Polotovar section. Reuses existing `HeaderCell` / `DataCell` styling helpers. No DB migration, no DTO contract change, no OpenAPI regeneration, no frontend work.

**Tech Stack:** .NET 8, C#, MediatR, QuestPDF, xUnit + FluentAssertions + Moq. Existing dependencies only.

---

## Context Pointers

These are the only files this plan touches. Read them once before starting Task 1:

- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/ManufactureProtocolData.cs` — the transport class. All protocol DTOs are co-located in this single file (existing convention).
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs` — maps `ManufactureOrder` → `ManufactureProtocolData`, then calls `IManufactureProtocolRenderer.Render(data)`.
- `backend/src/Anela.Heblo.API/PDFPrints/ManufactureProtocolDocument.cs` — the QuestPDF document. `HeaderCell` and `DataCell` helpers live at lines 216–222.
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrderConditionsReading.cs` — source entity (already eager-loaded by `ManufactureOrderRepository.GetOrderByIdAsync` at line 120 of that repository).
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/ConditionsReadingSource.cs` — `enum { Live = 1, Partial = 2, Unavailable = 3 }`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs` — handler tests with renderer mock capture pattern (`capturedData` at lines 114–118).
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureProtocolRendererSmokeTests.cs` — smoke test asserting PDF magic bytes.

Working directory for all commands below: `/Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-add-environmental-conditions-to-manufact`.

---

## File Structure

No new files. Five files modified in place:

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/ManufactureProtocolData.cs` | Add `using` for `Conditions` namespace; append `ManufactureProtocolConditionsReading` class; add `ConditionsReadings` property to `ManufactureProtocolData`. |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs` | Project `order.ConditionsReadings` into the protocol data in `Handle`. |
| `backend/src/Anela.Heblo.API/PDFPrints/ManufactureProtocolDocument.cs` | Add `using System.Globalization;` and `using Anela.Heblo.Domain.Features.Manufacture;` + `Conditions` namespace; insert a conditional "Podmínky výroby" section in `Compose` between the basic-info row and the Polotovar section; add a private static `StageLabel` helper. |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs` | Extend `BuildCompletedOrder` with two `ConditionsReadings` entries; add `Handle_CompletedOrder_MapsConditionsReadingsToProtocolData` test. |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureProtocolRendererSmokeTests.cs` | Populate `ConditionsReadings` on the "full data" fixture; assert render does not throw + magic bytes remain valid. |

---

## Task 1: Extend the protocol data contract

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/ManufactureProtocolData.cs`

This is a pure additive type change. We add the new nested DTO and the collection property, but do NOT yet map or render anything. After this task, the solution still builds (no callers reference the new property).

- [ ] **Step 1.1: Add `using` directive for the `Conditions` sub-namespace**

The existing file imports only `Anela.Heblo.Domain.Features.Manufacture`. We need `ConditionsReadingSource` from the `.Conditions` sub-namespace.

Edit `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/ManufactureProtocolData.cs` line 1, replacing:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;
```

with:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
```

- [ ] **Step 1.2: Append the new nested DTO class at the end of the file**

Append after the existing `ManufactureProtocolNote` class (after line 56, at end of file):

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

- [ ] **Step 1.3: Add the `ConditionsReadings` collection property on `ManufactureProtocolData`**

Inside `ManufactureProtocolData` (the class starting at line 5), insert a new property between the existing `Notes` property (line 18) and `GeneratedAt` (line 20). The line that currently reads:

```csharp
    public List<ManufactureProtocolNote> Notes { get; set; } = new();

    public DateTime GeneratedAt { get; set; }
```

becomes:

```csharp
    public List<ManufactureProtocolNote> Notes { get; set; } = new();

    public List<ManufactureProtocolConditionsReading> ConditionsReadings { get; set; } = new();

    public DateTime GeneratedAt { get; set; }
```

- [ ] **Step 1.4: Build the backend**

Run:

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-add-environmental-conditions-to-manufact
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds. No new warnings related to the change.

- [ ] **Step 1.5: Apply formatting**

Run:

```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/ManufactureProtocolData.cs
```

- [ ] **Step 1.6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/ManufactureProtocolData.cs
git commit -m "feat: add ConditionsReadings DTO to ManufactureProtocolData"
```

---

## Task 2: Failing handler test for the conditions-readings mapping

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs`

Write the test before the handler change. Test must fail with the current handler (mapping not implemented yet).

- [ ] **Step 2.1: Add `using` directive for the `Conditions` sub-namespace**

The test file currently imports only `Anela.Heblo.Domain.Features.Manufacture`. Add a second `using` so `ConditionsReadingSource` resolves.

Edit `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs` line 2, replacing:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;
```

with:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
```

- [ ] **Step 2.2: Extend `BuildCompletedOrder` with two conditions readings**

Edit the `BuildCompletedOrder` helper. Locate the closing brace of the order initializer (the line `DocProductReceiptDate = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),` on line 204). Add a `ConditionsReadings` collection right after that line (still inside the object initializer). Replace:

```csharp
            DocProductReceipt = "V-PROD-001",
            DocProductReceiptDate = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
        };
    }
```

with:

```csharp
            DocProductReceipt = "V-PROD-001",
            DocProductReceiptDate = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
            ConditionsReadings = new List<ManufactureOrderConditionsReading>
            {
                new()
                {
                    Stage = ManufactureOrderState.SemiProductManufactured,
                    InnerTemperature = 21.4m,
                    InnerHumidity = 45.2m,
                    OuterTemperature = 18.9m,
                    OuterHumidity = 52.1m,
                    RecordedAt = new DateTime(2026, 4, 1, 10, 30, 0, DateTimeKind.Utc),
                    Source = ConditionsReadingSource.Live,
                },
                new()
                {
                    Stage = ManufactureOrderState.Completed,
                    InnerTemperature = 22.0m,
                    InnerHumidity = null,
                    OuterTemperature = null,
                    OuterHumidity = null,
                    RecordedAt = new DateTime(2026, 4, 2, 14, 0, 0, DateTimeKind.Utc),
                    Source = ConditionsReadingSource.Partial,
                },
            },
        };
    }
```

Why two readings, one fully populated `Live` and one partially-null `Partial`: covers the two stages defined by the workflow, the null-decimal mapping, and a non-`Live` source value — exactly the variations the renderer must also tolerate (Task 5).

- [ ] **Step 2.3: Add the new test method**

Insert this test method immediately after the existing `Handle_CompletedOrder_MapsOrderDataToProtocolData` method (after the closing brace at line 158, before the `BuildCompletedOrder` helper). The method mirrors the existing `capturedData` capture pattern.

```csharp
    [Fact]
    public async Task Handle_CompletedOrder_MapsConditionsReadingsToProtocolData()
    {
        var order = BuildCompletedOrder();
        _repositoryMock
            .Setup(r => r.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _flexiMock
            .Setup(x => x.GetErpDocumentItemsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureErpDocumentItem>());

        ManufactureProtocolData? capturedData = null;
        _rendererMock
            .Setup(r => r.Render(It.IsAny<ManufactureProtocolData>()))
            .Callback<ManufactureProtocolData>(d => capturedData = d)
            .Returns(PdfMagicBytes);

        await _handler.Handle(new GetManufactureProtocolRequest { Id = 1 }, CancellationToken.None);

        capturedData.Should().NotBeNull();
        capturedData!.ConditionsReadings.Should().HaveCount(2);

        var first = capturedData.ConditionsReadings[0];
        first.Stage.Should().Be(ManufactureOrderState.SemiProductManufactured);
        first.InnerTemperature.Should().Be(21.4m);
        first.InnerHumidity.Should().Be(45.2m);
        first.OuterTemperature.Should().Be(18.9m);
        first.OuterHumidity.Should().Be(52.1m);
        first.RecordedAt.Should().Be(new DateTime(2026, 4, 1, 10, 30, 0, DateTimeKind.Utc));
        first.Source.Should().Be(ConditionsReadingSource.Live);

        var second = capturedData.ConditionsReadings[1];
        second.Stage.Should().Be(ManufactureOrderState.Completed);
        second.InnerTemperature.Should().Be(22.0m);
        second.InnerHumidity.Should().BeNull();
        second.OuterTemperature.Should().BeNull();
        second.OuterHumidity.Should().BeNull();
        second.RecordedAt.Should().Be(new DateTime(2026, 4, 2, 14, 0, 0, DateTimeKind.Utc));
        second.Source.Should().Be(ConditionsReadingSource.Partial);
    }
```

- [ ] **Step 2.4: Run the new test — expect it to FAIL**

Run:

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-add-environmental-conditions-to-manufact
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetManufactureProtocolHandlerTests.Handle_CompletedOrder_MapsConditionsReadingsToProtocolData"
```

Expected: 1 failed. Failure message comes from `capturedData!.ConditionsReadings.Should().HaveCount(2);` because the handler has not yet been updated — `ConditionsReadings` is still an empty list.

Also re-run the whole `GetManufactureProtocolHandlerTests` class to make sure the `BuildCompletedOrder` change did not regress anything else:

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetManufactureProtocolHandlerTests"
```

Expected: only `Handle_CompletedOrder_MapsConditionsReadingsToProtocolData` fails. The other five tests pass (the `ConditionsReadings` addition to `BuildCompletedOrder` is additive and does not touch fields they assert on).

If any pre-existing test fails, stop and inspect — that means the `BuildCompletedOrder` extension was wrong.

- [ ] **Step 2.5: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs
git commit -m "test: add failing handler test for conditions-readings mapping"
```

---

## Task 3: Implement the handler mapping

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs`

- [ ] **Step 3.1: Add the projection inside `Handle`**

Locate the `var data = new ManufactureProtocolData { ... }` block starting at line 37. Find the `Notes = order.Notes.Select(...).ToList(),` block (lines 66–71) and add the new `ConditionsReadings` projection immediately after it, before `GeneratedAt`. Replace this:

```csharp
            Notes = order.Notes.Select(n => new ManufactureProtocolNote
            {
                CreatedAt = n.CreatedAt,
                CreatedByUser = n.CreatedByUser,
                Text = n.Text,
            }).ToList(),
            GeneratedAt = DateTime.UtcNow,
```

with this:

```csharp
            Notes = order.Notes.Select(n => new ManufactureProtocolNote
            {
                CreatedAt = n.CreatedAt,
                CreatedByUser = n.CreatedByUser,
                Text = n.Text,
            }).ToList(),
            ConditionsReadings = order.ConditionsReadings
                .OrderBy(r => r.RecordedAt)
                .Select(r => new ManufactureProtocolConditionsReading
                {
                    Stage = r.Stage,
                    InnerTemperature = r.InnerTemperature,
                    InnerHumidity = r.InnerHumidity,
                    OuterTemperature = r.OuterTemperature,
                    OuterHumidity = r.OuterHumidity,
                    RecordedAt = r.RecordedAt,
                    Source = r.Source,
                })
                .ToList(),
            GeneratedAt = DateTime.UtcNow,
```

The repository (`ManufactureOrderRepository.GetOrderByIdAsync` line 120) already eager-loads with `.OrderBy(r => r.RecordedAt)`, but the explicit `.OrderBy` here is the contract the handler enforces — defensive against future repository changes and zero cost on a small list.

Note: no new `using` directives are needed in this file. `ManufactureOrderConditionsReading.Stage` is a `ManufactureOrderState` (already imported via `Anela.Heblo.Domain.Features.Manufacture`), and `ConditionsReadingSource` is reached through the property type (the compiler resolves it through the entity's namespace).

- [ ] **Step 3.2: Build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: succeeds.

- [ ] **Step 3.3: Run the new test — expect PASS**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetManufactureProtocolHandlerTests.Handle_CompletedOrder_MapsConditionsReadingsToProtocolData"
```

Expected: 1 passed.

- [ ] **Step 3.4: Run the full handler test class — expect ALL PASS**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetManufactureProtocolHandlerTests"
```

Expected: 6 passed, 0 failed.

- [ ] **Step 3.5: Apply formatting**

```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs
```

- [ ] **Step 3.6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs
git commit -m "feat: map ConditionsReadings from order to protocol data"
```

---

## Task 4: Render the "Podmínky výroby" section in the PDF

**Files:**
- Modify: `backend/src/Anela.Heblo.API/PDFPrints/ManufactureProtocolDocument.cs`

This is the only step where the printed PDF actually grows a new section. The architecture review pinned the column proportions, the stage-label helper location, the `InvariantCulture` formatting decision, and the source-suffix text — all encoded directly in the code below.

- [ ] **Step 4.1: Add new `using` directives**

The file currently has only:

```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
```

Replace that block with:

```csharp
using System.Globalization;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
```

- `System.Globalization` — for `CultureInfo.InvariantCulture`.
- `Anela.Heblo.Domain.Features.Manufacture` — for `ManufactureOrderState` (used by the `StageLabel` helper switch).
- `Anela.Heblo.Domain.Features.Manufacture.Conditions` — for `ConditionsReadingSource` (used by the source-suffix logic).

- [ ] **Step 4.2: Insert the new section in `Compose`**

The basic-info row in `Compose` ends at line 58. Line 60 is `col.Item().PaddingTop(10);` (the spacer before the Polotovar section that starts at line 62). Insert the new section right after that spacer.

Locate this block (currently lines 58–62):

```csharp
                });

                col.Item().PaddingTop(10);

                // Semi-product section
                if (_data.SemiProduct != null)
```

Replace it with:

```csharp
                });

                col.Item().PaddingTop(10);

                // Conditions readings section
                if (_data.ConditionsReadings.Count > 0)
                {
                    col.Item().Text("Podmínky výroby").FontSize(11).Bold();
                    col.Item().PaddingTop(4);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(3);   // Fáze
                            cols.RelativeColumn(2);   // T vnitřní (°C)
                            cols.RelativeColumn(2);   // RH vnitřní (%)
                            cols.RelativeColumn(2);   // T venkovní (°C)
                            cols.RelativeColumn(2);   // RH venkovní (%)
                            cols.RelativeColumn(3);   // Zaznamenáno
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Fáze").Bold();
                            header.Cell().Element(HeaderCell).Text("T vnitřní (°C)").Bold();
                            header.Cell().Element(HeaderCell).Text("RH vnitřní (%)").Bold();
                            header.Cell().Element(HeaderCell).Text("T venkovní (°C)").Bold();
                            header.Cell().Element(HeaderCell).Text("RH venkovní (%)").Bold();
                            header.Cell().Element(HeaderCell).Text("Zaznamenáno").Bold();
                        });

                        foreach (var reading in _data.ConditionsReadings)
                        {
                            table.Cell().Element(DataCell).Text(StageLabel(reading.Stage) + SourceSuffix(reading.Source));
                            table.Cell().Element(DataCell).Text(FormatDecimal(reading.InnerTemperature));
                            table.Cell().Element(DataCell).Text(FormatDecimal(reading.InnerHumidity));
                            table.Cell().Element(DataCell).Text(FormatDecimal(reading.OuterTemperature));
                            table.Cell().Element(DataCell).Text(FormatDecimal(reading.OuterHumidity));
                            table.Cell().Element(DataCell).Text(reading.RecordedAt.ToString("dd.MM.yyyy HH:mm"));
                        }
                    });

                    col.Item().PaddingTop(10);
                }

                // Semi-product section
                if (_data.SemiProduct != null)
```

- [ ] **Step 4.3: Add the three private static helper methods**

The file ends with two private static helpers `HeaderCell` and `DataCell` (lines 216–222). Add three new helpers after them — `StageLabel`, `SourceSuffix`, `FormatDecimal`. Locate the end of the class (the closing brace after `DataCell` definition at line 222–223):

```csharp
    private static IContainer DataCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3);
}
```

Replace with:

```csharp
    private static IContainer DataCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3);

    private static string StageLabel(ManufactureOrderState state) => state switch
    {
        ManufactureOrderState.SemiProductManufactured => "Polotovar",
        ManufactureOrderState.Completed => "Dokončeno",
        _ => state.ToString(),
    };

    private static string SourceSuffix(ConditionsReadingSource source) => source switch
    {
        ConditionsReadingSource.Live => string.Empty,
        ConditionsReadingSource.Partial => " (Částečné)",
        ConditionsReadingSource.Unavailable => " (HA nedostupný)",
        _ => string.Empty,
    };

    private static string FormatDecimal(decimal? value) =>
        value?.ToString("0.0", CultureInfo.InvariantCulture) ?? "—";
}
```

Rationale recap for reviewers:
- `StageLabel` and `SourceSuffix` live in the document (not a shared helper) because no other backend caller needs them — the FE has its own `STAGE_LABELS` map. YAGNI; extract later if a second caller appears.
- `FormatDecimal` uses `InvariantCulture` (decimal point) — pinned by spec FR-3 and matches the FE `toFixed(1)` output.
- The unknown-enum fallback in `StageLabel` falls through to `state.ToString()` (matches spec FR-3 acceptance criterion).

- [ ] **Step 4.4: Build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: succeeds.

- [ ] **Step 4.5: Run the renderer smoke tests (they still cover the happy path with the new code)**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ManufactureProtocolRendererSmokeTests"
```

Expected: 2 passed (the existing tests). The "full data" test does not yet populate `ConditionsReadings`, so the new section is skipped via the `Count > 0` guard — no behavior change for the existing fixtures.

- [ ] **Step 4.6: Apply formatting**

```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.API/PDFPrints/ManufactureProtocolDocument.cs
```

- [ ] **Step 4.7: Commit**

```bash
git add backend/src/Anela.Heblo.API/PDFPrints/ManufactureProtocolDocument.cs
git commit -m "feat: render Podmínky výroby section in protocol PDF"
```

---

## Task 5: Extend the renderer smoke test to exercise the new section

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureProtocolRendererSmokeTests.cs`

The handler test already proves that `ConditionsReadings` is mapped correctly. The smoke test's job is to prove that the renderer does not throw when those readings reach it — including the partial-null case. The text-content assertion is best-effort (see spec FR-5 risk note).

- [ ] **Step 5.1: Add `using` directives for the domain enums**

The file currently imports `Anela.Heblo.Domain.Features.Manufacture` (for `ManufactureErpDocumentItem`). Add the Conditions sub-namespace and ensure `System.Text` is available for the best-effort text search.

Edit `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureProtocolRendererSmokeTests.cs` lines 1–5. Replace:

```csharp
using Anela.Heblo.API.PDFPrints;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Xunit;
```

with:

```csharp
using Anela.Heblo.API.PDFPrints;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using FluentAssertions;
using Xunit;
```

- [ ] **Step 5.2: Populate `ConditionsReadings` in the "full data" fixture**

In `Render_ReturnsValidPdfBytes` (line 16), find the `Notes = new List<ManufactureProtocolNote> { ... }` initializer (lines 62–70) and add `ConditionsReadings` immediately after it.

Replace this block (ends at line 71):

```csharp
            Notes = new List<ManufactureProtocolNote>
            {
                new ManufactureProtocolNote
                {
                    Text = "Testovací poznámka",
                    CreatedAt = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc),
                    CreatedByUser = "user@anela.cz",
                },
            },
        };
```

with:

```csharp
            Notes = new List<ManufactureProtocolNote>
            {
                new ManufactureProtocolNote
                {
                    Text = "Testovací poznámka",
                    CreatedAt = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc),
                    CreatedByUser = "user@anela.cz",
                },
            },
            ConditionsReadings = new List<ManufactureProtocolConditionsReading>
            {
                new ManufactureProtocolConditionsReading
                {
                    Stage = ManufactureOrderState.SemiProductManufactured,
                    InnerTemperature = 21.5m,
                    InnerHumidity = 45.0m,
                    OuterTemperature = 19.0m,
                    OuterHumidity = 50.0m,
                    RecordedAt = new DateTime(2024, 6, 1, 10, 30, 0, DateTimeKind.Utc),
                    Source = ConditionsReadingSource.Live,
                },
                new ManufactureProtocolConditionsReading
                {
                    Stage = ManufactureOrderState.Completed,
                    InnerTemperature = 22.0m,
                    InnerHumidity = null,
                    OuterTemperature = null,
                    OuterHumidity = null,
                    RecordedAt = new DateTime(2024, 6, 1, 14, 0, 0, DateTimeKind.Utc),
                    Source = ConditionsReadingSource.Partial,
                },
            },
        };
```

This fixture covers: both stages, the `Live` and `Partial` source values (the latter triggers the suffix code path), all four sensor values populated in row 1, all four nulls (i.e. the em-dash branch in `FormatDecimal`) in row 2.

- [ ] **Step 5.3: Keep the existing magic-byte assertion; no additional text-content assertion**

The existing assertions at lines 75–79 (`bytes.Should().NotBeNullOrEmpty()` and the four magic-byte checks) remain unchanged. We deliberately do not add a UTF-8 substring search for `"Podmínky výroby"` — QuestPDF compresses content streams, so such an assertion would flake. The spec FR-5 risk note accepts this: the handler unit test (Task 2/3) proves the data flows correctly, and the "render does not throw" guarantee proves the document accepts that data.

No edit required in this step — just confirm by re-reading the test.

- [ ] **Step 5.4: Build and run the smoke tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-add-environmental-conditions-to-manufact
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ManufactureProtocolRendererSmokeTests"
```

Expected: 2 passed. The "full data" test now exercises the new section end-to-end; the minimal-data test exercises the `Count == 0` skip branch.

- [ ] **Step 5.5: Apply formatting**

```bash
dotnet format backend/Anela.Heblo.sln --include backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureProtocolRendererSmokeTests.cs
```

- [ ] **Step 5.6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureProtocolRendererSmokeTests.cs
git commit -m "test: extend renderer smoke test with conditions readings"
```

---

## Task 6: Full verification

**Files:** none (read-only commands).

- [ ] **Step 6.1: Build the whole solution**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-add-environmental-conditions-to-manufact
dotnet build backend/Anela.Heblo.sln
```

Expected: Build succeeded, 0 errors. No new warnings.

- [ ] **Step 6.2: Run the targeted protocol tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Manufacture.GetManufactureProtocolHandlerTests|FullyQualifiedName~Manufacture.ManufactureProtocolRendererSmokeTests"
```

Expected: 8 passed (6 handler tests + 2 smoke tests), 0 failed.

- [ ] **Step 6.3: Run the full Manufacture feature test suite (regression guard)**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Features.Manufacture"
```

Expected: all tests pass. If anything unrelated fails, it is not caused by this change — re-run on `main` to confirm baseline.

- [ ] **Step 6.4: Apply final formatting pass**

```bash
dotnet format backend/Anela.Heblo.sln
```

Expected: no remaining formatting changes (we formatted per-task already). If there are unrelated formatting changes, leave them out of this branch — `git checkout -- <files>` to drop them. Only commit changes touching the files this plan modifies.

- [ ] **Step 6.5: Manual PDF inspection (smoke verification)**

Per spec verification step 5:

1. Start the backend locally (`dotnet run --project backend/src/Anela.Heblo.API` or via the existing dev setup in `docs/development/setup.md`).
2. Open a `Completed` `ManufactureOrder` that has conditions readings recorded. If the dev DB has none, this manual step can be skipped — the test fixtures cover the rendering path.
3. Click "Tisk protokolu" on the order detail.
4. Confirm the printed PDF shows a "Podmínky výroby" section between the basic-info row and the "Polotovar" section, with:
   - Column headers: Fáze, T vnitřní (°C), RH vnitřní (%), T venkovní (°C), RH venkovní (%), Zaznamenáno.
   - Stage rendered in Czech ("Polotovar" / "Dokončeno").
   - Source annotation appended in parentheses for non-`Live` rows (e.g. " (Částečné)" or " (HA nedostupný)").
   - Decimal values shown with one decimal place using a period (e.g. `21.5`).
   - Em-dash `—` shown for null sensor values.
   - `RecordedAt` formatted as `dd.MM.yyyy HH:mm`.
   - For an order with zero readings, the section (heading + table) is omitted entirely; the basic-info row flows directly into Polotovar.

This step is best-effort — fall back to test coverage if a Completed-with-readings order is not available locally.

- [ ] **Step 6.6: Done**

No further commits — all five working commits are already on the branch.

---

## Out of Scope (do not implement)

- New manual temperature/humidity input fields on `ManufactureOrder`.
- Surfacing conditions readings anywhere outside the PDF.
- Internationalisation of new labels — the PDF stays hardcoded Czech.
- Frontend changes to the existing `ConditionsReadingsSection.tsx`.
- Colored badges in the PDF — replaced with textual parenthetical suffix.
- Repository, domain, or DB schema changes.
- OpenAPI client regeneration (the new DTO is internal to the use case).

## Self-Review

**Spec coverage check (against `spec.r1.md`):**

| Spec section | Covered by |
|---|---|
| FR-1 (extend protocol contract) | Task 1 (DTO + property + using) |
| FR-2 (map readings in handler) | Task 3 (projection in `Handle`) |
| FR-3 (render section in PDF) | Task 4 (table + `StageLabel` + `SourceSuffix` + `FormatDecimal` + `Count > 0` guard) |
| FR-4 (handler-level unit test) | Task 2 (`Handle_CompletedOrder_MapsConditionsReadingsToProtocolData`) |
| FR-5 (renderer smoke test) | Task 5 (full-data fixture extended with two readings) |
| NFR-1 (no DB round-trips) | Task 3 reuses already-eager-loaded `order.ConditionsReadings`; no repository call added |
| NFR-2 (no new auth/PII surface) | No endpoint change; new data is non-PII telemetry |
| NFR-3 (no DTO / OpenAPI / FE impact) | DTOs internal to the use case; no controller/contract change |
| NFR-4 (style + `dotnet build`/`format`) | Every task includes `dotnet build` and `dotnet format` steps |
| Verification step 1–4 | Task 6 |
| Verification step 5 (manual inspection) | Task 6.5 |

**Arch-review amendments check:**

1. Column proportions pinned (`3, 2, 2, 2, 2, 3`) — Task 4 Step 4.2. ✓
2. `using System.Globalization;` added to the Document — Task 4 Step 4.1. ✓
3. `using Anela.Heblo.Domain.Features.Manufacture.Conditions;` added to `ManufactureProtocolData.cs` — Task 1 Step 1.1. ✓

**Placeholder scan:** no "TBD", "TODO", "implement later", "similar to", or hand-wavy steps. Every code-producing step contains the full code to write. ✓

**Type consistency check:**

- DTO type name `ManufactureProtocolConditionsReading` appears identically in Tasks 1, 3, 5. ✓
- Property names (`InnerTemperature`, `InnerHumidity`, `OuterTemperature`, `OuterHumidity`, `RecordedAt`, `Source`, `Stage`) match across all five files (DTO, handler, document, two tests). ✓
- Helper method names (`StageLabel`, `SourceSuffix`, `FormatDecimal`) used in the table loop match the definitions at the bottom of the class in Task 4. ✓
- Source-suffix strings (`" (Částečné)"`, `" (HA nedostupný)"`) appear identically in spec FR-3 and Task 4 Step 4.3. ✓
- Stage-label strings (`"Polotovar"`, `"Dokončeno"`) match spec FR-3 and Task 4 Step 4.3. ✓
