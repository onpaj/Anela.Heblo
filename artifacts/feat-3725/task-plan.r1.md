# Task Plan: Remove dead `IGraphService.SearchUsersAsync` method

## Overview
`IGraphService.SearchUsersAsync` is a fully-implemented, fully-tested method with zero callers anywhere in the solution. This plan removes it from the interface, both adapter implementations, and its dedicated test file in a single mechanical deletion task.

### task: remove-search-users-async

## Goal
Delete the unused `SearchUsersAsync` method from `IGraphService` and both of its implementations (`GraphService`, `MockGraphService`), remove the now-orphaned `SearchResultLimit` constant, and delete the dedicated test file `GraphServiceSearchTests.cs`, with zero remaining references to `SearchUsersAsync` anywhere in the solution and no behavior change to any reachable code path.

## Context
Verified (spec + independent architecture review grep across `backend/src/Anela.Heblo.Application`, `backend/src/Anela.Heblo.API`, `backend/src/Anela.Heblo.Domain`, `backend/frontend`) that `SearchUsersAsync` has **zero callers** outside of: the interface declaration, the two adapter implementations, and its own test file. This is a pure YAGNI cleanup with no architectural exception needed — textbook interface-segregation removal, no MediatR handler, controller, DTO-contract-consumed-outside-slice, or persisted-data involvement.

Exact deletions, verified against current file contents in this worktree:

1. **`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`**
   Delete line 14 only:
   ```csharp
   Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);
   ```
   Lines 5–13 (interface declaration + XML doc on `GetGroupMembersAsync`) and line 15 (`GetAppRoleMembersAsync`) remain unchanged, including their XML doc comments.

2. **`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`**
   - Delete line 25: `private const int SearchResultLimit = 25;` (has no reference outside the deleted method body — confirmed via grep: only the declaration and its one use inside `SearchUsersAsync`).
   - Delete lines 192–266 (the full `SearchUsersAsync` method, from `public async Task<List<UserDto>> SearchUsersAsync(...)` through its closing `}`). Line 267 is a blank separator line before `GetAppRoleMembersAsync` at line 268.
   - Do not touch `GraphBatchSize` (line 26), `AcquireGraphTokenAsync`, `ParseMembersFromJson`, `GetGroupMembersAsync`, or `GetAppRoleMembersAsync` — `GetAppRoleMembersAsync` depends on `GraphBatchSize` and `GetGroupMembersAsync`, none of which reference the deleted code.

3. **`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs`**
   Delete lines 22–26 (the `SearchUsersAsync` stub method):
   ```csharp
   public Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
   {
       _logger.LogInformation("Mock GraphService: SearchUsersAsync called for query '{Query}'", query);
       return Task.FromResult(new List<UserDto>());
   }
   ```
   `GetGroupMembersAsync` and `GetAppRoleMembersAsync` stubs are unchanged.

4. **`backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs`**
   Delete the entire file (all 121 lines, 5 test methods: `SearchUsersAsync_BuildsSearchRequest_AndParsesUsers`, `SearchUsersAsync_NonSuccess_ReturnsEmpty`, `SearchUsersAsync_EmptyQuery_ReturnsEmpty_WithoutTouchingFactory`, `SearchUsersAsync_TokenFailure_ReturnsEmpty_WithoutTouchingFactory`, `SearchUsersAsync_StripsDoubleQuotesFromQuery`).

**Do NOT touch** (confirmed no reference to `SearchUsersAsync`): `backend/test/Anela.Heblo.Tests/Helpers/FakeHttpMessageHandler.cs` (shared helper also used by `GraphServiceTests.cs` and `OutlookCalendarSyncServiceTests.cs`), `GraphServiceTests.cs`, `GetGroupMembersHandlerTests.cs`, `GetGroupMembersValidationPipelineTests.cs`, `GraphArticleUserResolver.cs`, `EntraAccessUserSourceAdapter.cs`, `UserManagementModule.cs`, `Microsoft365AdapterServiceCollectionExtensions.cs`. No DI registration changes are needed — both adapters remain complete, valid implementations of the narrowed `IGraphService` after the change.

## Files to create/modify
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs` — edit (delete `SearchUsersAsync` declaration, line 14)
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — edit (delete `SearchResultLimit` constant, line 25, and `SearchUsersAsync` method body, lines 192–266)
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs` — edit (delete `SearchUsersAsync` stub, lines 22–26)
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs` — deletion (entire file removed)

## Implementation steps
1. In `IGraphService.cs`, delete the `SearchUsersAsync` method declaration (line 14). Leave the interface's other two method declarations and their XML doc comments untouched.
2. In `GraphService.cs`, delete the `SearchResultLimit` constant (line 25) and the entire `SearchUsersAsync` method (lines 192–266, including its closing brace). Leave `GraphBatchSize`, `AcquireGraphTokenAsync`, `ParseMembersFromJson`, `GetGroupMembersAsync`, and `GetAppRoleMembersAsync` unchanged.
3. In `MockGraphService.cs`, delete the `SearchUsersAsync` stub method (lines 22–26). Leave `GetGroupMembersAsync` and `GetAppRoleMembersAsync` stubs unchanged.
4. Delete the file `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs` entirely.
5. Run a repository-wide search for `SearchUsersAsync` across `backend/src/**` and `backend/test/**` and confirm zero matches remain.
6. Run `dotnet build` and confirm the solution builds successfully with no new warnings or errors, and that `GraphService` and `MockGraphService` still compile as valid implementations of `IGraphService`.
7. Run `dotnet format` and confirm no new formatting issues are reported in the touched files.
8. Run the full backend test suite and confirm it passes, with the total test count reduced by exactly 5 (the deleted tests) and no other failures or newly-skipped tests.

## Tests to write
None — this is a pure deletion. The required verification is that the existing test suite still builds and passes after `GraphServiceSearchTests.cs` is removed; no new tests are needed. The only expected change in test output is a reduction of exactly 5 tests (the deleted `SearchUsersAsync_*` `[Fact]` methods), with no other failures or skips introduced.

## Acceptance criteria
- `IGraphService.cs` no longer declares `SearchUsersAsync`; `GetGroupMembersAsync` and `GetAppRoleMembersAsync` remain unchanged, including their XML doc comments.
- `GraphService.cs` no longer contains `SearchUsersAsync` or the `SearchResultLimit` constant; `GraphBatchSize` and all other members are untouched.
- `MockGraphService.cs` no longer contains `SearchUsersAsync`; `GetGroupMembersAsync` and `GetAppRoleMembersAsync` stubs are unchanged.
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs` no longer exists.
- `backend/test/Anela.Heblo.Tests/Helpers/FakeHttpMessageHandler.cs` is not modified or deleted.
- A repository-wide search for `SearchUsersAsync` returns zero matches in `backend/src/**` and `backend/test/**`.
- The solution builds successfully (`dotnet build`) with no new warnings or errors.
- `dotnet format` reports no new formatting issues in the touched files.
- The full backend test suite passes, with the total test count reduced by exactly 5 and no other test failures or newly-skipped tests.
