File writes are blocked in this session. Outputting the architecture review directly as text per the task instructions.

# Architecture Review: Explicit Stable Tile IDs for Dashboard Tiles

## Skip Design: true

This is a backend-only refactor of an internal contract. No UI components, screens, layouts, or visual decisions are introduced. The DTO shape (`UserDashboardTileDto.TileId`, `DashboardTileDto.TileId`, etc.) and the on-wire string values are preserved verbatim, so the frontend sees zero behavioral change.

## Architectural Fit Assessment

The proposal aligns cleanly with the project's Clean Architecture / Vertical Slice conventions:

- `ITile` already lives in `Anela.Heblo.Xcc.Services.Dashboard` as a cross-cutting contract owned by the `Xcc` ("cross-cutting concerns") project. Adding `TileId` to that interface and a `TileIdValidator` next to it keeps the contract and its validation in the single project that owns the abstraction — no module boundary is crossed.
- All ~23 concrete tile implementations already live inside their feature module's `DashboardTiles/` folder (`Features/<Module>/DashboardTiles/<Name>Tile.cs`) and are registered via `services.RegisterTile<T>()` from each module's `*Module.cs`. The change adds one line per file inside the boundary the file already belongs to.
- The registration pipeline (`TileRegistryExtensions` → tracks types in a static `ConcurrentBag`, then `InitializeTileRegistry` resolves each on `IHost` startup) already runs **after** `BuildServiceProvider`, so it is safe to create a `IServiceScope` inside `TileRegistry.RegisterTile<TTile>()` to read the literal `TileId` from a resolved instance. This is the only behavioral change to the bootstrap sequence and it is structurally identical to the existing `GetAvailableTiles()` / `GetTile(id)` paths that already resolve tiles via a scope.
- The DB contract (`UserDashboardTile.TileId` column, `HasMaxLength(100)`, unique `(UserId, TileId)` index in `UserDashboardTileConfiguration.cs:20–36`) is unchanged. The `^[a-z0-9-]{1,100}$` validator is **tighter** than the column constraint, which is correct — code-side IDs are stricter than the DB allows.

Two integration points the spec correctly identifies and the implementation must not miss:

1. **Test doubles implement `ITile` directly.** `GetAvailableTilesHandlerTests.cs` defines `TestTile1`, `TestTile2`, `TestTileNoPermissions`; `DashboardServiceTests.cs` defines `NewAutoShowTile`, `ManualTile`, `AutoTile1`, `AutoTile2`, `TestTileWithData`. Adding `TileId` to the interface breaks all of these at compile time — they each need a literal added that preserves the value the test currently asserts (`"test1"`, `"test2"`, `"newautoshow"`, `"manual"`, `"auto1"`, `"auto2"`, `"testwithdata"`, plus the `_tileId` constructor parameter on `TestTileWithData`).
2. **`TileExtensions.GetTileId<T>()` is asserted by name in two tile tests** (`ManufactureConditionsTileTests.cs:48`, `WeatherForecastTileTests.cs:40`). These assertions become `new ManufactureConditionsTile(...).TileId.Should().Be(...)` style after the extension is deleted.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Xcc / Services / Dashboard                                  │
│                                                                         │
│   ITile  ──────► string TileId { get; }   ◄── NEW (required, literal)   │
│                  string Title { get; }                                  │
│                  ... (existing properties unchanged)                    │
│                                                                         │
│   TileIdValidator (new, static)                                         │
│     ├── public static Regex Pattern = ^[a-z0-9-]{1,100}$                │
│     └── public static bool IsValid(string?)                             │
│                                                                         │
│   TileRegistry.RegisterTile<TTile>()                                    │
│     ├── resolve TTile from a startup scope                              │
│     ├── read instance.TileId                                            │
│     ├── TileIdValidator.IsValid → throw if not                          │
│     ├── duplicate check vs _registeredTiles.Keys → throw if collision   │
│     └── _registeredTiles[tileId] = typeof(TTile)                        │
│                                                                         │
│   TileExtensions.cs ──────► DELETED                                     │
└─────────────────────────────────────────────────────────────────────────┘
                              ▲                       ▲
                              │ implements            │ uses
                              │                       │
┌─────────────────────────────┴───────┐  ┌────────────┴────────────────────┐
│ Anela.Heblo.Application             │  │ Application/Features/Dashboard/  │
│   Features/<Module>/DashboardTiles/ │  │   UseCases/GetAvailableTiles/    │
│   *.cs (22 files)                   │  │     Handler: t.TileId (was       │
│                                     │  │              t.GetTileId())      │
│   each adds:                        │  │   Service: DashboardService:     │
│     public string TileId =>         │  │     tile.TileId (4 call sites)   │
│       "<preserved literal>";        │  │                                  │
└─────────────────────────────────────┘  └──────────────────────────────────┘
                              ▲
                              │ scans
                              │
┌─────────────────────────────┴───────────────────────────────────────────┐
│ Anela.Heblo.Tests / Features / Dashboard /                              │
│   TileRegistrationContractTests.cs (new)                                │
│     ├── builds a host with all production module registrations          │
│     ├── reflects over Application + Xcc assemblies for !abstract ITile  │
│     ├── for each: resolve via DI, assert TileId valid + non-duplicate   │
│     └── one fail-fast message per violation                             │
└─────────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Interface property vs `[TileId]` attribute vs `const string`
**Options considered:**
- (a) Required `ITile.TileId` instance property returning a literal.
- (b) `[TileId("...")]` attribute on the class, read by reflection.
- (c) `public const string TileId = "..."` on each tile, surfaced via a static reflective helper.

**Chosen approach:** (a) — explicit interface property. This is what the spec mandates and it is correct.

**Rationale:** The compiler is the only enforcement mechanism that catches a new tile that forgets to declare an ID. Attributes (b) and consts (c) both require runtime reflection to surface a missing value, which moves the failure from compile time to startup/test time. The cost is one extra line per tile (~22 places) — paid once, never re-paid. The price is well worth eliminating an entire class of "I added a tile and forgot to set the ID" bug at the source. The interface property also makes the contract visible to anyone reading `ITile.cs`, which is the single file someone reaches for when implementing a new tile.

#### Decision 2: Validate at registration vs validate in a test only
**Options considered:**
- (a) Validate `TileId` only in the `TileRegistrationContractTests` discovery test.
- (b) Validate inside `TileRegistry.RegisterTile<TTile>()` at app startup, plus the discovery test.

**Chosen approach:** (b) — defense in depth.

**Rationale:** The discovery test is the authoritative gate, but it runs in CI. A developer who skips the test locally and pushes (or a refactor that introduces a tile in a not-yet-tested code path) would otherwise reach production with an invalid ID and only fail at first user request (`"Tile not found"` in `DashboardService.cs:147`). Throwing in `RegisterTile<TTile>()` instead crashes the app at startup with a fully qualified type name and the offending ID — fail fast, fail loud, before any user is affected. The runtime cost is one DI resolution per tile per process start (already paid by `GetAvailableTiles` on every dashboard load — moving it earlier is a wash).

#### Decision 3: Delete `TileExtensions.cs` vs reduce to a property-reading shim
**Options considered:**
- (a) Delete the file outright; all call sites read `tile.TileId` directly.
- (b) Keep `public static string GetTileId(this ITile tile) => tile.TileId;` for source compatibility.

**Chosen approach:** (a) — delete the file.

**Rationale:** The spec lists six call sites: `DashboardService.cs:55, 70, 79, 160`, `GetAvailableTilesHandler.cs:22`, and two test assertions. All are inside this PR. Keeping a shim guarantees the `Type`-based and generic overloads come back in IDE auto-complete next time someone wants "the tile ID for a type," and the whole point of the refactor is to remove the temptation to derive an ID from a `Type`. Delete it cleanly; let the next reader find `tile.TileId` and follow it to the property. (This is consistent with the project's "surgical, no half-finished" stance in `CLAUDE.md`.)

#### Decision 4: Where the validator lives
**Chosen approach:** A new `TileIdValidator` static class in `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileIdValidator.cs`, exposing a single `IsValid(string? id)` method and the compiled `Regex` as an `internal static readonly` field for reuse by the discovery test.

**Rationale:** The `Anela.Heblo.Xcc.Services.Dashboard` namespace already owns `ITile`, `TileRegistry`, and the registration extensions. Putting the validator anywhere else fragments ownership. One file, one regex, one entry point — the spec's FR-3 "defined in a single place" requirement is literally met by the file layout.

#### Decision 5: How the contract test discovers tiles
**Options considered:**
- (a) Enumerate types returned by `ITileRegistry.GetRegisteredTileIds()` after building the host.
- (b) Reflect over `typeof(Anela.Heblo.Application.AssemblyMarker).Assembly` + `typeof(Anela.Heblo.Xcc.AssemblyMarker).Assembly` for `!IsAbstract && typeof(ITile).IsAssignableFrom(t)`.
- (c) Both: reflect for the set, then assert the registry-known set equals the reflected set.

**Chosen approach:** (c) — reflect for the full set, then cross-check against the registry.

**Rationale:** Reflection alone catches "ID is invalid" and "two tiles share an ID." The cross-check additionally catches the third failure mode the spec doesn't explicitly call out: "I added a tile class but forgot to call `services.RegisterTile<T>()` in my feature module's registration." Without the cross-check, that new tile would never appear in the dashboard and the bug would only show up in QA. With it, the contract test fails with `"Tile X is not registered with any module"`. Cheap to add, high-signal.

## Implementation Guidance

### Directory / Module Structure

**New files (1):**
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileIdValidator.cs`

**Deleted files (1):**
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileExtensions.cs`

**Modified files (Xcc):**
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/ITile.cs` — add `string TileId { get; }` as the first property.
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileRegistry.cs` — change `RegisterTile<TTile>()` body (see "Interfaces and Contracts" below).
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/DashboardService.cs` — change `tile.GetTileId()` → `tile.TileId` at lines 55, 70, 79, 160.
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/Tiles/BackgroundTaskStatusTile.cs` — add `public string TileId => "backgroundtaskstatus";`.

**Modified files (Application — one per concrete tile, 22 files):**
- Add a single property line per tile, value per the FR-2 table in the spec. The literal **must** be the lowercased, `-tile`-stripped class name as it exists today. The full mapping is in the spec; do not deviate.

**Modified files (Application handlers):**
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetAvailableTiles/GetAvailableTilesHandler.cs:22` — `TileId = t.GetTileId()` → `TileId = t.TileId`.

**Test files to modify:**
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/GetAvailableTilesHandlerTests.cs` — add `public string TileId => "test1";` to `TestTile1`, `"test2"` to `TestTile2`, and the value the existing test asserts to `TestTileNoPermissions`. Preserve the asserted value; do not change the test's expected string.
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/DashboardServiceTests.cs` — add `TileId` to `NewAutoShowTile` (`"newautoshow"`), `ManualTile` (`"manual"`), `AutoTile1` (`"auto1"`), `AutoTile2` (`"auto2"`), and to `TestTileWithData` route the constructor-supplied `tileId` through the property: `public string TileId => _tileId;`.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/DashboardTiles/ManufactureConditionsTileTests.cs:48` — replace `TileExtensions.GetTileId<ManufactureConditionsTile>().Should().Be("manufactureconditions")` with an equivalent that reads the property (e.g. construct the tile via the test host and assert on `.TileId`, or use a typed `new ManufactureConditionsTile(...).TileId`).
- `backend/test/Anela.Heblo.Tests/Features/WeatherForecast/DashboardTiles/WeatherForecastTileTests.cs:40` — same pattern as above against `WeatherForecastTile`.

**New test file:**
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/TileRegistrationContractTests.cs`

### Interfaces and Contracts

**`ITile.cs` (full file after change):**
```csharp
namespace Anela.Heblo.Xcc.Services.Dashboard;

public interface ITile
{
    // Stable identifier persisted in UserDashboardTiles.TileId.
    // MUST be a string literal — see TileRegistrationContractTests for the contract.
    string TileId { get; }

    string Title { get; }
    string Description { get; }
    TileSize Size { get; }
    TileCategory Category { get; }
    bool DefaultEnabled { get; }
    bool AutoShow { get; }
    Type ComponentType { get; }
    string[] RequiredPermissions { get; }

    Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null,
                               CancellationToken cancellationToken = default);
}
```

The one-line comment above `TileId` is the **only** comment to add — it documents the non-obvious invariant (literal, persisted) that an `ITile.cs` reader cannot infer from the signature. No comments on the other properties; none of them changed.

**`TileIdValidator.cs` (full file):**
```csharp
using System.Text.RegularExpressions;

namespace Anela.Heblo.Xcc.Services.Dashboard;

public static class TileIdValidator
{
    internal static readonly Regex Pattern =
        new("^[a-z0-9-]{1,100}$", RegexOptions.Compiled);

    public static bool IsValid(string? id) =>
        !string.IsNullOrEmpty(id) && Pattern.IsMatch(id);
}
```

`internal` on `Pattern` is deliberate — the discovery test in the same solution can use `InternalsVisibleTo` (or re-derive the literal) without exposing the regex as public API.

**`TileRegistry.RegisterTile<TTile>()` (only the method body changes):**
```csharp
public void RegisterTile<TTile>() where TTile : class, ITile
{
    using var scope = _serviceProvider.CreateScope();
    var instance = scope.ServiceProvider.GetRequiredService<TTile>();
    var tileId = instance.TileId;

    if (!TileIdValidator.IsValid(tileId))
    {
        throw new InvalidOperationException(
            $"Tile {typeof(TTile).FullName} declared invalid TileId '{tileId}'. " +
            $"Expected format: ^[a-z0-9-]{{1,100}}$.");
    }

    if (_registeredTiles.TryGetValue(tileId, out var existing))
    {
        throw new InvalidOperationException(
            $"Duplicate TileId '{tileId}': " +
            $"{existing.FullName} and {typeof(TTile).FullName} both declare the same id.");
    }

    _registeredTiles[tileId] = typeof(TTile);
}
```

No other method on `TileRegistry` changes. `GetTile(string)` and `GetTileDataAsync(string)` continue to key the dictionary by the now-explicit id.

**Tile implementation pattern (every concrete tile):**
```csharp
public class PurchaseOrdersInTransitTile : ITile
{
    public string TileId => "purchaseordersintransit";  // ← the one new line
    public string Title => "Suma nákupních objednávek";
    // ... rest unchanged
}
```

For the abstract `TransportBoxBaseTile`: do **not** add `TileId`. The base remains `abstract`, the four concrete subclasses (`ReceivedBoxesTile`, `ErrorBoxesTile`, `InTransitBoxesTile`, `CriticalGiftPackagesTile`) each declare their own literal per the FR-2 table. (Verify during implementation that `TransportBoxBaseTile` is not in any `RegisterTile<...>()` call — `grep -n "RegisterTile<TransportBoxBaseTile>" backend/src` should return empty. If it ever appears, the design fails because the interface property is abstract on the base.)

### Data Flow

**Startup (once per process):**
```
Program.cs
  → services.AddXccModule() / AddCatalogModule() / ...
      → each calls services.RegisterTile<T>() (TileRegistryExtensions.cs:16)
          → services.AddScoped<T>()                  (DI registration only)
          → RegisteredTileTypes.Add(typeof(T))       (static bag)
  → BuildServiceProvider()
  → app.InitializeTileRegistry()                     (TileRegistryExtensions.cs:29)
      → foreach typeof(T) in RegisteredTileTypes:
          → registry.RegisterTile<T>()               (TileRegistry.cs, NEW BEHAVIOR)
              → scope.GetRequiredService<T>()        (one-time DI resolution)
              → read instance.TileId
              → TileIdValidator.IsValid → throw or pass
              → duplicate check → throw or pass
              → _registeredTiles[id] = typeof(T)
```

**Per-request (existing flow, unchanged in shape):**
```
GET /api/dashboard
  → DashboardController → GetUserSettings query
      → DashboardService.GetUserSettingsAsync(userId)
          → settingsRepository.GetByUserIdAsync(userId)
          → tileRegistry.GetAvailableTiles()
              → for each typeof(T) in _registeredTiles.Values:
                   scope.GetRequiredService(T) → ITile instance
          → autoShow filter, create UserDashboardTile rows
              → new UserDashboardTile { TileId = tile.TileId, ... }   ← was tile.GetTileId()
          → settingsRepository.AddAsync / UpdateAsync
  → DashboardService.GetTileDataAsync(userId)
      → for each visible UserDashboardTile:
          → registry.GetTile(t.TileId)               ← dict lookup, unchanged
          → registry.GetTileDataAsync(t.TileId)      ← dict lookup, unchanged
          → new TileData { TileId = tile.TileId, ... }   ← was tile.GetTileId()
```

**Test discovery flow (new):**
```
TileRegistrationContractTests.AllTiles_HaveValidUniqueRegisteredIds
  → reflect: Application.Assembly.GetTypes() ∪ Xcc.Assembly.GetTypes()
           ∩ where t is class && !abstract && ITile.IsAssignableFrom(t)
  → build a minimal Host with the same module registrations as production
        (or use the existing test fixture if one already wires modules; otherwise
         compose ad hoc — modules are: Xcc, Catalog, Analytics, BackgroundJobs,
         DataQuality, Manufacture, Purchase, WeatherForecast, Logistics, Dashboard)
  → for each discovered type T:
      ├── assert ITileRegistry.GetRegisteredTileIds().Contains(T.TileId)
      ├── resolve T via host.Services.GetRequiredService(T)
      ├── assert TileIdValidator.IsValid(instance.TileId)
      └── add (instance.TileId, T.FullName) to a list
  → assert no duplicate TileId across the list
  → collect all failures, fail with a single message listing every violation
```

The "minimal host" composition step is the only non-trivial piece. Two ways to do it, in preference order:

1. **Reuse the existing integration-test fixture** if `Anela.Heblo.Tests` already has one (`WebApplicationFactory<Program>` or similar). A `Host.CreateApplicationBuilder()` with the registration extensions called is a host but not a *running* one and does not start Kestrel or open a DB connection — that satisfies the NFR-5 spirit (pure in-process, no I/O).
2. **Compose ad hoc** by calling each module's `services.AddXxxModule()` extension directly on a fresh `ServiceCollection`, then calling `BuildServiceProvider()` and `InitializeTileRegistry()`. This is what the new test should do if no integration fixture exists yet.

The implementer must verify (1) during the first task and pick (2) only if no fixture is available.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Developer copies an existing literal verbatim into a new tile, producing a silent duplicate. | **High** | Duplicate detection in `TileRegistry.RegisterTile<TTile>()` (FR-4) throws at startup; the contract test (FR-5) catches it in CI before merge. Both layers required. |
| A new tile is added but no `services.RegisterTile<T>()` call exists in any module — the tile compiles, has a valid `TileId`, but never reaches the dashboard. | **Medium** | The contract test cross-checks reflected `ITile` types against `ITileRegistry.GetRegisteredTileIds()` and fails with "tile X is not registered" (Decision 5). |
| Implementer mistypes one of the 22 FR-2 literals (e.g. `"purchaseordersintransat"`), persisted user data breaks for that one tile on deploy. | **High** | The fixtures table in FR-2 is the source of truth. A unit test that does this comparison (using the still-extant derivation captured *before* `TileExtensions.cs` is deleted, then deleted along with it) is the cleanest enforcement. The spec calls for it in FR-2; implement it as `TilePreservedIdTests` and delete it together with `TileExtensions.cs` in a single commit. |
| `TransportBoxBaseTile` is mistakenly registered (or another abstract tile is later added that the contract test discovers as concrete). | **Low** | Reflection filter is `!t.IsAbstract`. Document in `TileRegistrationContractTests`: a one-line comment "abstract tiles are not discoverable" beside the filter. |
| `internal` validator regex is not visible from the test project. | **Low** | Add `[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]` to `Anela.Heblo.Xcc` (check if one already exists in `AssemblyInfo.cs` or `.csproj`). If the test prefers to re-derive the regex literal rather than depend on internals, that is also acceptable. |
| Startup performance regression from one DI resolution per tile during `InitializeTileRegistry`. | **Negligible** | ~23 transient scoped resolutions at app start, each cheap. Already paid by the existing `GetAvailableTiles()` path on every dashboard request. No mitigation needed; flag only if anyone questions it. |
| Constructor of a tile throws when resolved during registration (e.g. an unavailable dependency at startup time). | **Low** | This would crash app startup with a clear DI exception naming the failed type — strictly better than today's behavior of failing later inside `GetAvailableTiles()` mid-request. No change required; the failure mode is just earlier. |

## Specification Amendments

The spec is solid. Three small additions to make it implementation-ready:

1. **FR-5 cross-check.** Augment FR-5 to require the discovery test to assert every reflected concrete `ITile` is present in `ITileRegistry.GetRegisteredTileIds()`. Without this, "I forgot to register my new tile" remains an undetected failure mode (Decision 5).
2. **Test stubs are part of FR-1's compile-time guarantee.** The spec mentions tile classes in `Application` + `Xcc` but does not call out that **8 test doubles** (listed under "Test files to modify" above) will also fail to compile and must be updated in the same PR. The values are explicitly fixed by what the existing test assertions expect; do not invent new IDs.
3. **`[assembly: InternalsVisibleTo]` note.** If the discovery test reads `TileIdValidator.Pattern` (the `internal` field), the test project must be in `InternalsVisibleTo`. Add a one-line note under FR-3 or NFR-5 so this is not surprising. If the test instead duplicates the regex literal `"^[a-z0-9-]{1,100}$"` in its own constant, that is acceptable and avoids the `InternalsVisibleTo` dependency entirely — the implementer picks one.

These are clarifications, not behavioral changes. They do not alter any acceptance criterion.

## Prerequisites

Nothing must be built or provisioned before implementation can begin. Specifically:

- **No DB migration.** The `UserDashboardTile.TileId` column, constraints, and indexes are unchanged (`UserDashboardTileConfiguration.cs`). Confirm via `dotnet ef migrations list` after the change — the list must be identical to the pre-change list.
- **No configuration change.** `DashboardOptions` is untouched. No appsettings keys are added or read.
- **No NuGet package addition.** `System.Text.RegularExpressions` is in the BCL.
- **No frontend regeneration.** The OpenAPI surface is byte-identical because the DTOs are unchanged; running `npm run build` should produce a zero-line diff in `frontend/src/api/generated/api-client.ts`. Verify by running it once and inspecting `git status` in `frontend/` before committing.
- **One-time pre-implementation evidence capture.** Before deleting `TileExtensions.cs`, write `TilePreservedIdTests` that asserts, for every concrete tile type discovered by reflection, `typeof(T).Name.ToLowerInvariant().Replace("tile", "")` equals the new `.TileId` property value. Run it once green. This is the ratchet that proves FR-2 was met. Delete the test in the same commit that deletes `TileExtensions.cs` — once the literals are in code, the derivation is no longer the source of truth and the assertion becomes a tautology.