# Specification: Decouple GridLayouts Persistence Schema from Public API DTO

## Summary
The `GridLayouts` feature currently uses the public API contract `GridLayoutDto` as both the wire-format type and the persisted JSON schema for the `GridLayouts.LayoutJson` column. This couples the API contract to the storage format and means any evolution of the DTO silently changes the on-disk schema. Introduce a private, internal stored-layout type owned by the application layer and use it exclusively for serialization/deserialization of `LayoutJson`, mapping to and from `GridLayoutDto` / `GridColumnStateDto` at the handler boundary.

## Background
The GridLayouts module persists user-customized grid column state (order, width, hidden flag) as a JSON blob keyed by `(UserId, GridKey)`. Today, both handlers reach for `GridLayoutDto` directly:

- `SaveGridLayoutHandler.cs:30-36` constructs a `GridLayoutDto` and serializes it into `LayoutJson`.
- `GetGridLayoutHandler.cs:41` deserializes `LayoutJson` straight into `GridLayoutDto`, then overrides `GridKey` and `LastModified` from the entity.

This shape works only because the serializer tolerates unknown / missing fields. Real risks:

1. **Silent schema drift on contract changes.** A future change to `GridLayoutDto` (rename via `[JsonPropertyName]`, add field, remove field, split `GridColumnStateDto`) immediately changes the format written to new rows while old rows retain the prior format. Round-tripping of existing saved layouts may break invisibly.
2. **Layering violation.** The application layer's persistence format leaks through the public contract namespace `Contracts/`. The contract type can no longer evolve independently of stored data, undermining the goal of `Contracts/` as a pure API projection.
3. **Redundant data in the blob.** `GridKey` and `LastModified` are stored inside the JSON but are sourced from the entity on read (`GetGridLayoutHandler.cs:56-57`). The serialized `GridKey` is dead data, and the serialized `LastModified` is always `null` (it is never set on the payload at write time).

The fix is a small, low-risk refactor: introduce an internal stored-layout type, route serialization through it, and map at the handler boundary. Existing data must continue to deserialize correctly (back-compat read path).

## Functional Requirements

### FR-1: Introduce internal persistence types
Add new internal types in `Anela.Heblo.Application/Features/GridLayouts/Persistence/` (new folder), outside `Contracts/`:

```csharp
internal sealed record StoredGridLayout(List<StoredColumnState> Columns);
internal sealed record StoredColumnState(string Id, int Order, int? Width, bool Hidden);
```

Notes:
- Use `record` (immutable) — these are value-like persistence shapes and must not be mutated.
- `internal sealed` access — they MUST NOT be part of the public contract surface.
- Apply `[JsonPropertyName]` attributes matching the **current** stored JSON property names (lowercase: `columns`, `id`, `order`, `width`, `hidden`) so existing rows continue to deserialize unchanged.
- The stored shape MUST NOT include `gridKey` or `lastModified` — those are entity-level, not blob-level.

**Acceptance criteria:**
- New types live under `Anela.Heblo.Application/Features/GridLayouts/Persistence/` (or `Internal/`) and are `internal sealed`.
- `Contracts/GridLayoutDto.cs` and `Contracts/GridColumnStateDto.cs` are unchanged in name, namespace, and JSON shape.
- The new persistence types are not referenced anywhere outside the `GridLayouts` feature folder.

### FR-2: Route Save handler through stored type
Modify `SaveGridLayoutHandler.Handle`:

- Build a `StoredGridLayout` from `request.Columns`, mapping each `GridColumnStateDto` → `StoredColumnState`.
- Serialize the `StoredGridLayout` instance (not a `GridLayoutDto`) and pass the resulting JSON to `IGridLayoutRepository.UpsertAsync`.
- Stop constructing `GridLayoutDto` inside the handler.

**Acceptance criteria:**
- `JsonSerializer.Serialize` in `SaveGridLayoutHandler` is called with a `StoredGridLayout`.
- The resulting JSON contains exactly `{"columns":[ ... ]}` (no `gridKey`, no `lastModified` properties).
- Existing `SaveGridLayoutHandlerTests` pass with at most label-level adjustments; behavior of `UpsertAsync(userId, gridKey, json, ct)` is unchanged from the repository's perspective.

### FR-3: Route Get handler through stored type
Modify `GetGridLayoutHandler.Handle`:

- Deserialize `entity.LayoutJson` to `StoredGridLayout?`.
- Map the result to a `GridLayoutDto`, setting `Columns` from `Stored.Columns` (mapping `StoredColumnState` → `GridColumnStateDto`).
- Populate `GridKey` and `LastModified` from `entity` (as today).
- Preserve current error-handling behavior: `JsonException` → log warning and return `Layout = null`; `null` deserialize result → return `Layout = null`.

**Acceptance criteria:**
- Deserialization target is `StoredGridLayout`, not `GridLayoutDto`.
- The handler returns a `GridLayoutDto` whose `Columns` reflect the stored data and whose `GridKey` / `LastModified` come from the entity.
- All existing read-path tests pass without changing their assertions on the API contract shape.

### FR-4: Backward-compatible read of historical data
Existing rows currently contain payloads of the form `{"gridKey":"...","columns":[...],"lastModified":null}`. The new `StoredGridLayout` deserialization MUST read these rows successfully and produce the correct `Columns`. Extra properties (`gridKey`, `lastModified`) are ignored.

**Acceptance criteria:**
- A unit test deserializes the legacy JSON shape (with `gridKey` and `lastModified` present) and produces a `StoredGridLayout` whose `Columns` match.
- A unit test deserializes the new JSON shape (without `gridKey` / `lastModified`) and produces the equivalent result.
- No data migration is required; this is read-time tolerance only.

### FR-5: Boundary mapping
Mapping between `GridLayoutDto`/`GridColumnStateDto` and `StoredGridLayout`/`StoredColumnState` is performed inside the handlers (or a small `internal static` mapper class colocated with the stored types). No third-party mapping library is required.

**Acceptance criteria:**
- Mapping is straight property-to-property; no transformation logic beyond field copy.
- Mapping code is covered by tests at the handler level (existing handler tests are sufficient).
- No new dependencies (AutoMapper, Mapster, etc.) are introduced.

### FR-6: Reset handler review
Inspect `ResetGridLayoutHandler` for any direct dependency on `GridLayoutDto` for persistence. If it constructs or persists JSON via `GridLayoutDto`, apply the same decoupling. If it only deletes / clears rows (no JSON serialization), no change is required — document the inspection result.

**Acceptance criteria:**
- `ResetGridLayoutHandler` is reviewed; the spec author confirms whether changes were needed.
- If changes were applied, the same FR-1 / FR-2 principles hold (uses `StoredGridLayout`, not `GridLayoutDto`).

## Non-Functional Requirements

### NFR-1: Performance
No measurable change in serialization or DB performance is expected. The stored JSON should be smaller (no redundant `gridKey`/`lastModified` fields) but the size difference is negligible. No new allocations beyond the boundary mapping (a handful of records per request).

### NFR-2: Security
No change to the security posture:
- `[Authorize]` on `GridLayoutsController` continues to enforce auth.
- User scoping via `_currentUserService.GetCurrentUser()` is unchanged.
- No additional inputs are accepted from clients.
- The internal stored type does not loosen any validation that previously applied to `GridLayoutDto` (the contract DTO still validates request input at the API boundary).

### NFR-3: Compatibility
- **Wire / API compatibility**: `GridLayoutDto` and `GridColumnStateDto` MUST remain identical in name, namespace, and JSON property names. No OpenAPI / TypeScript client regeneration is required as part of this refactor.
- **Storage compatibility**: Existing `LayoutJson` rows MUST continue to read correctly (FR-4). New writes use the slimmer shape; both shapes coexist indefinitely.
- **Repository contract**: `IGridLayoutRepository.UpsertAsync(userId, gridKey, json, ct)` is unchanged.

### NFR-4: Testability
- Handler tests continue to assert via `GridLayoutDto` / `GridColumnStateDto` at the API boundary.
- At least one new unit test covers FR-4 (legacy-shape deserialization).
- At least one new unit test asserts the **shape** of the JSON written by `SaveGridLayoutHandler` (no `gridKey`, no `lastModified`).
- All existing `GetGridLayoutHandlerTests`, `SaveGridLayoutHandlerTests`, `ResetGridLayoutHandlerTests`, and `GridLayoutRepositoryTranslationTests` continue to pass.

### NFR-5: Code quality / layering
- The new types are `internal sealed record`, colocated with the feature, never exposed via `Contracts/`.
- No new public surface area is added.
- `Contracts/GridLayoutDto.cs` and `Contracts/GridColumnStateDto.cs` end the refactor with zero references from inside the feature's persistence path (serialization / deserialization). They are referenced only by request/response shapes and the controller.

## Data Model

### Stored (persistence-internal)
```
StoredGridLayout
  Columns: List<StoredColumnState>

StoredColumnState
  Id:     string
  Order:  int
  Width:  int?
  Hidden: bool
```
Serialized property names (lowercase to match existing rows): `columns`, `id`, `order`, `width`, `hidden`.

### API contract (unchanged)
```
GridLayoutDto                                GridColumnStateDto
  GridKey:      string                         Id:     string
  Columns:      List<GridColumnStateDto>       Order:  int
  LastModified: DateTime?                      Width:  int?
                                               Hidden: bool
```

### Persistence entity (unchanged)
```
GridLayout (Domain)
  Id:           int
  UserId:       string
  GridKey:      string
  LayoutJson:   string  // now: {"columns":[...]}
  LastModified: DateTime
```

## API / Interface Design
**No external API changes.** The MediatR requests (`GetGridLayoutRequest`, `SaveGridLayoutRequest`, `ResetGridLayoutRequest`) and the controller endpoints under `GridLayoutsController` retain their current signatures, JSON shapes, and status codes.

Internal interface changes:
- `SaveGridLayoutHandler` builds and serializes a `StoredGridLayout` instead of a `GridLayoutDto`.
- `GetGridLayoutHandler` deserializes `StoredGridLayout` and projects to `GridLayoutDto`.
- New internal mapping: `StoredColumnState ↔ GridColumnStateDto` (1:1 field mapping).

## Dependencies
- `System.Text.Json` (already in use).
- No new NuGet packages.
- No coordination with the frontend required — the wire contract does not change.
- No database migration required — `LayoutJson` column shape evolves at write time only, and old shape remains readable.

## Out of Scope
- Schema versioning of `LayoutJson` (e.g. embedding a `schemaVersion` field). Not warranted at current scale; revisit only if a non-back-compatible storage change is needed.
- Rewriting existing rows to the new shape. Old rows remain in legacy shape until naturally overwritten by a user save.
- Adding new fields to either the API contract or the stored layout.
- Changes to `GridLayoutRepository`, `GridLayoutConfiguration`, the EF model, or the database schema.
- Frontend changes, OpenAPI/TypeScript client regeneration.
- Authentication / authorization changes.
- Any other GridLayouts feature work (reset semantics, multi-user sharing, etc.).

## Open Questions
None.

## Status: COMPLETE