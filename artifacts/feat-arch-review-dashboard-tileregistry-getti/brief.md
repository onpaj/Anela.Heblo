## Module
Dashboard

## Finding
`TileRegistry.GetTile()` (`backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileRegistry.cs`, lines 39–50) creates a `using` DI scope, resolves the tile, and returns the tile object:

```csharp
public ITile? GetTile(string tileId)
{
    ...
    using (var scope = _serviceProvider.CreateScope())
    {
        return (ITile)scope.ServiceProvider.GetRequiredService(tileType);
    }   // <-- scope disposed here; tile's scoped dependencies are now invalid
}
```

The caller — `DashboardService.GetTileDataAsync` (lines 140–169) — receives the tile object and then reads its properties (`tile.GetTileId()`, `tile.Title`, `tile.Description`, `tile.Size`, etc.) after the scope has been disposed. These properties are simple literal getters today, so nothing breaks at runtime now, but the returned object is formally "outside its DI lifetime."

Any future tile that reads from a scoped dependency (e.g. `DbContext`, `ICurrentUserService`) inside a property getter would silently access a disposed service — likely causing `ObjectDisposedException` at runtime with no obvious connection to the `GetTile` call.

`GetTileDataAsync` in the same registry (lines 52–64) correctly creates a fresh scope for the actual data load, so tile data is fine. The problem is confined to the metadata read via the leaked instance.

## Why it matters
- Violates the Dependency Inversion + SOLID principle: consumers should not receive objects whose lifetimes are already over.
- Creates a latent trap: the bug is invisible until a tile gains a scoped dependency, at which point it fails non-deterministically.
- Makes the code misleading: the `using` scope implies safety but provides none for the caller.

## Suggested fix
The simplest fix is to not return the tile instance from `GetTile`; return only the metadata values the caller actually needs:

```csharp
public TileMetadata? GetTileMetadata(string tileId)
{
    if (!_registeredTiles.TryGetValue(tileId, out var tileType)) return null;

    using var scope = _serviceProvider.CreateScope();
    var tile = (ITile)scope.ServiceProvider.GetRequiredService(tileType);
    return new TileMetadata(tile.GetTileId(), tile.Title, tile.Description, tile.Size, tile.Category, ...);
}
```

Alternatively, register all tiles as **singletons** (metadata is static) and keep scoped resolution only for `LoadDataAsync`. Then `GetTile()` can safely return the singleton without a scope wrapper.

---
_Filed by daily arch-review routine on 2026-05-28._