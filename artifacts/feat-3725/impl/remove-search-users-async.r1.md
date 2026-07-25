# Implementation: remove-search-users-async

## What was implemented
Removed the unused `SearchUsersAsync` method from `IGraphService`, its two implementations (`GraphService`, `MockGraphService`), the now-orphaned `SearchResultLimit` constant, and the dedicated test file `GraphServiceSearchTests.cs`. Zero references to `SearchUsersAsync` remain anywhere in `backend/src` or `backend/test`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs` — removed the `SearchUsersAsync` declaration
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — removed the `SearchResultLimit` constant and the `SearchUsersAsync` method body
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs` — removed the `SearchUsersAsync` stub
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs` — deleted entirely (5 tests removed)

## Tests
- Repo-wide grep for `SearchUsersAsync` across `backend/src` and `backend/test` returns zero matches.
- `dotnet build Anela.Heblo.sln` succeeds with 0 errors.
- `dotnet format Anela.Heblo.sln --verify-no-changes` reports no formatting issues.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` run: 5942 passed, 96 failed, 4 skipped. All 96 failures are pre-existing integration tests (Bank, Leaflet, Catalog, GridLayouts, KnowledgeBase, Invoices, MeetingTasks, Persistence, Photobank, Purchase, Smartsupp) that require a Postgres/Docker Testcontainer, which is unavailable in this sandbox (`System.ArgumentException: Docker is either not running or misconfigured`). None involve `GraphService`/`UserManagement`, and none are new — this is a pre-existing environment limitation, not caused by this change.

## How to verify
1. `grep -rn "SearchUsersAsync" backend/src backend/test` — expect no matches.
2. `dotnet build Anela.Heblo.sln` — expect success.
3. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` in an environment with Docker available — expect the same pass count as before this change, minus the 5 deleted `SearchUsersAsync_*` tests.

## Notes
No behavior change to any reachable code path — this was a pure YAGNI cleanup of dead code with zero callers, confirmed independently by both the architecture review and this implementation pass.

## PR Summary
Deletes the unused `IGraphService.SearchUsersAsync` method along with its Microsoft Graph adapter implementation, mock stub, and dedicated 5-test file. The method had zero callers anywhere in the Application, API, or Domain layers, so removing it eliminates ~70 lines of dead production code and its test surface with no behavior change.

### Changes
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs` — removed `SearchUsersAsync` declaration
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — removed `SearchResultLimit` constant and `SearchUsersAsync` implementation
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs` — removed `SearchUsersAsync` stub
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs` — deleted

## Status
DONE
