## Module
GridLayouts

## Finding
`GridLayoutDto` (a public API contract DTO in `Contracts/`) is used as the schema for the JSON blob stored in the `GridLayouts.LayoutJson` database column:

- **Write path** (`SaveGridLayoutHandler.cs:30-36`): serializes `GridLayoutDto` to produce `LayoutJson`
- **Read path** (`GetGridLayoutHandler.cs:41`): deserializes `LayoutJson` directly into `GridLayoutDto`

```csharp
// SaveGridLayoutHandler.cs:30-36
var payload = new GridLayoutDto { GridKey = request.GridKey, Columns = request.Columns };
var json = JsonSerializer.Serialize(payload);

// GetGridLayoutHandler.cs:41
dto = JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson);
```

This means the public API contract and the persistence storage schema are the same type. Any change to `GridLayoutDto` (adding a field, renaming a `[JsonPropertyName]`, removing a property) silently changes the format of data written to `LayoutJson` for new rows while old rows still have the old format — creating a schema drift that `JsonSerializer` handles silently (unknown fields ignored, missing fields default).

## Why it matters
The persistence format is an internal concern; the API shape is an external contract. Coupling them violates the principle that the application layer owns its serialization format independently of the API projection. A future refactor of the API DTO (e.g. splitting `GridColumnStateDto` or renaming `id` → `columnId`) would unknowingly change the stored data format and break round-tripping for existing user layouts.

## Suggested fix
Introduce a private internal record for the stored JSON shape, separate from `GridLayoutDto`:

```csharp
// Internal to the Application/Features/GridLayouts layer — not in Contracts/
private record StoredGridLayout(List<StoredColumnState> Columns);
private record StoredColumnState(string Id, int Order, int? Width, bool Hidden);
```

Use this for serialization/deserialization in the handlers. Map to/from `GridColumnStateDto` at the handler boundary. `GridLayoutDto` then remains a pure API projection type free to evolve independently.

---
_Filed by daily arch-review routine on 2026-06-07._