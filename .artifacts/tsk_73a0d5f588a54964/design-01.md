# Design: Remove dead `IGraphService.SearchUsersAsync`

## UX/UI

Not applicable. This change has no user-facing surface — `SearchUsersAsync` was never wired to an API endpoint, MCP tool, or frontend hook, so there is no UI to design or affect.

## Component design

This is a pure subtraction from three existing components plus their test doubles. No new components, boundaries, or interfaces are introduced. Design is expressed as exact diffs against current source (verified live against the working tree, not the plan's line numbers, which had drifted slightly).

### 1. `IGraphService` (contract)

`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`

Remove line 14 only:

```diff
     Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default);
-    Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);
     Task<List<UserDto>> GetAppRoleMembersAsync(string appRoleValue, CancellationToken cancellationToken = default);
```

Resulting interface has exactly two members: `GetGroupMembersAsync`, `GetAppRoleMembersAsync`. Both remain documented by the existing `<exception>` XML doc comments above `GetGroupMembersAsync`, which are unaffected.

### 2. `GraphService` (real implementation)

`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`

Two deletions in this file:

- **Method body**: lines 192–266 (`public async Task<List<UserDto>> SearchUsersAsync(...) { ... }`), the full method including its `$search` query construction, quote-stripping, and error handling. Verified this method is not called from anywhere else in the file (`GetAppRoleMembersAsync` reuses `GetGroupMembersAsync`, not `SearchUsersAsync`).
- **Dead constant**: line 25, `private const int SearchResultLimit = 25;`. Verified by grep this constant has exactly one other reference (line 218, inside the method being deleted) — safe to remove as part of the same change, not left orphaned.

No other members of the class reference `SearchUsersAsync` or `SearchResultLimit`. `GetGroupMembersAsync` and `GetAppRoleMembersAsync` are untouched. No `using` directives become unused as a result (the deleted method used only types already used elsewhere in the file: `System.Text.Json`, `System.Net.Http`).

### 3. `MockGraphService` (test/DI double)

`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs`

Remove lines 22–26 (the `SearchUsersAsync` override) in full:

```diff
     public Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default)
     {
         _logger.LogInformation("Mock GraphService: GetGroupMembersAsync called for group {GroupId}", groupId);
         return Task.FromResult(new List<UserDto>());
     }

-    public Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
-    {
-        _logger.LogInformation("Mock GraphService: SearchUsersAsync called for query '{Query}'", query);
-        return Task.FromResult(new List<UserDto>());
-    }
-
     public Task<List<UserDto>> GetAppRoleMembersAsync(string appRoleValue, CancellationToken cancellationToken = default)
```

### 4. Test suite

`backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs` — delete the entire file (120 lines, 5 `[Fact]` tests, all exclusively exercising the method being removed: `SearchUsersAsync_BuildsSearchRequest_AndParsesUsers`, `SearchUsersAsync_NonSuccess_ReturnsEmpty`, `SearchUsersAsync_EmptyQuery_ReturnsEmpty_WithoutTouchingFactory`, `SearchUsersAsync_TokenFailure_ReturnsEmpty_WithoutTouchingFactory`, `SearchUsersAsync_StripsDoubleQuotesFromQuery`).

Verified (live grep, not just the plan's assumption) that no other test file references `SearchUsersAsync` — `MockGraphServiceTests.cs`, `GraphServiceTests.cs`, and `PhotobankGraphServiceThumbnailTests.cs` have zero matches. No cleanup needed there.

### Post-condition (verification interface)

A repo-wide grep for `SearchUsersAsync` after the change must return zero matches:

```
grep -rn "SearchUsersAsync" backend/ frontend/
```

Currently returns exactly 4 files (`IGraphService.cs`, `GraphService.cs`, `MockGraphService.cs`, `GraphServiceSearchTests.cs`) — all four are the files edited/deleted above, confirming the removal surface is fully enumerated and nothing was missed.

## Data schemas

None. `UserDto` (the shared return type of all three `IGraphService` methods) is unchanged — it remains used by the two surviving methods, `GetGroupMembersAsync` and `GetAppRoleMembersAsync`. No request/response contracts, DB schemas, or event payloads exist for this method today (that's the finding itself — no endpoint was ever built), so none are removed either.
