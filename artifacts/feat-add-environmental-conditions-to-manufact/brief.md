# Add environmental conditions to manufacture protocol PDF

## Context

The manufacture order workflow already auto-captures environmental conditions from a Home Assistant sensor adapter on two state transitions (`SemiProductManufactured`, `Completed`). Each reading stores four decimal values: `InnerTemperature`, `InnerHumidity`, `OuterTemperature`, `OuterHumidity` — plus stage, `RecordedAt`, and source quality (`Live | Partial | Unavailable`). These readings are exposed in the order detail UI under the "Podmínky výroby" tab, but they are **not** rendered in the printed protocol PDF that operators print at the end of a batch.

The goal is to add a "Podmínky výroby" section to the protocol PDF showing the captured readings per stage, so the printed protocol is complete evidence of the conditions during manufacture. No new fields, no DB migration, no FE changes — the data is already eager-loaded by the repository; only the protocol mapping and the QuestPDF document need updates.

## Scope

Backend only. Two source files + two test files. No migration, no DTO changes, no OpenAPI regeneration, no FE work.

## Files to modify

### 1. `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/ManufactureProtocolData.cs`

Add a new nested type and a collection property on `ManufactureProtocolData`:

- New class `ManufactureProtocolConditionsReading` with: `ManufactureOrderState Stage`, `decimal? InnerTemperature`, `decimal? InnerHumidity`, `decimal? OuterTemperature`, `decimal? OuterHumidity`, `DateTime RecordedAt`, `ConditionsReadingSource Source`.
- New property on `ManufactureProtocolData`: `public List<ManufactureProtocolConditionsReading> ConditionsReadings { get; set; } = new();` placed alongside `Notes` and `ErpDocuments`.

Use the existing `ManufactureOrderState` and `ConditionsReadingSource` enums from `Anela.Heblo.Domain.Features.Manufacture.*` — the PDF document already references the order state enum directly.

### 2. `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs`

In `Handle` (currently at line 22-82), after the existing field assignments and before `BuildErpDocumentsAsync`, map the readings:

```csharp
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
```

The repository (`ManufactureOrderRepository.GetOrderByIdAsync`, line 120) already eager-loads `.Include(x => x.ConditionsReadings.OrderBy(r => r.RecordedAt))`, so no repository change.

### 3. `backend/src/Anela.Heblo.API/PDFPrints/ManufactureProtocolDocument.cs`

Insert a new "Podmínky výroby" section between the basic-info row (current line ~58) and the "Polotovar" section (current line ~62). Render only when `data.ConditionsReadings.Count > 0`.

Layout: a 6-column table mirroring `frontend/src/components/manufacture/detail/ConditionsReadingsSection.tsx` columns 56-63:

| Fáze | T vnitřní (°C) | RH vnitřní (%) | T venkovní (°C) | RH venkovní (%) | Zaznamenáno |

Formatting:
- Numeric cells: `value?.ToString("0.0", CultureInfo.InvariantCulture) ?? "—"` (one decimal, em-dash for null — matches the FE `ValueCell` `toFixed(1)` behavior).
- Stage cell: map enum to Czech label — "Polotovar" for `SemiProductManufactured`, "Dokončeno" for `Completed`, fallback to enum name. Mirror the `STAGE_LABELS` map in `ConditionsReadingsSection.tsx:7-11`.
- `RecordedAt`: format as `dd.MM.yyyy HH:mm` (match the existing date format used elsewhere in the document — check the basic-info row formatting at line 39-58 for the precedent).
- Source: if not `Live`, append a small annotation suffix (e.g. " (HA nedostupný)" / " (Částečné)") to the stage cell, mirroring `getSourceBadge` in `ConditionsReadingsSection.tsx:17-37`. Keep it textual since QuestPDF colored badges would add complexity for low value.

Use the same QuestPDF table-cell helpers/styles as the existing "Výrobky" table (current lines 100-126) to stay consistent.

### 4. `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs`

- Extend `BuildCompletedOrder` helper (line 160-206) to attach two `ManufactureOrderConditionsReading` entries — one per stage with representative values — so existing tests cover the new mapping path.
- Add one focused test: `Handle_CompletedOrder_MapsConditionsReadingsToProtocolData` — asserts the renderer is called with a `ManufactureProtocolData` whose `ConditionsReadings` contains 2 entries with correct stages, values, `RecordedAt`, and `Source`. Use the existing `Mock<IManufactureProtocolRenderer>` capture pattern already used in the file.

### 5. `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureProtocolRendererSmokeTests.cs`

- Extend the "full data" smoke test fixture to populate `ConditionsReadings` with two stages (one fully-populated `Live`, one partially-populated `Partial` to exercise null handling and source annotation).
- The existing assertion (PDF magic bytes) is sufficient. Add a second assertion checking that `Encoding.UTF8.GetString(bytes)` contains a recognizable marker (e.g. "Podmínky výroby") to guard against the section being silently dropped. Note: QuestPDF output is a PDF stream — string-search inside is unreliable for compressed content; if it fails, fall back to only the magic-bytes assertion plus a render-doesn't-throw guarantee.

## Critical files

- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/ManufactureProtocolData.cs` — add type + property
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs` — map readings
- `backend/src/Anela.Heblo.API/PDFPrints/ManufactureProtocolDocument.cs` — render new section
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs` — mapping test
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureProtocolRendererSmokeTests.cs` — smoke test fixture

## Reused existing code (do NOT duplicate)

- `ManufactureOrderState` enum — `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrderState.cs`
- `ConditionsReadingSource` enum — `backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/ConditionsReadingSource.cs`
- `ManufactureOrderConditionsReading` entity — `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrderConditionsReading.cs`
- Eager-load already in place — `ManufactureOrderRepository.GetOrderByIdAsync` line 120
- Czech labels and column titles already defined in `frontend/src/components/manufacture/detail/ConditionsReadingsSection.tsx` — copy verbatim to the PDF for UI/PDF consistency
- QuestPDF table helpers — existing patterns in `ManufactureProtocolDocument.cs` "Výrobky" and "ABRA Flexi" sections

## Verification

End-to-end check before marking complete:

1. `dotnet build` in repo root — must succeed.
2. `dotnet format` — apply formatting.
3. Run unit tests for the touched files:
   ```
   dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Manufacture.GetManufactureProtocolHandlerTests|FullyQualifiedName~Manufacture.ManufactureProtocolRendererSmokeTests"
   ```
4. Run the full Manufacture test suite to catch regressions:
   ```
   dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Features.Manufacture"
   ```
5. Manual PDF check: start the backend, find a `Completed` order with conditions readings via the FE order list, open the protocol via the "Tisk protokolu" button on the order detail (`GET /api/manufactureorder/{id}/protocol.pdf`), and verify the new "Podmínky výroby" table appears between the basic-info row and the "Polotovar" section with the expected values, Czech stage labels, em-dashes for missing readings, and source annotation for non-`Live` rows.
6. No FE build needed — no FE code is touched and no OpenAPI surface changes.

## Out of scope

- Adding new manual temperature/humidity fields on `ManufactureOrder` (rejected during planning).
- Showing conditions readings anywhere besides the PDF (already shown in the FE "Podmínky výroby" tab).
- i18n of the new labels — the rest of the PDF uses hardcoded Czech strings; new labels match that convention.
- Changing the FE "Podmínky výroby" tab.