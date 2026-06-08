Committed successfully. Here is the output summary:

---

# Implementation: Remove Redundant GridKey from GridLayouts JSON Payload

## What was implemented

Removed the redundant `GridKey` and `LastModified` fields from the `LayoutJson` column in `GridLayouts`. A new internal `GridLayoutPersistencePayload` record (columns-only) is serialized on save and deserialized on read; `GridLayoutDto` is then assembled from `payload.Columns + entity.GridKey + entity.LastModified`. Legacy rows containing embedded `gridKey`/`lastModified` keys continue to deserialize correctly because `System.Text.Json` ignores unknown properties by default.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs` — new `internal sealed record` with a single `[JsonPropertyName("columns")]` property; shared by both handlers (FR-4)
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs` — now serializes `GridLayoutPersistencePayload(request.Columns)` instead of a full `GridLayoutDto`
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` — now deserializes `GridLayoutPersistencePayload`, then constructs `GridLayoutDto` from `payload.Columns ?? new()`, `entity.GridKey`, and `entity.LastModified`
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutHandlerTests.cs` — 30 new unit tests

## Tests

`GridLayoutHandlerTests.cs` covers:
- **SaveGridLayoutHandlerPayloadTests** (3 tests): JSON payload contains only `columns` (not `gridKey`/`lastModified`); all column properties round-trip correctly; empty column list serializes correctly
- **GetGridLayoutHandlerPayloadTests** (7 tests via two classes, 30 total): slim payload deserialization; legacy JSON with embedded `gridKey` ignored; `entity.GridKey`/`entity.LastModified` are authoritative; `null` columns guarded with empty list; empty `{}` object; malformed JSON returns null + logs warning; literal `"null"` JSON returns null without logging; entity not found; database exception handled

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayout"
# Expected: Passed 30, Failed 0
```

## Notes

- Arch-review Amendment 1 (also strip `LastModified` from JSON) was folded in — the slim record carries only `Columns`, matching the "no dead data" goal for both fields
- `[JsonPropertyName("columns")]` on the record property is load-bearing for backward compatibility: legacy rows store lowercase `"columns"`, and without it the default PascalCase serializer would emit `"Columns"` which would fail to round-trip
- The 38 pre-existing test failures are Docker-dependent integration tests unrelated to this change

## PR Summary

Removes dead data from the `GridLayouts.LayoutJson` column: `GridKey` and `LastModified` were being serialized into the JSON blob even though both are stored in dedicated columns and were unconditionally overwritten on read. The fix introduces a single `GridLayoutPersistencePayload` record (columns-only, `internal`, shared by both handlers) so save and read stay in lockstep. Legacy rows with extra JSON keys continue to work transparently via `System.Text.Json`'s ignore-unknown-properties default.

### Changes
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs` — new `internal sealed record` with `[JsonPropertyName("columns")]` property
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs` — serializes slim payload instead of full `GridLayoutDto`
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` — deserializes slim payload, assembles `GridLayoutDto` from columns + entity fields
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutHandlerTests.cs` — 30 new unit tests covering all paths including legacy format (FR-3) and null guards

## Status
DONE