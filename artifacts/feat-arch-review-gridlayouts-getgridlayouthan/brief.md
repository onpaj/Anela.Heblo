## Module
GridLayouts

## Finding
`GetGridLayoutHandler.Handle` deserializes `entity.LayoutJson` at line 39:

```csharp
// GetGridLayoutHandler.cs:39
var dto = JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson) ?? new GridLayoutDto();
```

If `entity.LayoutJson` contains malformed or empty JSON, `JsonSerializer.Deserialize` throws `JsonException`. The existing catch block directly below (lines 45–51) only filters `PostgresException or NpgsqlException` — a `JsonException` escapes unhandled and surfaces as an unhandled 500 to the caller, breaking any grid that uses this layout key.

## Why it matters
Under normal operation the stored JSON is always written by `SaveGridLayoutHandler`, so corruption is unlikely. However it can occur when:
- A DB migration script touches the `LayoutJson` column incorrectly.
- An old row predates a schema change to `GridLayoutDto`.
- Manual DB editing during debugging.

A deserialization failure in a user-preference store should be treated as "no saved layout exists" (graceful fallback to default column order), not a server error. The handler already returns `null` for missing rows and for DB errors — deserialise failures should follow the same path.

## Suggested fix
Wrap deserialization in a `JsonException` catch that logs a warning and falls back to `null`:

```csharp
GridLayoutDto? dto = null;
try
{
    dto = JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson);
}
catch (JsonException ex)
{
    _logger.LogWarning(ex,
        "Malformed LayoutJson for user={UserId} gridKey={GridKey}; returning null layout",
        userId, request.GridKey);
}

return new GetGridLayoutResponse { Layout = dto };
```

No change needed to `SaveGridLayoutHandler` or `ResetGridLayoutHandler` — they do not deserialize.

---
_Filed by daily arch-review routine on 2026-05-29._