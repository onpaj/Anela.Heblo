# Specification: Fix DI Lifetime Leak in `TileRegistry.GetTile()` and `GetAvailableTiles()`

## Summary
`TileRegistry.GetTile()` and `TileRegistry.GetAvailableTiles()` resolve tile instances inside a `using` DI scope and then return those instances after the scope is disposed, formally placing them outside their DI lifetime. Today this is a latent bug because all `ITile` property getters return literals, but any future tile that reads from a scoped dependency in a property getter would throw `ObjectDisposedException`. This spec replaces the leaked-instance pattern with a metadata-only contract (`TileMetadata`) so consumers never receive objects whose lifetimes have ended.

## Background
The `TileRegistry` (`backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileRegistry.cs`) is registered as a singleton (`XccModule.cs:22`) while individual tiles are registered as scoped (`TileRegistryExtensions.cs:21`). Three of its methods touch scoped tile instances:

- `GetTile(string tileId)` (lines 39–50) — creates a scope, resolves the tile, disposes the scope, then returns the tile. The returned object outlives its DI scope.
- `GetAvailableTiles()` (lines 23–37) — same anti-pattern, applied to every registered tile.
- `GetTileDataAsync(...)` (lines 52–64) — correct: scope wraps the entire `LoadDataAsync` call.

Current consumers (`DashboardService`, `GetAvailableTilesHandler`) only read metadata properties from the returned tiles (`Title`, `Description`, `Size`, `Category`, `DefaultEnabled`, `AutoShow`, `ComponentType`, `RequiredPermissions`, `GetTileId()`). Because today's implementations of `ITile` (e.g. `BackgroundTaskStatusTile`) hard-code these as literal getters, the disposed-scope access is harmless. The risk is structural, not observed: the `using` block signals "the framework manages this for you" while leaking a service whose scoped dependencies are dead. Any future tile that, for example, computes `Title` or `RequiredPermissions` from `ICurrentUserService` or a `DbContext` would fail non-deterministically with errors that do not point back to `TileRegistry`.

The fix tightens the contract so the registry never hands out a live tile instance for metadata purposes — only a value object containing the snapshot data the consumers actually use.

## Functional Requirements

### FR-1: Introduce `TileMetadata` value type
Define a new immutable record class `TileMetadata` in `backend/src/Anela.Heblo.Xcc/Services/Dashboard/` that captures the static descriptive data a tile exposes today. It must contain:

- `string TileId`
- `string Title`
- `string Description`
- `TileSize Size`
- `TileCategory Category`
- `bool DefaultEnabled`
- `bool AutoShow`
- `string[] RequiredPermissions`

`ComponentType` (`Type`) is currently exposed by `ITile` but is read by `DashboardService.GetTileDataAsync` into `TileData.ComponentType`. It must also be included in `TileMetadata` to preserve current API output.

`TileMetadata` should be a regular class (per project rule on DTOs/records) only if it is serialized through the OpenAPI boundary. Since `TileMetadata` is an internal registry-layer type (not a DTO returned over HTTP), it MAY be a C# `record class` with positional or init-only properties — choose immutability and value semantics.

**Acceptance criteria:**
- `TileMetadata` exists with all listed properties.
- All fields are read-only after construction.
- It contains no `ITile` reference and no `IServiceProvider` reference.

### FR-2: Replace `GetTile()` with `GetTileMetadata()`
Modify `ITileRegistry`:

- Remove `ITile? GetTile(string tileId)`.
- Add `TileMetadata? GetTileMetadata(string tileId)` that returns `null` when the id is not registered; otherwise creates a DI scope, resolves the tile, snapshots its metadata into a `TileMetadata`, lets the scope dispose, and returns the snapshot.

The new method must not return an `ITile`, an `IServiceProvider`, an `IServiceScope`, or any object that holds a reference to the tile instance.

**Acceptance criteria:**
- `ITileRegistry.GetTile` no longer exists; `GetTileMetadata` exists with the signature above.
- Returns `null` for unknown tile id.
- Returns a populated `TileMetadata` for known tile id; all fields match the tile's property values at resolution time.
- The DI scope is disposed before the method returns (verifiable by code inspection — `using var scope` block exits before `return`).
- No reference to the scoped tile instance escapes the method.

### FR-3: Replace `GetAvailableTiles()` with metadata enumeration
Modify `ITileRegistry`:

- Change `IEnumerable<ITile> GetAvailableTiles()` to `IEnumerable<TileMetadata> GetAvailableTiles()`.
- Implementation must use a single DI scope that wraps the entire enumeration (resolve all tiles, snapshot each, then dispose), returning the materialized list of `TileMetadata`.

The return must be a fully materialized collection (`List<TileMetadata>` or equivalent), not a deferred enumerable that captures the disposed scope.

**Acceptance criteria:**
- Method returns `IEnumerable<TileMetadata>` containing one entry per registered tile.
- All metadata values match what each `ITile` instance exposes at resolution time.
- No `ITile` reference escapes the method.
- Enumeration is safe after the registry call returns (no lazy resolution against disposed scope).

### FR-4: Update `DashboardService` to consume metadata
Update `DashboardService.GetTileDataAsync` (`DashboardService.cs:121–187`):

- Replace `var tile = _tileRegistry.GetTile(tileSettings.TileId);` with `var metadata = _tileRegistry.GetTileMetadata(tileSettings.TileId);`.
- The "tile not found" branch (`tile == null`) checks `metadata == null` instead and produces the same error `TileData`.
- The success branch populates `TileData` from `metadata` fields. `TileData.TileId` is sourced from `metadata.TileId` (not `tile.GetTileId()`).
- `TileData.Data` is still populated from `await _tileRegistry.GetTileDataAsync(tileSettings.TileId, tileParameters)` — that call already manages its own scope correctly and is unchanged.

Update `DashboardService.GetUserSettingsAsync` (`DashboardService.cs:29–102`):

- The local variable `availableTiles` becomes `IEnumerable<TileMetadata>`.
- `autoShowTiles` filtering uses `t.DefaultEnabled` and `t.AutoShow` on `TileMetadata`.
- `tile.GetTileId()` calls are replaced with `metadata.TileId`.

**Acceptance criteria:**
- `DashboardService` compiles without referencing `ITile` directly.
- All existing `DashboardServiceTests` continue to pass after mocks are updated per FR-6.
- Tile output `TileData` payloads remain byte-equivalent (same property values) for the existing `BackgroundTaskStatusTile`.

### FR-5: Update `GetAvailableTilesHandler` to consume metadata
Update `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetAvailableTiles/GetAvailableTilesHandler.cs`:

- `var tiles = _tileRegistry.GetAvailableTiles();` now yields `IEnumerable<TileMetadata>`.
- The projection to `DashboardTileDto` reads from `TileMetadata` fields directly (`t.TileId`, `t.Title`, `t.Description`, `t.Size.ToString()`, `t.Category.ToString()`, `t.DefaultEnabled`, `t.AutoShow`, `t.RequiredPermissions`).
- The resulting `DashboardTileDto` shape is unchanged — same field names, same string formatting.

**Acceptance criteria:**
- Handler compiles and returns the same `GetAvailableTilesResponse` shape.
- HTTP response of `GET /api/dashboard/available-tiles` is byte-equivalent to pre-change output (verifiable via existing `DashboardControllerTests` and `GetAvailableTilesHandlerTests`).

### FR-6: Update affected tests
The following test files must be updated to match the new `ITileRegistry` surface:

- `backend/test/Anela.Heblo.Tests/Features/Dashboard/DashboardServiceTests.cs` — every `_tileRegistryMock.Setup(x => x.GetAvailableTiles())` setup that returns `IEnumerable<ITile>` must return `IEnumerable<TileMetadata>`; every `Setup(x => x.GetTile(...))` becomes `Setup(x => x.GetTileMetadata(...))` returning a `TileMetadata`.
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/GetAvailableTilesHandlerTests.cs` — same updates to `GetAvailableTiles()` mock setups.
- `backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs` — update any setups referencing the old surface.

Existing test helpers that build fake `ITile` instances (e.g. mock tiles with literal properties) should be replaced with direct `TileMetadata` construction.

**Acceptance criteria:**
- `dotnet build` succeeds at the solution level.
- All previously passing tests in the three files above continue to pass.
- No test references `ITileRegistry.GetTile` or expects `IEnumerable<ITile>` from the registry.

### FR-7: Add regression test for scope disposal
Add a new unit test in `backend/test/Anela.Heblo.Tests/Services/Dashboard/TileRegistryTests.cs` (create the file if it doesn't exist) that verifies the lifetime contract directly. The test must:

- Register a fake `ITile` whose constructor takes a scoped tracking service.
- Register the tracking service as `AddScoped<T>()` so a new instance is created per scope and disposed when the scope is disposed.
- Call `GetTileMetadata(...)` and `GetAvailableTiles()`.
- Assert that calling any method on the returned `TileMetadata` does NOT touch the tracking service (proving the returned object is decoupled from the scoped graph).
- Assert that the tracking service was disposed by the time the call returned (proving the scope was closed).

**Acceptance criteria:**
- Test exists and passes against the new implementation.
- Test would fail against the previous `GetTile()` if the fake tile read a scoped service from a property getter (this is for documentation — it is not required to be encoded as a separate failing test against legacy code).

## Non-Functional Requirements

### NFR-1: Performance
- `GetTileMetadata(string)` and `GetAvailableTiles()` must not be measurably slower than current implementations under normal load. The only added work is a small object allocation per tile (the `TileMetadata`); no extra DI resolution, no extra scope creation.
- `GetAvailableTiles()` continues to use a single DI scope across all registered tiles (not one scope per tile) to avoid linear scope overhead.

### NFR-2: Backwards compatibility
- The external HTTP contract (`DashboardController.GetAvailableTiles` returning `IEnumerable<DashboardTileDto>`) is unchanged in shape and field values.
- `TileData` shape returned to dashboard data callers is unchanged.
- `ITile` interface itself is unchanged — tile authors do not need to modify their implementations.

### NFR-3: Safety / correctness
- After this change, no public method on `ITileRegistry` may return a reference to a scoped `ITile`. Only `GetTileDataAsync` is permitted to touch a scoped tile, and it does so inside the same scope it creates.
- Code review / `dotnet build` must show zero remaining call sites referencing the removed `GetTile` method.

### NFR-4: Code style
- Follow project conventions (`dotnet format` clean).
- `TileMetadata` lives next to `ITile.cs` in `backend/src/Anela.Heblo.Xcc/Services/Dashboard/`.

## Data Model

```
TileMetadata (new, immutable value object)
├── TileId: string
├── Title: string
├── Description: string
├── Size: TileSize
├── Category: TileCategory
├── DefaultEnabled: bool
├── AutoShow: bool
├── ComponentType: Type
└── RequiredPermissions: string[]
```

`TileMetadata` is constructed once per `ITile` resolution, inside a DI scope. After construction it has no reference to the originating `ITile` or `IServiceScope`.

`ITile` interface is unchanged. `TileExtensions.GetTileId(this ITile)` is unchanged.

## API / Interface Design

### `ITileRegistry` (after change)

```csharp
public interface ITileRegistry
{
    void RegisterTile<TTile>() where TTile : class, ITile;
    IEnumerable<TileMetadata> GetAvailableTiles();
    TileMetadata? GetTileMetadata(string tileId);
    Task<object?> GetTileDataAsync(string tileId, Dictionary<string, string>? parameters = null);
    IEnumerable<string> GetRegisteredTileIds();
}
```

### `TileRegistry.GetTileMetadata(...)` reference implementation

```csharp
public TileMetadata? GetTileMetadata(string tileId)
{
    if (!_registeredTiles.TryGetValue(tileId, out var tileType))
    {
        return null;
    }

    using var scope = _serviceProvider.CreateScope();
    var tile = (ITile)scope.ServiceProvider.GetRequiredService(tileType);
    return new TileMetadata(
        TileId: tile.GetTileId(),
        Title: tile.Title,
        Description: tile.Description,
        Size: tile.Size,
        Category: tile.Category,
        DefaultEnabled: tile.DefaultEnabled,
        AutoShow: tile.AutoShow,
        ComponentType: tile.ComponentType,
        RequiredPermissions: tile.RequiredPermissions);
}
```

### `TileRegistry.GetAvailableTiles()` reference implementation

```csharp
public IEnumerable<TileMetadata> GetAvailableTiles()
{
    var result = new List<TileMetadata>(_registeredTiles.Count);
    using (var scope = _serviceProvider.CreateScope())
    {
        foreach (var tileType in _registeredTiles.Values)
        {
            var tile = (ITile)scope.ServiceProvider.GetRequiredService(tileType);
            result.Add(new TileMetadata(
                tile.GetTileId(), tile.Title, tile.Description, tile.Size,
                tile.Category, tile.DefaultEnabled, tile.AutoShow,
                tile.ComponentType, tile.RequiredPermissions));
        }
    }
    return result;
}
```

### HTTP surface (unchanged)

- `GET /api/dashboard/available-tiles` — same `DashboardTileDto[]` payload as before.
- All tile data endpoints unchanged.

## Dependencies

- `Microsoft.Extensions.DependencyInjection` (already referenced).
- No new NuGet packages.
- No new framework features beyond what's already in use.
- The change is isolated to the `Anela.Heblo.Xcc` project (production) and the `Anela.Heblo.Application` + test projects (consumers).

## Out of Scope

- Changing the scoped lifetime of tile registrations to singleton. (The brief mentioned this as an alternative. It is rejected for this work because future tiles may legitimately need scoped dependencies for `LoadDataAsync`, and per-request scoping is the safer default.)
- Changing the `ITile` interface or refactoring existing tile implementations (`BackgroundTaskStatusTile`).
- Changing `GetTileDataAsync` — it is already correct.
- Frontend changes — none required; HTTP contract is preserved.
- Adding new dashboard tiles or features.
- Renaming `GetAvailableTiles` despite the return type change (kept for minimal diff at consumer call sites).
- Caching `TileMetadata` across calls. Metadata is recomputed per call from a fresh scope, matching today's behavior. Caching is a separate optimization, not a correctness fix.
- Documenting an architectural rule that registry-style services must never leak scoped instances. (Can be added later to `docs/architecture/development_guidelines.md` if the team wants — see Open Questions.)

## Open Questions

None.

## Status: COMPLETE