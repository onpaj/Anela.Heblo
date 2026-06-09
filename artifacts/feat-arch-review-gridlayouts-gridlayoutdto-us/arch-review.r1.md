# Architecture Review: Decouple GridLayouts Persistence Schema from Public API DTO

## Skip Design: true

Backend-only refactor — no UI/UX components, no visual changes, wire contract unchanged.

## Architectural Fit Assessment

The proposed change directly aligns with the project's documented architecture:

- **`Contracts/` ownership rule** (`docs/architecture/development_guidelines.md`): The folder is intended to hold **API DTOs only**. Reusing `GridLayoutDto` as a persistence schema violates this — the spec restores the intended separation.
- **Vertical Slice ownership**: The internal stored shape belongs entirely inside the `GridLayouts` feature folder, alongside its handlers. No types cross feature boundaries; nothing new is exposed from `Contracts/`.
- **DTOs are classes / internal stored types are `record`** (CLAUDE.md): The public `GridLayoutDto` already follows the "class" rule (NSwag generator constraint). The new persistence-internal types correctly use `record` per the coding-style rule — they are pure value-like persistence shapes never crossing the OpenAPI boundary.
- **Repository contract stable**: `IGridLayoutRepository.UpsertAsync(userId, gridKey, json, ct)` already takes a `string` JSON blob; the handler owns serialization. The refactor reshapes what gets serialized, not who serializes.
- **Existing single-DbContext / repository-in-Persistence layering** is unaffected.

Integration points are minimal and local: only `SaveGridLayoutHandler.cs` and `GetGridLayoutHandler.cs` change behavior. `ResetGridLayoutHandler` performs no JSON I/O (verified — it calls `_repository.DeleteAsync` only), so FR-6 collapses to "no change required, document it."

## Proposed Architecture

### Component Overview

```
┌──────────────────── Anela.Heblo.Application ────────────────────┐
│  Features/GridLayouts/                                          │
│  ├── Contracts/                  (API surface — UNCHANGED)      │
│  │   ├── GridLayoutDto           (class, public, wire shape)    │
│  │   └── GridColumnStateDto      (class, public, wire shape)    │
│  │                                                              │
│  ├── Infrastructure/             (NEW — persistence-internal)   │
│  │   ├── StoredGridLayout        (internal sealed record)       │
│  │   ├── StoredColumnState       (internal sealed record)       │
│  │   └── GridLayoutStoredMapper  (internal static)              │
│  │                                                              │
│  └── UseCases/                                                  │
│      ├── SaveGridLayout/Handler  ──┐                            │
│      ├── GetGridLayout/Handler    ─┼─► uses StoredGridLayout    │
│      └── ResetGridLayout/Handler   │    for (de)serialization,  │
│                                    │    maps at boundary        │
└────────────────────────────────────┼────────────────────────────┘
                                     │
                  IGridLayoutRepository (UNCHANGED — takes string json)
                                     │
                  ApplicationDbContext.GridLayouts.LayoutJson (text column)
```

Boundary direction: external `GridLayoutDto` flows **in** at `SaveGridLayoutRequest`; `StoredGridLayout` is constructed inside the handler and never leaves the feature folder. On read, `StoredGridLayout` is deserialized, mapped to `GridLayoutDto`, and `GridLayoutDto` is the only thing the controller/response sees.

### Key Design Decisions

#### Decision 1: Folder name — `Infrastructure/` vs `Persistence/` vs `Internal/`

**Options considered:**
- `Application/Features/GridLayouts/Persistence/` — spec's preferred name.
- `Application/Features/GridLayouts/Infrastructure/` — matches the documented vertical-slice template in `docs/architecture/filesystem.md` ("Features/{Feature}/Infrastructure/: Feature-specific infrastructure").
- `Application/Features/GridLayouts/Internal/` — unambiguous but introduces a new convention.

**Chosen approach:** `Infrastructure/`.

**Rationale:** The filesystem documentation already names this slot for feature-internal infrastructure. Naming it `Persistence/` inside the Application project is misleading because `Anela.Heblo.Persistence` is a separate project for the EF/DbContext layer — same name would invite confusion about which layer owns these types. Reusing the documented `Infrastructure/` slot keeps the slice consistent with the rest of the codebase. Marking the types `internal sealed` is what actually enforces the invisibility — the folder name is documentation.

#### Decision 2: Where mapping lives — inline in handlers vs. dedicated static mapper

**Options considered:**
- Inline mapping inside both handlers (spec FR-5 lists this as acceptable).
- A small `internal static class GridLayoutStoredMapper` colocated with the stored types.
- AutoMapper profile (already a project dependency).

**Chosen approach:** Dedicated `internal static class GridLayoutStoredMapper` colocated with the stored types in `Infrastructure/`.

**Rationale:** Mapping is invoked from two handlers (Save + Get); inlining duplicates the projection in two places. A static mapper is ~20 lines, has zero runtime overhead, is trivially testable in isolation, and keeps handler bodies focused on orchestration/error handling. AutoMapper is rejected because (a) the mapping is 1:1 field copy with no transformation, (b) AutoMapper would obscure rather than clarify intent for such a small mapping, and (c) the spec explicitly says no new dependencies. This is **not** a premature abstraction — it has two real callers today.

#### Decision 3: How `StoredGridLayout` serializes JSON

**Options considered:**
- Explicit `[JsonPropertyName("…")]` attributes on every property (matches current row format exactly).
- Rely on a shared `JsonSerializerOptions { PropertyNamingPolicy = CamelCase }`.
- Default Pascal-case serialization (would break legacy data immediately).

**Chosen approach:** Explicit `[JsonPropertyName]` attributes, no `JsonSerializerOptions` argument passed to `Serialize`/`Deserialize`.

**Rationale:** The current stored shape uses lowercase property names (`columns`, `id`, `order`, `width`, `hidden`). Existing rows must continue to deserialize (FR-4). Per-property attributes make the wire format self-documenting at the type definition and immune to ambient `JsonSerializerOptions` changes elsewhere in the codebase. Passing no options matches the existing handler behavior (`JsonSerializer.Serialize(payload)` is called without options today), so behavior is preserved.

#### Decision 4: Handling `null` and missing `columns` on read

**Options considered:**
- Treat any deserialized `StoredGridLayout?` as the source of truth (current behavior — null → `Layout = null`).
- Treat `columns == null` as malformed.
- Default missing `columns` to an empty list.

**Chosen approach:** Initialize `StoredGridLayout.Columns` to an empty list as a safety default; treat a top-level `null` deserialization the same as today (return `Layout = null`).

**Rationale:** Avoids a `NullReferenceException` when an existing row contains a JSON object without a `columns` field. Empty list is the correct interpretation: "no columns saved" = "use the grid's default layout" which is already the read-time semantic when no row exists. A top-level `null` payload preserves the current "no layout to return" branch, matching `Handle_WhenLayoutJsonIsLiteralNull_ReturnsNullLayoutAndDoesNotLog`.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/GridLayouts/
├── Contracts/                                  (unchanged)
│   ├── GridLayoutDto.cs
│   └── GridColumnStateDto.cs
├── Infrastructure/                             (NEW)
│   ├── StoredGridLayout.cs                     internal sealed record
│   ├── StoredColumnState.cs                    internal sealed record
│   └── GridLayoutStoredMapper.cs               internal static class
├── UseCases/
│   ├── SaveGridLayout/SaveGridLayoutHandler.cs    (modify)
│   ├── GetGridLayout/GetGridLayoutHandler.cs      (modify)
│   └── ResetGridLayout/ResetGridLayoutHandler.cs  (no change — documented)
└── GridLayoutsModule.cs                        (no change)
```

`GridLayoutsModule.cs` requires no edit — the new types are not DI-resolved, they are constructed directly by handlers.

### Interfaces and Contracts

**New persistence-internal types** (place each in its own file, namespace `Anela.Heblo.Application.Features.GridLayouts.Infrastructure`):

```csharp
internal sealed record StoredGridLayout(
    [property: JsonPropertyName("columns")] List<StoredColumnState> Columns);

internal sealed record StoredColumnState(
    [property: JsonPropertyName("id")]     string Id,
    [property: JsonPropertyName("order")]  int    Order,
    [property: JsonPropertyName("width")]  int?   Width,
    [property: JsonPropertyName("hidden")] bool   Hidden);
```

**Internal mapper** (`GridLayoutStoredMapper.cs`):

```csharp
internal static class GridLayoutStoredMapper
{
    public static StoredGridLayout ToStored(IEnumerable<GridColumnStateDto> columns) =>
        new(columns.Select(c => new StoredColumnState(c.Id, c.Order, c.Width, c.Hidden)).ToList());

    public static List<GridColumnStateDto> ToDtoColumns(StoredGridLayout stored) =>
        stored.Columns.Select(c => new GridColumnStateDto
        {
            Id = c.Id, Order = c.Order, Width = c.Width, Hidden = c.Hidden
        }).ToList();
}
```

**Unchanged contracts:** `GridLayoutDto`, `GridColumnStateDto`, `IGridLayoutRepository`, `SaveGridLayoutRequest`, `GetGridLayoutResponse`, `ResetGridLayoutRequest`.

### Data Flow

**Save path:**
```
Controller → SaveGridLayoutRequest { GridKey, List<GridColumnStateDto> Columns }
   → SaveGridLayoutHandler
       → GridLayoutStoredMapper.ToStored(request.Columns)  →  StoredGridLayout
       → JsonSerializer.Serialize(stored)                  →  "{\"columns\":[…]}"
       → IGridLayoutRepository.UpsertAsync(userId, gridKey, json, ct)
           → ApplicationDbContext.GridLayouts.LayoutJson
```

**Read path:**
```
Controller → GetGridLayoutRequest { GridKey }
   → GetGridLayoutHandler
       → IGridLayoutRepository.GetAsync(userId, gridKey, ct)  →  GridLayout entity
       → JsonSerializer.Deserialize<StoredGridLayout>(entity.LayoutJson)
            ├─ JsonException        → log warning, return Layout = null
            ├─ null result          → return Layout = null
            └─ StoredGridLayout     → build GridLayoutDto {
                                          GridKey      = entity.GridKey,
                                          Columns      = Mapper.ToDtoColumns(stored),
                                          LastModified = entity.LastModified
                                      }
       → GetGridLayoutResponse { Layout = dto }
```

**Reset path:** unchanged — no JSON crosses the handler.

### Test Plan

| Test | Location | Purpose |
|------|----------|---------|
| Existing `SaveGridLayoutHandlerTests.Handle_CallsUpsertWithSerializedColumns` | `test/Features/GridLayouts/` | Already asserts the serialized JSON has a `columns` key — continues to pass. |
| **NEW** `SaveGridLayoutHandlerTests.Handle_SerializedJson_DoesNotContainGridKeyOrLastModified` | same file | FR-2 acceptance: shape contains no `gridKey` / `lastModified` properties. |
| Existing `GetGridLayoutHandlerTests.Handle_WhenSavedLayoutExists_ReturnsDeserializedDto` | same folder | Already serializes via anonymous object `{ columns = [...] }` (new shape) — already passes. |
| **NEW** `GetGridLayoutHandlerTests.Handle_LegacyJsonShape_DeserializesColumnsCorrectly` | same folder | FR-4: feed `{"gridKey":"…","columns":[…],"lastModified":null}` and assert `Layout.Columns` populated. |
| All existing `GetGridLayoutHandlerTests` malformed/null/empty branches | same folder | Error paths unchanged. |
| Existing `ResetGridLayoutHandlerTests` | same folder | No change. |
| Existing `GridLayoutRepositoryTranslationTests` | `test/Persistence/GridLayouts/` | Repository contract unchanged — no impact. |

A direct mapper test is unnecessary — handler-level tests fully exercise the mapping behavior, and adding a unit test for a 1:1 field copy is the kind of low-value test the project doesn't need.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Property casing mismatch on `StoredGridLayout` → existing rows deserialize as empty | **HIGH** | Use explicit `[JsonPropertyName]` per Decision 3; lock in via the FR-4 legacy-shape test. |
| Future developer treats `StoredGridLayout` as a public type and exposes it via `Contracts/` | MEDIUM | `internal sealed` modifier on both types prevents accidental export. Place in `Infrastructure/` folder which signals "feature-internal." |
| Subsequent change to `GridLayoutDto` (e.g. adding a field) wrongly expects automatic persistence | MEDIUM | Decoupling is the whole point. Mitigate via PR-time code review and an explanatory file-level comment on `StoredGridLayout` (one line) calling out: "Persistence-internal shape for `GridLayouts.LayoutJson`; do NOT add API-only fields here." |
| `dotnet format` analyzer surfaces an `IDE0005`/unused-using warning on `Anela.Heblo.Application.Features.GridLayouts.Contracts` in `SaveGridLayoutHandler.cs` after the handler stops referencing `GridLayoutDto` directly | LOW | The handler still references `GridColumnStateDto` via `SaveGridLayoutRequest.Columns`, so the using stays valid. Run `dotnet format` after the edit. |
| Hidden coupling: existing test `Handle_WhenSavedLayoutExists_ReturnsDeserializedDto` already uses the new lowercase JSON shape, so it would silently pass even if the refactor were wrong | LOW | The explicit new FR-4 test (with legacy shape including `gridKey` + `lastModified` siblings) closes this gap. |
| Reset handler review skipped without documentation | LOW | The review here records the result: **`ResetGridLayoutHandler` performs no JSON I/O** (verified at `ResetGridLayoutHandler.cs:30` — calls `DeleteAsync` only). No change is required. |

## Specification Amendments

1. **Folder location: `Infrastructure/` rather than `Persistence/`.** The spec lists `Persistence/` or `Internal/`. Use `Infrastructure/` to match the documented vertical-slice template in `docs/architecture/filesystem.md` and avoid a name collision with the `Anela.Heblo.Persistence` project. (Decision 1.)
2. **Mapping: extract to `internal static class GridLayoutStoredMapper`** colocated with the stored types, rather than inlining. The spec already permits this; making it the default keeps mapping DRY between Save and Get. (Decision 2.)
3. **Use `[property: JsonPropertyName("…")]` on `record` positional parameters** explicitly — do not rely on ambient `JsonSerializerOptions`. The handlers must call `JsonSerializer.Serialize`/`Deserialize` **without** an options argument to preserve current handler behavior. (Decision 3.)
4. **Default `StoredGridLayout.Columns` to a non-null empty list at construction** (since `record` positional parameters require a value, supply `new List<StoredColumnState>()` when absent) so a malformed-but-deserializable payload missing `columns` cannot NRE downstream. (Decision 4.)
5. **`ResetGridLayoutHandler` review result: no change required** — confirmed it calls `_repository.DeleteAsync` only, with no `JsonSerializer` use. FR-6 is satisfied by documenting this here.
6. **Skip the redundant standalone mapper unit test** referenced in NFR-4. Handler-level tests already exercise mapping end-to-end; a dedicated test for 1:1 field copy is low-value churn.

## Prerequisites

None.

- No NuGet dependencies added.
- No EF migration (column type and shape unchanged).
- No frontend / OpenAPI / TypeScript client regeneration (API contract unchanged).
- No DI registration changes (`GridLayoutsModule.cs` untouched).
- No infrastructure or Azure Key Vault changes.

Implementation can begin immediately against the current `main`/feature branch.