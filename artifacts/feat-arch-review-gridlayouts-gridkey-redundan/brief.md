## Module
GridLayouts

## Finding
`SaveGridLayoutHandler` constructs a `GridLayoutDto` with `GridKey` set and serializes it as the stored JSON:

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs:30-36
var payload = new GridLayoutDto
{
    GridKey = request.GridKey,   // ← stored inside JSON
    Columns = request.Columns
};
var json = JsonSerializer.Serialize(payload);
await _repository.UpsertAsync(userId, request.GridKey, json, cancellationToken);
```

`GridKey` is already stored in the dedicated `GridLayouts.GridKey` database column. When the layout is read back, `GetGridLayoutHandler` immediately overwrites the deserialized value with the column value anyway:

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs:56
dto.GridKey = entity.GridKey;   // ← overwrites the embedded JSON value
```

The `gridKey` field inside the JSON blob is never actually used — it is dead data on every row.

## Why it matters
KISS/YAGNI: the embedded field adds bytes to every stored row, makes the save handler misleading (it looks like `GridKey` matters in the JSON schema), and creates a latent inconsistency risk if the two values ever diverge (e.g., a future migration that changes one but not the other).

## Suggested fix
Only serialize `Columns` — not the full `GridLayoutDto` — when writing to `LayoutJson`. Introduce a minimal private record or anonymous type for the stored format:

```csharp
var json = JsonSerializer.Serialize(new { columns = request.Columns });
```

Or deserialize with a type that only has `Columns`, keeping `GridLayoutDto` as the API-facing shape only (also fixes the dual-purpose issue tracked separately).

---
_Filed by daily arch-review routine on 2026-06-07._