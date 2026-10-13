# Specification: Extract PackingMaterialDto mapping into a shared mapper

## Summary
`PackingMaterial` → `PackingMaterialDto` is hand-built with the same 8-field object initializer in four separate MediatR handlers (`CreatePackingMaterialHandler`, `UpdatePackingMaterialHandler`, `UpdatePackingMaterialQuantityHandler`, `GetPackingMaterialsListHandler`). This is real, verified duplication: every future field added to `PackingMaterialDto` must be added in all four places, and a missed site silently ships an inconsistent API response. The fix is to extract the mapping into a single internal static mapper, following the `JournalEntryMapper` pattern already established in this codebase, and have all four handlers call it.

## Background
The `PackingMaterials` module (`backend/src/Anela.Heblo.Application/Features/PackingMaterials/`) exposes CRUD + quantity-update use cases over `PackingMaterial` entities. Each use case handler independently constructs a `PackingMaterialDto` by copying `Id`, `Name`, `ConsumptionRate`, `ConsumptionType`, `ConsumptionTypeText` (derived via `PackingMaterialsTextHelper.ConsumptionTypeText`), `CurrentQuantity`, `ForecastedDays`, `CreatedAt`, `UpdatedAt` off the entity. Confirmed by reading all four handlers directly:

- `UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs:30-41` — `ForecastedDays` hardcoded to `null` (new material has no history)
- `UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs:37-48` — `ForecastedDays` hardcoded to `null` (this handler never computes a forecast)
- `UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs:49-60` — `ForecastedDays` set from a computed `displayForecast`
- `UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs:53-64` — `ForecastedDays` set from a computed `displayForecast`, inside a `.Select(...)` over the list

The other seven fields are byte-for-byte identical across all four sites. The project already has a precedent for this exact shape of fix: `Features/Journal/Mapping/JournalEntryMapper.cs` is an `internal static class` with a single public static `ToDto(entity)` method, living in a `Mapping/` subfolder of the feature. `development_guidelines.md` lists AutoMapper as "optional, for complex mappings" — this mapping is a flat 8-field copy with no nested collections, so a plain static mapper matches both the codebase convention and the guideline's own scoping (AutoMapper is for the complex cases; this isn't one).

`PackingMaterialsTextHelper` (used for `ConsumptionTypeText`) is already `internal static` in `Features/PackingMaterials/Contracts/`, in the same assembly (`Anela.Heblo.Application`) as all four handlers — no visibility change is needed for a mapper to call it.

## Functional Requirements

### FR-1: Single mapping method for PackingMaterial → PackingMaterialDto
Add one internal static mapper with a method that maps a `PackingMaterial` entity to a `PackingMaterialDto`, accepting the already-computed, nullable `forecastedDays` as a parameter (the mapper must not itself decide how forecast is computed — that varies per call site: hardcoded `null`, or computed from recent logs).

**Acceptance criteria:**
- One method exists that performs the `Id`, `Name`, `ConsumptionRate`, `ConsumptionType`, `ConsumptionTypeText`, `CurrentQuantity`, `CreatedAt`, `UpdatedAt` copy plus `ConsumptionTypeText` derivation, taking `PackingMaterial` and an optional/nullable `decimal? forecastedDays` and returning a fully populated `PackingMaterialDto`.
- The method lives in the `Anela.Heblo.Application.Features.PackingMaterials` namespace tree (mirroring `Journal.Mapping`), is `internal static`, and is discoverable next to the other mapping-adjacent contract helper (`PackingMaterialsTextHelper`).

### FR-2: All four handlers call the shared mapper
Replace each handler's inline `new PackingMaterialDto { ... }` block with a call to the new mapper method, passing the entity and that handler's forecast value (`null` for Create/Update, the computed `displayForecast` for UpdatePackingMaterialQuantity and GetPackingMaterialsList).

**Acceptance criteria:**
- `CreatePackingMaterialHandler`, `UpdatePackingMaterialHandler`, `UpdatePackingMaterialQuantityHandler`, `GetPackingMaterialsListHandler` no longer contain a `new PackingMaterialDto { ... }` object initializer — each calls the mapper instead.
- Each handler's existing forecast-computation logic (or lack thereof) is preserved exactly: Create and Update still pass `null`; UpdatePackingMaterialQuantity and GetPackingMaterialsList still pass their existing `displayForecast` (including the `decimal.MaxValue` → `null` guard and `Math.Round(..., 1)`, which are unaffected by this change since they run before the mapper call).
- `GetPackingMaterialsListHandler`'s `.Select(material => ...)` loop still builds one DTO per material via the shared mapper, preserving the existing `withForecast`/`withoutForecast`/`totalLogs` counters used in its `LogDebug` call (those counters are computed from `displayForecast`/`recentLogs`, not from the DTO itself, so they are unaffected by extraction).

### FR-3: No behavior change
This is a pure refactor. No API response field, value, or shape changes for any of the four endpoints.

**Acceptance criteria:**
- All four `PackingMaterialDto` fields produced for a given input are byte-identical before and after the change (verified by existing handler/controller tests continuing to pass unmodified where they assert DTO field values).
- No new field is added to `PackingMaterialDto` in this change (the brief's future-maintenance concern is about ease of the *next* field addition, not this one).

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact expected — this replaces four inline object-initializer blocks with four calls to an equivalent static method; no additional allocations, I/O, or async boundaries are introduced.

### NFR-2: Security
None — internal, in-process mapping of already-authorized data. No new surface area.

### NFR-3: Maintainability (the actual driver for this change)
A new field added to `PackingMaterialDto` in the future requires editing exactly one method, not four handlers.

## Data Model
No entity or DTO schema changes. `PackingMaterial` (domain entity, `Domain/Features/PackingMaterials/PackingMaterial.cs`) and `PackingMaterialDto` (`Application/Features/PackingMaterials/Contracts/PackingMaterialDto.cs`, 8 fields: `Id`, `Name`, `ConsumptionRate`, `ConsumptionType`, `ConsumptionTypeText`, `CurrentQuantity`, `ForecastedDays`, `CreatedAt`, `UpdatedAt`) are unchanged. Only the code path that populates the DTO from the entity is consolidated.

## API / Interface Design
No public API surface changes — same four MediatR requests/responses, same controller endpoints (`Anela.Heblo.API/Controllers/PackingMaterialsController.cs`), same JSON shape returned to the frontend (`frontend/src/api/hooks/usePackingMaterials.ts` and the generated OpenAPI client are unaffected since `PackingMaterialDto`'s public shape does not change).

New internal-only surface: one internal static mapper type/method in the `PackingMaterials` feature, following the `JournalEntryMapper` precedent (`internal static class ... { public static PackingMaterialDto ToDto(PackingMaterial material, decimal? forecastedDays) }`), placed under `Application/Features/PackingMaterials/Mapping/` to mirror `Features/Journal/Mapping/JournalEntryMapper.cs`.

## Dependencies
- Existing `PackingMaterialsTextHelper.ConsumptionTypeText(ConsumptionType)` — called from inside the new mapper exactly as each handler currently calls it inline.
- No new package or library dependency (AutoMapper is explicitly not warranted here per NFR-3 reasoning above — this is a flat, non-nested mapping, and the codebase's own convention for this exact shape (`JournalEntryMapper`) already avoids AutoMapper).

## Out of Scope
- Adding any new field to `PackingMaterialDto`.
- Touching `DeletePackingMaterialHandler` or `GetPackingMaterialLogsHandler` — neither constructs a `PackingMaterialDto` (Delete returns no material; GetPackingMaterialLogs returns `PackingMaterialLogDto`, a different type) and the brief does not list them.
- Introducing AutoMapper or any other mapping library.
- Changing how `forecastedDays` is computed in `UpdatePackingMaterialQuantityHandler` or `GetPackingMaterialsListHandler` (the `Math.Round`/`decimal.MaxValue` guard logic stays exactly where it is, upstream of the mapper call).
- Frontend changes — none are needed since the wire format is unchanged.

## Open Questions

None.

## Status: COMPLETE
