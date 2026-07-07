# Architecture Review: Remove dead `GridLayoutPersistencePayload` type

## Skip Design: true

## Architectural Fit Assessment

This is a pure dead-code removal in the `GridLayouts` vertical slice
(`backend/src/Anela.Heblo.Application/Features/GridLayouts/`). I inspected the
three files in question directly:

- `GridLayoutPersistencePayload.cs` defines `internal sealed record
  GridLayoutPersistencePayload(List<GridColumnStateDto> Columns)`. It has a
  `[JsonPropertyName("columns")]` attribute but is never constructed,
  deserialized into, or passed as a parameter/return type anywhere.
- The actual persistence path already exists and is in active use:
  `GridLayoutStoredMapper` (in `Features/GridLayouts/Infrastructure/`) maps
  `GridColumnStateDto` ⇄ `StoredGridLayout`/`StoredColumnState`, and the
  save/get handlers work with `Dictionary<string, JsonElement>` for the raw
  JSON round-trip in tests. `GridLayoutPersistencePayload` sits alongside this
  as an orphaned, unused parallel shape.
- A repo-wide grep for `GridLayoutPersistencePayload` (backend `**/*.cs` and
  frontend `**/*.ts(x)`) returns exactly two hits: the type's own definition
  file, and a doc comment on `SaveGridLayoutHandlerPayloadTests` (lines 14–16)
  in `GridLayoutHandlerTests.cs`. No production code, DI registration,
  MediatR handler, controller, or frontend generated client references it —
  it's `internal`, so it was never exposed via the OpenAPI contract either.

There is no architectural decision to make here beyond "delete it and fix the
comment." No module boundary, DTO contract, or persistence format changes as
a result — `StoredGridLayout`/`StoredColumnState`/`GridLayoutStoredMapper`
remain exactly as they are today.

## Proposed Architecture

### Component Overview

No new or changed components. This removes one unreferenced internal type
and corrects a stale XML doc comment on an existing test class. The
`GridLayouts` feature's public shape (handlers, DTOs, repository,
`GridLayoutStoredMapper`) is untouched.

### Key Design Decisions

#### Decision 1: Delete outright vs. deprecate/mark obsolete
**Options considered:**
1. Delete the file entirely.
2. Mark the type `[Obsolete]` and leave it for a deprecation window.

**Chosen approach:** Delete the file entirely (option 1).

**Rationale:** The type is `internal`, has no external consumers by
construction (nothing outside this assembly could reference it), and has
zero in-repo references outside its own file. There is no deprecation
audience — `[Obsolete]` exists to warn external/future callers, and there
are none. Per the spec's YAGNI framing, keeping it around (even flagged)
just perpetuates the "is this a future replacement?" ambiguity the finding
called out.

## Implementation Guidance

### Directory / Module Structure

1. **Delete** `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs` in full (all 7 lines — the entire file).
2. **Edit** `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutHandlerTests.cs`, lines 14–17 (the XML doc comment on `SaveGridLayoutHandlerPayloadTests`):

   Current:
   ```csharp
   /// <summary>
   /// Tests for the slim persistence payload refactor.
   /// These tests verify that GridLayoutPersistencePayload contains only columns,
   /// and that GridLayoutDto is assembled from payload + entity.GridKey + entity.LastModified.
   /// </summary>
   ```

   Replace with wording that keeps the same intent but drops the type name,
   e.g.:
   ```csharp
   /// <summary>
   /// Tests for the slim persistence payload refactor.
   /// These tests verify that the persisted JSON payload contains only columns,
   /// and that GridLayoutDto is assembled from that payload + entity.GridKey + entity.LastModified.
   /// </summary>
   ```
   No other lines in this file change — test bodies, assertions, and the
   `SaveGridLayoutHandlerPayloadTests` class name stay as-is (confirmed by
   reading the test file: it already asserts against
   `JsonSerializer.Deserialize<Dictionary<string, JsonElement>>`, not against
   the deleted type).

No other files need to change. `GridLayoutStoredMapper.cs` and
`GridColumnStateDto`/`StoredGridLayout`/`StoredColumnState` are unaffected —
do not touch them.

### Interfaces and Contracts

N/A — `GridLayoutPersistencePayload` was `internal` and never part of any
MediatR request/response, controller contract, or the generated OpenAPI
client.

### Data Flow

N/A — no runtime data flow references this type; its removal has no effect
on the save/get grid layout request path.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Hidden reflection-based usage (e.g. via `JsonSerializer` polymorphism or DI scanning) that grep wouldn't catch | Low | `dotnet build` will fail loudly if any compiled reference exists, since the type is only usable via direct C# reference (no interface, no attribute-based registration found). Acceptance criteria already require a clean build. |
| Doc comment edit accidentally changes test semantics | Low | Restrict the edit strictly to the XML comment text (lines 14–17); do not touch method bodies or assertions. |

## Specification Amendments

None. The spec (`spec.r1.md`) is accurate and directly verified against the
working tree: file paths, line numbers, and current file contents all match
what I found. FR-1 and FR-2 are implementable exactly as written.

## Prerequisites

None — this change has no dependencies on other in-flight work and can be
implemented immediately.
