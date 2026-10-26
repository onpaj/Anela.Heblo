# Specification: GraphFolderResolver unit tests

## Summary
Add unit tests for `GraphFolderResolver` (internal, `Anela.Heblo.Application/Shared/Rag/OneDrive`) lifting line coverage from 37.7% to >=60%. Test-only; no production change.

## Functional Requirements
### FR-1: Existing folder (GET 200) returns its id, no POST.
### FR-2: Flat path, GET 404 -> GET `/drives/{d}/root`, then POST `/items/{rootId}/children` with `{name, folder:{}}`; returns created id.
### FR-3: Deep path a/b/c, all missing -> recursive GETs for a/b/c, a/b, a; root fetched once; POSTs a, b, c in order each under the previous id.
### FR-4: Partial existence (a exists, a/b missing) -> only b and c created; parent of b is a's id.
### FR-5: Non-404 on GET (500, 403) -> `HttpRequestException` (from EnsureSuccessStatusCode, NOT wrapped); no POST issued.
### FR-6: Failed POST (409) -> `HttpRequestException`.
### FR-7: Caching: second `GetOrCreateFolderIdAsync` for same drive/path makes no HTTP call; root id cached across two different flat paths (one root GET).
### FR-8: Leading/trailing slashes are trimmed (`/a/` behaves as `a`).

## Findings on the issue's claims
- `lastSlash == 0` is unreachable: `GetOrCreateFolderIdAsync` calls `Trim('/')` before `ResolveOrCreateAsync`, and recursion passes `folderPath[..lastSlash]` of a trimmed path. The `<= 0` branch is effectively `< 0`. No production fix needed; FR-8 documents this.
- Non-404 errors throw plain `HttpRequestException` (no wrapping). Tests assert current behaviour; adding context is out of scope.

## Non-Functional
Tests must be deterministic, no network; follow existing style in `GraphOneDriveServiceTests.cs` (xunit, Moq, stub HttpMessageHandler).

## Dependencies
`InternalsVisibleTo("Anela.Heblo.Tests")` already present. Test file: `backend/test/Anela.Heblo.Tests/Shared/Rag/OneDrive/GraphFolderResolverTests.cs`.
