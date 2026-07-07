# Specification: Remove dead `GridLayoutPersistencePayload` type

## Summary
An arch-review finding identified `GridLayoutPersistencePayload.cs` in the `GridLayouts` feature as dead code: it is defined but never referenced by any production code path. This change deletes the unused file and corrects a stale doc-comment reference to it in the test suite, with no behavioral impact.

## Background
`GridLayoutPersistencePayload` is an `internal sealed record` in `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs`. A prior refactor plan (`docs/superpowers/plans/2026-06-08-gridlayouts-slim-persistence-payload.md`) proposed introducing this type as the on-disk JSON persistence shape for grid layouts, decoupling it from `GridLayoutDto`. The plan document describes `GridLayoutPersistencePayload` as "used by both save and get handlers," but the codebase as it exists today does not use it that way: the actual persistence serialization path uses `StoredGridLayout` / `StoredColumnState` via `GridLayoutStoredMapper` (`Infrastructure/GridLayoutStoredMapper.cs`), and the save/get handlers work directly with `Dictionary<string, JsonElement>`-shaped JSON, not `GridLayoutPersistencePayload`.

The only trace of `GridLayoutPersistencePayload` outside its own definition file is a doc comment on the test class `SaveGridLayoutHandlerPayloadTests` in `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutHandlerTests.cs`, which describes the tests as verifying properties of `GridLayoutPersistencePayload`. The tests themselves do not import, instantiate, or otherwise reference the type — they assert against parsed `JsonElement` dictionaries.

Leaving an unreferenced type with the same conceptual role as `StoredGridLayout` in the codebase creates ambiguity for future readers (is it a planned replacement? a leftover from an abandoned refactor? a parallel code path?). Per YAGNI, it should be removed.

## Functional Requirements

### FR-1: Delete the unused `GridLayoutPersistencePayload` type
Delete the file `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs` in its entirety. This file currently contains (verified in the working tree):

```csharp
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;

namespace Anela.Heblo.Application.Features.GridLayouts;

internal sealed record GridLayoutPersistencePayload(
    [property: JsonPropertyName("columns")] List<GridColumnStateDto> Columns);
```

(Note: the type parameter is `List<GridColumnStateDto>`, not `List<StoredColumnState>` as an earlier description of this finding assumed — this does not change the disposition, since the type is unreferenced regardless of its member types.)

**Acceptance criteria:**
- The file `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs` no longer exists in the repository.
- A repository-wide search for the identifier `GridLayoutPersistencePayload` in `backend/src/**` returns zero matches.
- `dotnet build` succeeds with no new errors or warnings introduced by the deletion.

### FR-2: Correct the stale doc comment in the test file
Update the XML doc comment on the `SaveGridLayoutHandlerPayloadTests` class in `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutHandlerTests.cs` (verified at lines 14–18 in the current working tree) so it no longer references the now-deleted `GridLayoutPersistencePayload` type by name. The comment currently reads:

```csharp
/// <summary>
/// Tests for the slim persistence payload refactor.
/// These tests verify that GridLayoutPersistencePayload contains only columns,
/// and that GridLayoutDto is assembled from payload + entity.GridKey + entity.LastModified.
/// </summary>
```

The reference to `GridLayoutPersistencePayload` is on line 16. Reword lines 14–18 to describe the same test intent (that the serialized persistence JSON contains only `columns`, and that `GridLayoutDto` is assembled from the payload plus `entity.GridKey` and `entity.LastModified`) without naming the deleted type — e.g. referring to "the persisted JSON payload" or "the on-disk payload shape" instead. The test class name (`SaveGridLayoutHandlerPayloadTests`), method bodies, and assertions are unaffected — this is a comment-only edit; no test logic reference to the type was found to require updating.

**Acceptance criteria:**
- The string `GridLayoutPersistencePayload` no longer appears anywhere in `GridLayoutHandlerTests.cs`.
- The doc comment still accurately describes what the test class verifies (slim JSON payload containing only `columns`; `GridLayoutDto` reconstructed from payload + `GridKey` + `LastModified`).
- All existing tests in `GridLayoutHandlerTests.cs` continue to pass unmodified (no assertion or setup logic is changed).

## Non-Functional Requirements
N/A — this is a dead-code removal and comment correction with no behavioral, performance, or security surface.

## Data Model
N/A — `GridLayoutPersistencePayload` was unused; no data model, serialization contract, or storage format is affected by its removal. The active persistence shape (`StoredGridLayout` / `StoredColumnState`, mapped via `GridLayoutStoredMapper`) is unchanged.

## API / Interface Design
N/A — the type is `internal` and was never exposed via any public API, controller, or MediatR contract.

## Dependencies
N/A — no other production files reference `GridLayoutPersistencePayload`. The stale plan document `docs/superpowers/plans/2026-06-08-gridlayouts-slim-persistence-payload.md` also mentions the type extensively, but historical plan documents are not in scope for this change and should not be edited.

## Out of Scope
- Any change to the actual persistence path (`StoredGridLayout`, `StoredColumnState`, `GridLayoutStoredMapper`).
- Any change to test assertions or behavior in `GridLayoutHandlerTests.cs` beyond the doc comment wording.
- Editing or removing the historical plan document `docs/superpowers/plans/2026-06-08-gridlayouts-slim-persistence-payload.md`.
- Any broader refactor of the `GridLayouts` feature's persistence contracts.

## Open Questions
None.

## Status: COMPLETE
