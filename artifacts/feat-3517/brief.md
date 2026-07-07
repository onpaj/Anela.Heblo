# [arch-review] GridLayouts: GridLayoutPersistencePayload.cs is dead code

## Module
GridLayouts

## Finding
`backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs` defines `internal sealed record GridLayoutPersistencePayload` but this type is **never referenced in any production source file**.

```csharp
// GridLayoutPersistencePayload.cs (lines 1–7) — defined but unused
internal sealed record GridLayoutPersistencePayload(
    [property: JsonPropertyName("columns")] List<StoredColumnState> Columns);
```

The actual persistence serialization path uses `StoredGridLayout` / `StoredColumnState` via `GridLayoutStoredMapper` (`Infrastructure/GridLayoutStoredMapper.cs`). The only reference to `GridLayoutPersistencePayload` outside its own definition file is a comment in a test class description — it is not imported or instantiated anywhere.

## Why it matters
A dangling type with the same conceptual role as `StoredGridLayout` creates confusion: a reader must determine whether it is a future replacement, a legacy artifact, or an alternative code path. YAGNI — unused code has maintenance cost with zero benefit.

## Suggested fix
Delete `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs`. Update the comment in `GridLayoutHandlerTests.cs` (line 14–17) to remove the reference if needed.

---
_Filed by daily arch-review routine on 2026-07-06._
