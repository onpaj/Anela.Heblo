# Implementation: remove-dead-grid-layout-persistence-payload

## What was implemented
Deleted the unused `GridLayoutPersistencePayload` record (never referenced anywhere in
production code — the active persistence path is `StoredGridLayout`/`StoredColumnState`
via `GridLayoutStoredMapper`), and corrected the stale XML doc comment on
`SaveGridLayoutHandlerPayloadTests` that named the deleted type. No behavioral change.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutPersistencePayload.cs` — deleted (via `git rm`).
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutHandlerTests.cs` — updated the XML doc
  comment (lines 14-18) on `SaveGridLayoutHandlerPayloadTests` to no longer name the deleted type
  ("These tests verify that the persisted JSON payload contains only columns, and that GridLayoutDto
  is assembled from that payload + entity.GridKey + entity.LastModified."). No other lines touched.

## Tests
- Repo-wide grep for `GridLayoutPersistencePayload`: only remaining hit is the historical plan doc
  `docs/superpowers/plans/2026-06-08-gridlayouts-slim-persistence-payload.md` (out of scope, expected).
- `dotnet build Anela.Heblo.sln` (from repo root): succeeded, 0 errors, 254 pre-existing warnings
  (none related to this change).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayouts&FullyQualifiedName!~IntegrationTests" --no-build`:
  Passed - 35/35 (all unit tests, including `SaveGridLayoutHandlerPayloadTests`).
- Note: `--filter FullyQualifiedName~GridLayouts` alone shows 7 additional failures in
  `GridLayoutRepositoryUpsertIntegrationTests`, but these fail because Docker is unavailable in this
  sandbox (Testcontainers Postgres fixture throws "Docker is either not running or misconfigured") —
  confirmed unrelated to this change and pre-existing environment limitation, not a regression.

## How to verify
1. `grep -r "GridLayoutPersistencePayload" backend/` → no matches.
2. `dotnet build Anela.Heblo.sln` from repo root → 0 errors.
3. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GridLayouts&FullyQualifiedName!~IntegrationTests"` → all pass.
4. `git show --stat HEAD` → confirms only the two intended files changed.

## Notes
Integration tests under `GridLayoutRepositoryUpsertIntegrationTests` could not be executed in this
sandbox due to no Docker daemon being available for the Postgres Testcontainer fixture; this is an
environmental limitation unrelated to the change (those tests don't reference the deleted type or the
edited doc comment).

## Status
DONE
