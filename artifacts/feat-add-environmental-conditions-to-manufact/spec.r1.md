# Specification: Add Environmental Conditions to Manufacture Protocol PDF

## Summary

Add a "Podmínky výroby" (Manufacturing Conditions) section to the printed manufacture protocol PDF, rendering the per-stage environmental sensor readings already captured during the order workflow. Backend-only change touching three production files and two test files; no DB migration, DTO contract change, or frontend work.

## Background

The manufacture order workflow already auto-captures environmental conditions (inner/outer temperature and humidity) from a Home Assistant sensor adapter on two state transitions: `SemiProductManufactured` and `Completed`. Each `ManufactureOrderConditionsReading` stores four decimal values plus `Stage`, `RecordedAt`, and a `Source` quality flag (`Live | Partial | Unavailable`).

These readings are visible in the frontend under the "Podmínky výroby" tab, but the printed protocol PDF — the document operators print as evidence at the end of a batch — does not include them. The protocol is therefore incomplete as a paper record of the conditions during manufacture. Adding the section to the PDF closes that gap.

The data is already eager-loaded by `ManufactureOrderRepository.GetOrderByIdAsync` (line 120 includes `.Include(x => x.ConditionsReadings.OrderBy(r => r.RecordedAt))`), so only the protocol-data mapping and the QuestPDF document require changes.

## Functional Requirements

### FR-1: Extend the protocol data contract with conditions readings

Add a new nested type `ManufactureProtocolConditionsReading` and a collection property `ConditionsReadings` to `ManufactureProtocolData`. The new type carries `Stage`, four nullable decimals, `RecordedAt`, and `Source`. The collection lives alongside `Notes` and `ErpDocuments`.

**Acceptance criteria:**
- `ManufactureProtocolData.ConditionsReadings` exists as `List<ManufactureProtocolConditionsReading>` initialised to an empty list.
- `ManufactureProtocolConditionsReading` has properties: `ManufactureOrderState Stage`, `decimal? InnerTemperature`, `decimal? InnerHumidity`, `decimal? OuterTemperature`, `decimal? OuterHumidity`, `DateTime RecordedAt`, `ConditionsReadingSource Source`.
- The type reuses the existing domain enums `ManufactureOrderState` and `ConditionsReadingSource`; no new enums are introduced.
- The new property is internal to the protocol use case — it is not exposed via OpenAPI/MediatR responses to the frontend.

### FR-2: Map readings from the order aggregate into protocol data

In `GetManufactureProtocolHandler.Handle`, between the existing field assignments and `BuildErpDocumentsAsync`, project `order.ConditionsReadings` (ordered by `RecordedAt` ascending) into `ManufactureProtocolConditionsReading` instances.

**Acceptance criteria:**
- The handler maps every `ManufactureOrderConditionsReading` on the loaded order, preserving stage, all four sensor values (including nulls), `RecordedAt`, and `Source` exactly as stored.
- Order is ascending by `RecordedAt` (already guaranteed by the repository include, but the projection states it explicitly via `.OrderBy(r => r.RecordedAt)` for safety).
- When the order has no readings, `ConditionsReadings` is an empty list (never null).
- No additional repository call is introduced — eager loading at the repository layer is sufficient.

### FR-3: Render the "Podmínky výroby" section in the PDF

In `ManufactureProtocolDocument.cs`, insert a new section between the basic-info row (~line 58) and the "Polotovar" section (~line 62). The section renders only when `data.ConditionsReadings.Count > 0`.

The section presents a 6-column table mirroring the frontend `ConditionsReadingsSection.tsx`:

| Fáze | T vnitřní (°C) | RH vnitřní (%) | T venkovní (°C) | RH venkovní (%) | Zaznamenáno |

**Acceptance criteria:**
- A "Podmínky výroby" heading precedes the table, styled consistently with other section headings in the document.
- Numeric cells format with `ToString("0.0", CultureInfo.InvariantCulture)`; null values render as the em-dash (`—`).
- The stage cell maps the enum to Czech: `SemiProductManufactured` → "Polotovar", `Completed` → "Dokončeno", fallback to enum name for any other value.
- The `RecordedAt` cell formats as `dd.MM.yyyy HH:mm`, matching the date format used in the basic-info row (lines 39–58).
- When `Source != Live`, the stage cell is suffixed with a textual annotation: ` (HA nedostupný)` for `Unavailable`, ` (Částečné)` for `Partial`. No colored badges — text only.
- The table uses the same QuestPDF cell helpers/styles as the existing "Výrobky" table (lines 100–126) to stay visually consistent.
- The section is skipped entirely (no heading, no empty table) when there are no readings.
- The section renders without throwing for orders that have one or more readings, including readings with null sensor values.

### FR-4: Handler-level unit test for the new mapping

Extend `GetManufactureProtocolHandlerTests` to cover the new mapping path.

**Acceptance criteria:**
- The `BuildCompletedOrder` helper attaches two `ManufactureOrderConditionsReading` entries — one per stage — with representative values.
- A new test `Handle_CompletedOrder_MapsConditionsReadingsToProtocolData` captures the `ManufactureProtocolData` passed to the renderer mock and asserts `ConditionsReadings.Count == 2`, with both entries carrying the correct `Stage`, sensor values, `RecordedAt`, and `Source`.
- The new test reuses the existing `Mock<IManufactureProtocolRenderer>` capture pattern already in the file (no new mocking infrastructure).
- Pre-existing tests in the file continue to pass — the helper change must not break them.

### FR-5: Renderer smoke test covering the new section

Extend `ManufactureProtocolRendererSmokeTests` to exercise the new section.

**Acceptance criteria:**
- The "full data" smoke fixture populates `ConditionsReadings` with two entries: one fully populated with `Source = Live`, and one partially populated (some null sensor values) with `Source = Partial`.
- Rendering must complete without throwing.
- The PDF magic-byte assertion remains.
- An additional best-effort assertion attempts to confirm the section is not silently dropped (e.g., searching for `"Podmínky výroby"` in `Encoding.UTF8.GetString(bytes)`); if this proves unreliable due to PDF stream compression, the test falls back to the magic-byte assertion plus the implicit "render did not throw" guarantee. This decision is documented inline.

## Non-Functional Requirements

### NFR-1: Performance

- No additional database round-trips. The repository already eager-loads `ConditionsReadings`.
- PDF generation time must not degrade noticeably; the new section is a single small table with at most a handful of rows per order.

### NFR-2: Security & data sensitivity

- No new endpoints, no new auth surface. The protocol endpoint (`GET /api/manufactureorder/{id}/protocol.pdf`) is already protected by existing authorization.
- The new data (temperature/humidity readings) is non-sensitive operational telemetry; no PII implications.

### NFR-3: Compatibility & non-regression

- No DTO contract changes visible to the frontend.
- No OpenAPI regeneration required.
- No database migration.
- No FE build/test required.
- Orders without conditions readings continue to produce the same PDF as before (section omitted entirely).

### NFR-4: Code quality & consistency

- Match existing QuestPDF section conventions in `ManufactureProtocolDocument.cs` (cell helpers, header styling, spacing).
- Match existing Czech-string convention — no i18n introduced.
- Code passes `dotnet build` and `dotnet format`.

## Data Model

No schema changes. The feature consumes the existing domain entity `ManufactureOrderConditionsReading` (in `Anela.Heblo.Domain/Features/Manufacture/ManufactureOrderConditionsReading.cs`) via the `ManufactureOrder.ConditionsReadings` navigation property.

New in-process types (Application layer only, not persisted, not exposed to FE):

- `ManufactureProtocolConditionsReading` — DTO carrying one reading into the PDF document.
- `ManufactureProtocolData.ConditionsReadings : List<ManufactureProtocolConditionsReading>` — collection passed to the renderer.

Reused enums:
- `Anela.Heblo.Domain.Features.Manufacture.ManufactureOrderState`
- `Anela.Heblo.Domain.Features.Manufacture.Conditions.ConditionsReadingSource`

## API / Interface Design

No public API changes. Internal call chain:

```
GET /api/manufactureorder/{id}/protocol.pdf
  → GetManufactureProtocolHandler.Handle
      → ManufactureOrderRepository.GetOrderByIdAsync (already includes ConditionsReadings)
      → maps ConditionsReadings into ManufactureProtocolData
      → IManufactureProtocolRenderer.Render(data)
          → ManufactureProtocolDocument (QuestPDF) emits the new section
```

PDF layout change (single insertion point):

```
[Header]
[Basic info row]                                    ← existing
[Podmínky výroby section]                           ← NEW (conditional on Count > 0)
[Polotovar section]                                 ← existing
[Výrobky section]                                   ← existing
[ABRA Flexi section]                                ← existing
...
```

## Dependencies

- **QuestPDF** — already in use by `ManufactureProtocolDocument`.
- **Existing eager-load** in `ManufactureOrderRepository.GetOrderByIdAsync` (line 120).
- **Existing renderer mock pattern** in `GetManufactureProtocolHandlerTests`.

No new packages, no new external services.

## Verification

1. `dotnet build` from repo root — succeeds.
2. `dotnet format` — applied.
3. Targeted unit tests pass:
   ```
   dotnet test backend/test/Anela.Heblo.Tests \
     --filter "FullyQualifiedName~Manufacture.GetManufactureProtocolHandlerTests|FullyQualifiedName~Manufacture.ManufactureProtocolRendererSmokeTests"
   ```
4. Full Manufacture test suite passes (regression guard):
   ```
   dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Features.Manufacture"
   ```
5. Manual PDF inspection: start the backend, open a `Completed` order with readings, click "Tisk protokolu" on the order detail, and confirm the "Podmínky výroby" table renders between the basic-info row and "Polotovar" with the correct Czech labels, one-decimal numeric formatting, em-dashes for nulls, and source annotations for non-`Live` rows.
6. No FE build required.

## Out of Scope

- New manual temperature/humidity input fields on `ManufactureOrder` (explicitly rejected during planning).
- Surfacing conditions readings anywhere outside the PDF (already in the FE "Podmínky výroby" tab).
- Internationalisation of the new labels — the rest of the PDF is hardcoded Czech and the new labels follow that convention.
- Any change to the frontend "Podmínky výroby" tab.
- Colored source badges in the PDF — replaced with textual suffix to keep QuestPDF complexity low.
- Repository or domain layer changes.
- OpenAPI client regeneration.
- Database migrations.

## Open Questions

None.

## Status: COMPLETE