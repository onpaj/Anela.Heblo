# Design: Remove dead `GridLayoutPersistencePayload` type

## Component Design
No new or changed components. This change deletes one unreferenced internal
type — `GridLayoutPersistencePayload` (in
`backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs`)
— and corrects a stale XML doc comment on `SaveGridLayoutHandlerPayloadTests`
in `GridLayoutHandlerTests.cs` so it no longer names the deleted type. The
`GridLayouts` feature's active components (handlers, `GridLayoutStoredMapper`,
`StoredGridLayout`/`StoredColumnState`) are untouched and keep their existing
responsibilities and interfaces.

## Data Schemas
N/A, no schema changes. `GridLayoutPersistencePayload` was unused, `internal`,
and never part of the persisted JSON format, any MediatR contract, or the
OpenAPI surface; the active persistence shape (`StoredGridLayout` /
`StoredColumnState` via `GridLayoutStoredMapper`) is unaffected.
