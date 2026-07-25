# Implementation: fix-graph-service-swallow

## What was implemented
`GraphService.GetGroupMembersAsync` previously swallowed non-success HTTP responses from
Microsoft Graph (403/404/429/5xx), logging the error but returning an empty `List<UserDto>()`
— making a Graph outage or permissions failure indistinguishable to callers from "the group
genuinely has zero members". The `!response.IsSuccessStatusCode` branch now throws
`GraphServiceException` wrapping an `HttpRequestException(errorContent)`, with message
`"Microsoft Graph returned {(int)response.StatusCode} for group {groupId}."`. This matches the
existing sibling handling for ODataError responses elsewhere in the same method and fully
implements FR-1 and FR-2 from `artifacts/feat-3724/spec.r1.md`.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — in
  `GetGroupMembersAsync`, the `!response.IsSuccessStatusCode` branch now throws
  `GraphServiceException` instead of returning `new List<UserDto>()`. The existing
  `_logger.LogError` and `_logger.LogDebug` calls are unchanged and still fire before the throw.
  No other line in `GetGroupMembersAsync`, `SearchUsersAsync`, or `GetAppRoleMembersAsync` changed.
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` — replaced
  `GetGroupMembersAsync_GraphReturnsNonSuccess_ReturnsEmptyList` with
  `GetGroupMembersAsync_GraphReturnsNonSuccess_ThrowsGraphServiceException` (asserts the thrown
  exception's message contains the status code and group id, and that `InnerException` is set),
  and added `GetGroupMembersAsync_GraphReturnsTooManyRequests_ThrowsGraphServiceException`
  covering the 429 case per spec FR-2's optional acceptance criterion.

## Tests
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`:
  - `GetGroupMembersAsync_GraphReturnsNonSuccess_ThrowsGraphServiceException` — 403 Forbidden
    response now throws `GraphServiceException` with message containing "403" and "group-1",
    and a non-null `InnerException`.
  - `GetGroupMembersAsync_GraphReturnsTooManyRequests_ThrowsGraphServiceException` — 429 Too
    Many Requests response now throws `GraphServiceException` with message containing "429"
    and "group-2", and a non-null `InnerException`.
  - Full `GraphServiceTests` class (14 tests) re-verified green, including the
    `GetAppRoleMembersAsync_*` regression tests (NFR-1) and MSAL/transport/disposal tests.

## How to verify
```bash
cd backend
# Targeted tests
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphServiceTests.GetGroupMembersAsync_GraphReturnsNonSuccess_ThrowsGraphServiceException|FullyQualifiedName~GraphServiceTests.GetGroupMembersAsync_GraphReturnsTooManyRequests_ThrowsGraphServiceException"

# Full GraphServiceTests class
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.UserManagement.GraphServiceTests"

# Downstream consumers
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetGroupMembersHandlerTests|FullyQualifiedName~GraphArticleUserResolver"

# Full solution gate (run from repo root, where Anela.Heblo.sln lives)
cd ..
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
```

## Notes
- The task context's Step 3 commands assumed `Anela.Heblo.sln` lives under `backend/`; it
  actually lives at the repository root (`/home/user/worktrees/.../Anela.Heblo.sln`). Ran the
  solution-level build/test/format commands from the repo root instead — no other deviation.
- Full-solution `dotnet test` reports 161 pre-existing failures unrelated to this change:
  - `Anela.Heblo.Adapters.Flexi.Tests` (72 failed) — integration tests requiring a live Flexi
    ERP connection, not available in this environment.
  - `Anela.Heblo.Adapters.Shoptet.Tests` (13 failed) — integration tests requiring a valid
    Shoptet API token / non-live-environment guard, not available in this environment.
  - `Anela.Heblo.Tests` (76 failed) — `LeafletRepositoryIntegrationTests` and similar tests
    requiring Docker/Testcontainers (Postgres), not available in this sandbox.
  None of these relate to `GraphService`, `UserManagement`, `GetGroupMembersHandler`, or
  `GraphArticleUserResolver` — confirmed via grep across the full test log. All GraphService-
  and UserManagement-scoped tests pass (14 + 6 = 20 tests, 0 failed).
- A pre-existing, unrelated `Anela.Heblo.AccessMatrixGen` post-build tool step fails during
  every `Anela.Heblo.API` build with a `System.Text.Json.JsonException` (environment/path
  issue), surfaced as MSB3073 warning, not an error — build still succeeds. Not touched, not
  in scope.
- `artifacts/feat-3724/state.json` shows as modified in `git status` (pre-existing pipeline
  state, not created by this task) — intentionally left out of the commit, which contains
  exactly the two files in scope.
- `dotnet build`, full `dotnet test`, and `dotnet format --verify-no-changes` all clean/green
  with respect to this change.

## PR Summary
Fixes an architecture-review finding (#3724): `GraphService.GetGroupMembersAsync` was silently
swallowing non-success HTTP responses from Microsoft Graph (403/404/429/5xx) by logging the
error and returning an empty list, making a genuine service failure indistinguishable from a
group with zero members. Both downstream consumers (`GetGroupMembersHandler` and
`GraphArticleUserResolver.ResolveByGroupAsync`) already had `catch (GraphServiceException)`
blocks in place, expecting this contract — they just never received it for this failure path
before now.

The fix throws `GraphServiceException` (wrapping an `HttpRequestException` carrying the
response body) from that branch, with a message identifying both the HTTP status code and the
group id, matching how the sibling ODataError branch in the same method already behaves.

Followed strict TDD: replaced the old
`GetGroupMembersAsync_GraphReturnsNonSuccess_ReturnsEmptyList` test with a renamed,
re-asserted version expecting the exception, added a second test for the 429 case, confirmed
both failed against the unmodified code (red), applied the one-line production change, and
confirmed green — then ran the full `GraphServiceTests` suite, the downstream consumer test
files, a full solution build, the full solution test suite, and `dotnet format
--verify-no-changes`.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` —
  throw `GraphServiceException` instead of returning an empty list on non-success Graph
  responses in `GetGroupMembersAsync`.
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` — renamed/
  re-asserted the 403 test, added a 429 test.

## Status
DONE
