# Task Plan: Remove dead `GridLayoutPersistencePayload` type

## Overview
This is a small, self-contained dead-code removal identified by the arch review: delete an unused
internal record and correct a stale doc comment that names it. Both the spec (`spec.r1.md`) and the
arch review (`arch-review.r1.md`, `Skip Design: true`) agree there is a single unit of work with no
architectural decisions left to make. A single task is appropriate — do not split further.

---

### task: remove-dead-grid-layout-persistence-payload

**Goal:** Delete the unused `GridLayoutPersistencePayload` type and correct the stale doc comment
that references it, with no behavioral change to the `GridLayouts` feature.

**Context (from spec.r1.md / arch-review.r1.md):**
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs` defines
  an `internal sealed record GridLayoutPersistencePayload(List<GridColumnStateDto> Columns)` that is
  never referenced anywhere in production code. The active persistence path is
  `StoredGridLayout`/`StoredColumnState` via `GridLayoutStoredMapper`
  (`Features/GridLayouts/Infrastructure/GridLayoutStoredMapper.cs`), which is untouched by this change.
- The only other reference to the type's name in the repo is a stale XML doc comment (lines 14–18) on
  `SaveGridLayoutHandlerPayloadTests` in
  `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutHandlerTests.cs`. This was confirmed
  by reading the file directly — the comment reads:
  ```csharp
  /// <summary>
  /// Tests for the slim persistence payload refactor.
  /// These tests verify that GridLayoutPersistencePayload contains only columns,
  /// and that GridLayoutDto is assembled from payload + entity.GridKey + entity.LastModified.
  /// </summary>
  ```
  The test bodies themselves assert against `JsonSerializer.Deserialize<Dictionary<string, JsonElement>>`
  and do not reference the type — this is a comment-only fix.
- Out of scope (per spec): any change to `StoredGridLayout`, `StoredColumnState`,
  `GridLayoutStoredMapper`, any test assertion/logic, or the historical plan document
  `docs/superpowers/plans/2026-06-08-gridlayouts-slim-persistence-payload.md`.

**Files to create/modify:**
- Delete: `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutHandlerTests.cs` (doc comment
  on `SaveGridLayoutHandlerPayloadTests`, lines 14–18 only)

**Implementation steps:**
1. Delete the file `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs`
   in its entirety (e.g. `git rm`).
2. In `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutHandlerTests.cs`, replace the XML
   doc comment on `SaveGridLayoutHandlerPayloadTests` (currently lines 14–18) with wording that keeps
   the same intent but does not name the deleted type, e.g.:
   ```csharp
   /// <summary>
   /// Tests for the slim persistence payload refactor.
   /// These tests verify that the persisted JSON payload contains only columns,
   /// and that GridLayoutDto is assembled from that payload + entity.GridKey + entity.LastModified.
   /// </summary>
   ```
   Do not touch any other lines in this file — no method bodies, assertions, setup code, or the class
   name change.
3. Run a repository-wide search for the identifier `GridLayoutPersistencePayload` under `backend/`
   (excluding `docs/`) to confirm zero remaining references outside the historical plan document.
4. Build the backend solution and run the `GridLayouts` test suite to confirm no regressions.

**Tests to write:**
- No new tests are required — this is a dead-code deletion and comment-only edit with no behavioral
  change (per spec FR-1/FR-2 acceptance criteria).
- Existing tests in `GridLayoutHandlerTests.cs` must continue to pass unmodified; do not alter any
  assertion or setup logic while editing the doc comment.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs` no longer
  exists in the repository.
- A repository-wide search for `GridLayoutPersistencePayload` under `backend/**` returns zero matches.
- The string `GridLayoutPersistencePayload` no longer appears anywhere in `GridLayoutHandlerTests.cs`.
- The updated doc comment still accurately describes the test class's intent (persisted JSON payload
  contains only `columns`; `GridLayoutDto` is assembled from that payload plus `entity.GridKey` and
  `entity.LastModified`).
- `dotnet build` succeeds with no new errors or warnings introduced by the deletion.
- All tests in `GridLayoutHandlerTests.cs` (and the broader `GridLayouts` test suite) pass unmodified.
