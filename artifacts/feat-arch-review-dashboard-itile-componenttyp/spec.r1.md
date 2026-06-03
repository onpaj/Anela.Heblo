# Specification: Remove dead `ComponentType` property from `ITile` contract

## Summary
Remove the unused `Type ComponentType` property from the dashboard `ITile` contract and all carrier types (`TileMetadata`, `TileData`) plus every tile implementation. The value is set to `typeof(object)` everywhere, propagates through three layers, and is then silently dropped before reaching the API DTO — pure dead code that adds boilerplate to every new tile.

## Background
The dashboard subsystem exposes tiles via the `ITile` contract in `Xcc/Services/Dashboard/ITile.cs`. The contract declares `Type ComponentType { get; }`, which was originally intended to let the backend signal which React component should render a given tile. In practice the frontend never received this value:

- `DashboardTileDto` (the wire contract) has no `ComponentType` field, so the value never crosses the API boundary.
- `TileContent.tsx` already resolves the React component from `tileId` via a `switch` statement, so the frontend has no need for a backend-supplied type.
- Every tile implementation (e.g. `PurchaseOrdersInTransitTile.cs:17`, `TransportBoxBaseTile.cs:17`) sets the value to `typeof(object)` accompanied by a comment explaining the property is not needed.

The property therefore satisfies no functional need but imposes a compile-time obligation on every new tile author, inflates two carrier types (`TileMetadata`, `TileData`), and creates the false impression that the backend manages frontend component wiring. This finding was filed by the daily architecture review routine on 2026-06-01.

## Functional Requirements

### FR-1: Remove `ComponentType` from the `ITile` contract
Delete the `Type ComponentType { get; }` declaration from `Xcc/Services/Dashboard/ITile.cs`.

**Acceptance criteria:**
- `ITile.cs` no longer declares any member named `ComponentType`.
- The file compiles and existing XML doc / member comments remain coherent (no orphaned doc references to `ComponentType`).

### FR-2: Remove `ComponentType` from every tile implementation
Remove the `ComponentType` property (and its accompanying inline comment) from every concrete class that currently implements `ITile`. Known sites from the brief:
- `PurchaseOrdersInTransitTile.cs:17`
- `TransportBoxBaseTile.cs:17`
- Any other tile classes that follow the same pattern (an exhaustive search of implementers of `ITile` must be performed).

**Acceptance criteria:**
- `grep -r "ComponentType"` across the backend solution returns zero hits in dashboard tile code.
- All tile classes still implement `ITile` and the solution builds.
- No tile class loses unrelated behavior.

### FR-3: Remove `ComponentType` from `TileMetadata`
Delete the `Type ComponentType` member from the `TileMetadata` record at `Xcc/Services/Dashboard/TileMetadata.cs:11`. Update every constructor call / `with`-expression that supplied this value.

**Acceptance criteria:**
- `TileMetadata` no longer exposes a `ComponentType` member.
- All call sites that constructed `TileMetadata` are updated; solution builds.

### FR-4: Remove `ComponentType` from `TileData`
Delete the `public Type ComponentType { get; set; } = typeof(object);` member from `TileData` at `Xcc/Services/Dashboard/TileData.cs:12`. Update any object initializer or assignment that wrote to this property.

**Acceptance criteria:**
- `TileData` no longer exposes a `ComponentType` member.
- All call sites that constructed or mutated `TileData` are updated; solution builds.

### FR-5: Remove the mapping in `GetTileDataHandler`
Remove the `ComponentType = tile.ComponentType` assignment from `Application/.../GetTileData/GetTileDataHandler.cs` (around line 84). Verify no other handler or mapper performs an equivalent assignment.

**Acceptance criteria:**
- `GetTileDataHandler` no longer references `ComponentType`.
- A repository-wide search for `ComponentType` in the `Application` project returns zero hits related to dashboard tiles.

### FR-6: Preserve external behavior
The change is a pure refactor — no observable behavior may change for API consumers or the frontend.

**Acceptance criteria:**
- `DashboardTileDto` is unchanged (it already lacked `ComponentType`).
- The JSON shape returned by the dashboard endpoint(s) is byte-identical before and after the change for an equivalent dataset.
- `TileContent.tsx` and other frontend code are not modified.
- Backend unit tests covering tile retrieval still pass without modification (or with only mechanical updates removing now-deleted `ComponentType` setup).

### FR-7: Update tests that reference `ComponentType`
Any unit or integration test that constructs `TileMetadata`, `TileData`, or a fake `ITile` and sets `ComponentType` must be updated to drop that field. Tests that *assert* on `ComponentType` (if any exist) must be deleted, since the property is being removed entirely.

**Acceptance criteria:**
- `dotnet build` succeeds.
- `dotnet test` succeeds with no skipped or commented-out tests left behind.
- No test asserts on `ComponentType` after the change.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. Removing a `Type` reference from a few in-memory carriers may marginally reduce allocations but is not a measurable goal.

### NFR-2: Security
No security impact. The property carries no sensitive data and is not used in any authorization decision.

### NFR-3: Maintainability
Primary motivation. After this change:
- New tile authors no longer need to learn about or implement a placeholder `ComponentType`.
- Reviewers no longer need to scrutinize the value supplied.
- The mental model of the contract — "backend supplies data, frontend chooses how to render it" — is consistent across the codebase.

### NFR-4: Backward compatibility
The dashboard wire contract (`DashboardTileDto`) is unchanged, so frontend and any external API consumers are unaffected. Internal C# consumers of `ITile`, `TileMetadata`, or `TileData` will break at compile time if they reference `ComponentType`; this is desired (compile-time discovery of all touch points).

## Data Model
No persistent-data changes. The affected types are runtime carriers only:

- `ITile` — interface in `Xcc/Services/Dashboard/`; loses `ComponentType` property.
- `TileMetadata` — record in `Xcc/Services/Dashboard/`; loses `ComponentType` member.
- `TileData` — class in `Xcc/Services/Dashboard/`; loses `ComponentType` property.
- `DashboardTileDto` — unchanged (already does not contain `ComponentType`).

No database schema, no migration, no Key Vault changes.

## API / Interface Design
No API surface changes. The HTTP response for dashboard tile endpoints is unchanged because `DashboardTileDto` already omitted `ComponentType`. No new endpoints, no new events, no UI flow changes.

Internal C# interface change:

```csharp
// Before
public interface ITile {
    string TileId { get; }
    // ... other members ...
    Type ComponentType { get; }
}

// After
public interface ITile {
    string TileId { get; }
    // ... other members ...
}
```

Equivalent removals in `TileMetadata` and `TileData`.

## Dependencies
- No new external libraries.
- No version bumps.
- No infrastructure changes.
- Depends on the existing dashboard module and the OpenAPI client generation pipeline (which will regenerate cleanly because `DashboardTileDto` is unchanged).

## Out of Scope
- Refactoring the frontend `TileContent.tsx` switch statement or replacing it with a registry-based component lookup.
- Adding any backend-driven component selection mechanism (the explicit conclusion is that the backend does not need this concern).
- Cleaning up other unrelated dead members on `ITile`, `TileMetadata`, or `TileData` even if discovered during the change — file a separate finding instead.
- Changes to `DashboardTileDto` or any other wire contract.
- Adjusting the daily architecture review routine.

## Open Questions
None.

## Status: COMPLETE