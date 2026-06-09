# Specification: Remove Redundant GridKey from GridLayouts JSON Payload

## Summary
The `GridLayouts` module currently stores `GridKey` twice — once in the dedicated `GridLayouts.GridKey` database column and once inside the serialized `LayoutJson` blob — and the read path always overwrites the embedded value with the column value. This refactor removes the redundant `GridKey` from the JSON payload by serializing only the `Columns` portion of the layout, eliminating dead data and a latent inconsistency risk.

## Background
`SaveGridLayoutHandler` (`backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs:30-36`) currently serializes a full `GridLayoutDto` — including `GridKey` — and writes it to the `LayoutJson` column. The same `GridKey` is also passed to `UpsertAsync` and persisted into the dedicated `GridLayouts.GridKey` column.

On read, `GetGridLayoutHandler` (`backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs:56`) deserializes the JSON and then unconditionally overwrites `dto.GridKey = entity.GridKey`, discarding whatever was embedded. The JSON-embedded `gridKey` is never read by any consumer.

This violates KISS/YAGNI: each row carries unused bytes, the save handler implies `GridKey` is meaningful in the JSON schema when it is not, and any future migration that touches one location without the other could produce silent divergence. The fix is purely internal to the persistence boundary — the API-facing `GridLayoutDto` shape does not change.

## Functional Requirements

### FR-1: Persist only Columns in LayoutJson
`SaveGridLayoutHandler` must serialize only the `Columns` collection (the layout data) into the `LayoutJson` column. `GridKey` continues to be persisted exclusively in the dedicated `GridLayouts.GridKey` column via the existing `UpsertAsync` parameter.

**Acceptance criteria:**
- After saving a layout, the stored `LayoutJson` value contains only the `columns` field (no `gridKey` or other `GridLayoutDto` metadata).
- The `GridLayouts.GridKey` column continues to be populated with the value from `request.GridKey`.
- The JSON property name for columns matches whatever casing convention the existing serializer produces for the codebase (preserve current `JsonSerializerOptions` behavior — do not introduce custom naming).
- `GridLayoutDto` remains unchanged as the API-facing contract returned by `GetGridLayoutHandler`.

### FR-2: Read path deserializes the slim payload
`GetGridLayoutHandler` must deserialize the slim `LayoutJson` (columns-only) and populate the API-facing `GridLayoutDto` by combining the deserialized `Columns` with `entity.GridKey` from the column.

**Acceptance criteria:**
- Reading a layout written by the new save path returns a `GridLayoutDto` with `GridKey` populated from the column and `Columns` populated from the JSON.
- The explicit `dto.GridKey = entity.GridKey` assignment at `GetGridLayoutHandler.cs:56` is preserved (it now serves as the sole source of `GridKey` in the response, rather than as an overwrite).
- No new public DTOs are exposed; the persistence-only shape may be a private record, private nested type, or anonymous type local to the handlers.

### FR-3: Backward compatibility for existing rows
Rows written before this change contain the full `GridLayoutDto` JSON (including `gridKey`). The new read path must continue to deserialize these rows correctly.

**Acceptance criteria:**
- Existing rows in the `GridLayouts` table (written by the old handler) are read successfully after the change — the `Columns` field is recovered from the JSON, and the redundant embedded `gridKey` is ignored.
- This is achieved naturally because `System.Text.Json` ignores unknown properties by default; no migration of existing rows is required.
- A unit test exercises the read path against a JSON payload that contains both `gridKey` and `columns` (legacy format) and confirms the returned DTO is correct.

### FR-4: Shared persistence shape between save and read
The slim persistence format (columns-only) must be defined once and used by both `SaveGridLayoutHandler` and `GetGridLayoutHandler` to keep serialization and deserialization in lockstep.

**Acceptance criteria:**
- A single private type (record or class) representing the persisted shape is referenced by both handlers, or the handlers live close enough that drift is structurally prevented (e.g., a small internal helper in the feature folder).
- Adding a new field to the persisted layout in the future requires changing one type definition, not two.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change is expected at request latency. Persisted JSON size per row decreases by the length of the `"gridKey":"..."` fragment (typically tens of bytes). No new allocations are introduced beyond what the existing serializer already does.

### NFR-2: Security
No security surface changes. The feature does not modify authorization, input validation, or what data leaves the boundary — only the on-disk JSON shape inside a column already owned by the authenticated user.

### NFR-3: Compatibility
- The API contract (`GridLayoutDto`) and HTTP/MediatR request/response shapes are unchanged.
- Existing database rows remain readable without any data migration.
- No new database migration is required.

### NFR-4: Test coverage
- Add or update unit tests for `SaveGridLayoutHandler` to assert that the persisted `LayoutJson` does not contain `gridKey`.
- Add or update unit tests for `GetGridLayoutHandler` covering both the legacy JSON shape (with embedded `gridKey`) and the new slim shape.
- Existing tests for the GridLayouts use cases must continue to pass without modification of their public assertions (round-trip behavior is preserved).

## Data Model

No schema changes. The relevant entity remains:

- `GridLayouts` table
  - `UserId` (key part)
  - `GridKey` (key part, string) — authoritative source of the grid key
  - `LayoutJson` (string) — after this change, contains `{"columns":[...]}` only

Domain/API DTO (unchanged):
- `GridLayoutDto { GridKey: string, Columns: <existing columns shape> }`

New internal persistence shape (not exposed):
- A private record/type with a single `Columns` property, used only inside the GridLayouts feature folder for `JsonSerializer.Serialize`/`Deserialize` against `LayoutJson`.

## API / Interface Design

No external interface changes.

- `SaveGridLayoutCommand` request/response: unchanged.
- `GetGridLayoutQuery` request/response: unchanged.
- `GridLayoutDto` public shape: unchanged.

Internal changes only:
- `SaveGridLayoutHandler` swaps `JsonSerializer.Serialize(payload)` for serialization of a columns-only shape.
- `GetGridLayoutHandler` deserializes into the columns-only shape, then assembles the response `GridLayoutDto` from the deserialized columns plus `entity.GridKey`.

## Dependencies
- `System.Text.Json` (already in use).
- MediatR handlers and repository abstraction (`IGridLayoutRepository` or equivalent) — unchanged.
- No new packages, no new external services.

## Out of Scope
- Changing `GridLayoutDto` itself or any other API-facing contract.
- Migrating existing stored rows to the new format (backward-compatible read keeps this unnecessary).
- The separately tracked "dual-purpose DTO" issue mentioned in the brief — fixing the persistence redundancy here may make the future split cleaner, but the split itself is not part of this change.
- Versioning the persisted JSON format (no `version` field is added; if the shape ever evolves again, that decision will be made then).
- Changes to authorization, validation, or the upsert repository contract.

## Open Questions
None.

## Status: COMPLETE