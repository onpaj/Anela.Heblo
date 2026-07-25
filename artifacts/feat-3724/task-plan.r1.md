# Task Plan: Fix silently-swallowed HTTP error in `GraphService.GetGroupMembersAsync`

## Goal

`GraphService.GetGroupMembersAsync` (`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs:140-150`) currently swallows non-success HTTP responses from Microsoft Graph (403, 404, 429, 500, etc.) by logging and returning `new List<UserDto>()`, instead of throwing `GraphServiceException` as `IGraphService`'s documented contract promises and as the sibling `ODataError` catch block (line 174) already does. This violates Liskov Substitution against the two callers (`GetGroupMembersHandler`, `GraphArticleUserResolver.ResolveByGroupAsync`) that already catch and correctly handle `GraphServiceException` — meaning real outages are currently indistinguishable from "the group has zero members."

## Architecture summary

- **Layers involved:** `Anela.Heblo.Adapters.Microsoft365` (infrastructure adapter implementing `IGraphService`) and its test project. No Application-layer, contract, or consumer changes — `GraphServiceException` already exists at `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceException.cs` and both consumers (`GetGroupMembersHandler`, `GraphArticleUserResolver.ResolveByGroupAsync`) already have working `catch (GraphServiceException ex)` blocks tested against the `ODataError` path.
- **Change shape:** replace one `return new List<UserDto>();` statement with a `throw new GraphServiceException(...)`, keeping the existing `_logger.LogError` and `_logger.LogDebug` calls untouched immediately above it. This makes the `!response.IsSuccessStatusCode` branch (line 140) structurally symmetric with the `catch (ODataError odataEx)` branch (line 174) three blocks below.
- **Blast radius:** exactly one production file (one branch, ~4 lines changed) plus one existing test (renamed and re-asserted), with an optional second test case for a different status code. `SearchUsersAsync`, `GetAppRoleMembersAsync`, and their own independent swallow-and-return-empty-list patterns are explicitly out of scope (NFR-1) — `GetAppRoleMembersAsync`'s internal call to `GetGroupMembersAsync` (line 383) sits inside its own top-level `catch (Exception ex)` (lines 448-452), so a newly-thrown `GraphServiceException` from a failing nested group still surfaces externally as an empty list, same as today — covered by regression (existing `GetAppRoleMembersAsync_*` tests must still pass unchanged).
- **Why one task:** this is a single-branch, single-file production change plus a single test-file update, with no design doc, no new types, no interface changes, and no independent sub-components to sequence — splitting it would be artificial busywork against the "surgical changes" / YAGNI principles in `CLAUDE.md`.

---

### task: fix-graph-service-swallow

**Context:** `GraphService.GetGroupMembersAsync` throws `GraphServiceException` on non-success HTTP responses from Microsoft Graph, instead of silently returning an empty list. This is the only task in this plan — it fully implements FR-1 and FR-2 from `artifacts/feat-3724/spec.r1.md`.

**Files:**
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` (modify lines 140-150)
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` (modify the test at lines 438-450, add one new test)

**Preconditions verified:**
- `GraphServiceException` constructor signature: `public GraphServiceException(string message, Exception innerException)` — `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceException.cs:11`.
- `GraphServiceTests.cs` already has `using Anela.Heblo.Application.Features.UserManagement.Contracts;` (line 4) and `using Anela.Heblo.Tests.Helpers;` (line 6), so `GraphServiceException` and `FakeHttpMessageHandler` are already in scope — no new `using` statements needed.
- `BuildService(...)` test helper (lines 66-90) wires a `FakeHttpMessageHandler` into a mocked `IHttpClientFactory` and returns a working `GraphService` — reused as-is.

#### Step 1 — Write the failing test (red)

Replace the existing test `GetGroupMembersAsync_GraphReturnsNonSuccess_ReturnsEmptyList` (lines 438-450) with the renamed, re-asserted version, and add a second status-code test case (429) as recommended by spec FR-2's optional acceptance criterion, asserting the exception message contains both the status code and the group id.

Edit `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`:

Replace:
```csharp
    [Fact]
    public async Task GetGroupMembersAsync_GraphReturnsNonSuccess_ReturnsEmptyList()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"Forbidden\"}}");
        var service = BuildService(handler, out _, out _, out _);

        // Act
        var result = await service.GetGroupMembersAsync("group-1");

        // Assert
        result.Should().BeEmpty();
    }
```

With:
```csharp
    [Fact]
    public async Task GetGroupMembersAsync_GraphReturnsNonSuccess_ThrowsGraphServiceException()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"Forbidden\"}}");
        var service = BuildService(handler, out _, out _, out _);

        // Act
        var ex = await Assert.ThrowsAsync<GraphServiceException>(() => service.GetGroupMembersAsync("group-1"));

        // Assert
        ex.Message.Should().Contain("403");
        ex.Message.Should().Contain("group-1");
        ex.InnerException.Should().NotBeNull();
    }

    [Fact]
    public async Task GetGroupMembersAsync_GraphReturnsTooManyRequests_ThrowsGraphServiceException()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":\"TooManyRequests\"}}");
        var service = BuildService(handler, out _, out _, out _);

        // Act
        var ex = await Assert.ThrowsAsync<GraphServiceException>(() => service.GetGroupMembersAsync("group-2"));

        // Assert
        ex.Message.Should().Contain("429");
        ex.Message.Should().Contain("group-2");
        ex.InnerException.Should().NotBeNull();
    }
```

Run the two new/changed tests and confirm they fail (red) against the current, unmodified `GraphService.cs` — the first because the current code returns `[]` instead of throwing, the second likewise:

```bash
cd /home/user/worktrees/feature-3724-Arch-Review-Usermanagement-Graphservice-Swallows-N/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphServiceTests.GetGroupMembersAsync_GraphReturnsNonSuccess_ThrowsGraphServiceException|FullyQualifiedName~GraphServiceTests.GetGroupMembersAsync_GraphReturnsTooManyRequests_ThrowsGraphServiceException"
```

Expected: both tests fail — `Assert.ThrowsAsync<GraphServiceException>` does not observe any exception (the method returns normally with an empty list).

#### Step 2 — Implement the fix (green)

Edit `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`.

Replace (lines 140-150):
```csharp
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Microsoft Graph API call failed for groupId: {GroupId}. Status: {StatusCode}, RequestUrl: {RequestUrl}, ResponseContent: {Content}",
                    groupId, response.StatusCode, requestUrl, errorContent);

                // Log response headers for troubleshooting
                _logger.LogDebug("Response headers: {@Headers}", response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)));

                return new List<UserDto>();
            }
```

With:
```csharp
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Microsoft Graph API call failed for groupId: {GroupId}. Status: {StatusCode}, RequestUrl: {RequestUrl}, ResponseContent: {Content}",
                    groupId, response.StatusCode, requestUrl, errorContent);

                // Log response headers for troubleshooting
                _logger.LogDebug("Response headers: {@Headers}", response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)));

                throw new GraphServiceException(
                    $"Microsoft Graph returned {(int)response.StatusCode} for group {groupId}.",
                    new HttpRequestException(errorContent));
            }
```

No `using` changes needed — `GraphServiceException` is in `Anela.Heblo.Application.Features.UserManagement.Contracts`, already imported at the top of `GraphService.cs` (line 1: `using Anela.Heblo.Application.Features.UserManagement.Contracts;`), and `HttpRequestException` is in `System.Net.Http`, already imported (line 8).

#### Step 3 — Run tests (green) and full regression sweep

```bash
cd /home/user/worktrees/feature-3724-Arch-Review-Usermanagement-Graphservice-Swallows-N/backend

# 1. The two tests targeted by this fix
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphServiceTests.GetGroupMembersAsync_GraphReturnsNonSuccess_ThrowsGraphServiceException|FullyQualifiedName~GraphServiceTests.GetGroupMembersAsync_GraphReturnsTooManyRequests_ThrowsGraphServiceException"

# 2. Full GraphServiceTests class — covers GetAppRoleMembersAsync_* regression per NFR-1,
#    cache-hit/cache-miss paths, MSAL/transport/disposal tests
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.UserManagement.GraphServiceTests"

# 3. Downstream consumer test files named in the spec (GetGroupMembersHandlerTests,
#    GraphArticleUserResolver tests) — confirm their existing catch(GraphServiceException)
#    paths still pass unchanged
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetGroupMembersHandlerTests|FullyQualifiedName~GraphArticleUserResolver"

# 4. Full solution build + full test suite (final gate before commit)
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
```

Expected: all tests pass; `dotnet format --verify-no-changes` reports no formatting diffs (if it does, run `dotnet format Anela.Heblo.sln` and re-verify, then re-check the diff only touches the two files above).

#### Step 4 — Commit

```bash
cd /home/user/worktrees/feature-3724-Arch-Review-Usermanagement-Graphservice-Swallows-N
git add backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs \
        backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs
git commit -m "$(cat <<'EOF'
Fix GraphService.GetGroupMembersAsync swallowing non-success HTTP responses

The !response.IsSuccessStatusCode branch returned an empty list on Graph
HTTP failures (403/404/429/5xx), silently indistinguishable from "group has
zero members" to callers already coded against the documented
GraphServiceException contract (GetGroupMembersHandler,
GraphArticleUserResolver.ResolveByGroupAsync). Now throws
GraphServiceException, matching the sibling ODataError catch block.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
git status
```

**Definition of done for this task:**
- `GetGroupMembersAsync`'s `!response.IsSuccessStatusCode` branch throws `GraphServiceException` wrapping `HttpRequestException(errorContent)`, with message `"Microsoft Graph returned {(int)response.StatusCode} for group {groupId}."`.
- The existing `_logger.LogError` and `_logger.LogDebug` calls in that branch are unchanged and still fire before the throw.
- No other line in `GetGroupMembersAsync`, `SearchUsersAsync`, or `GetAppRoleMembersAsync` changed.
- `GetGroupMembersAsync_GraphReturnsNonSuccess_ThrowsGraphServiceException` and `GetGroupMembersAsync_GraphReturnsTooManyRequests_ThrowsGraphServiceException` pass.
- Full `GraphServiceTests` class passes, including all `GetAppRoleMembersAsync_*` tests (NFR-1 regression).
- `GetGroupMembersHandlerTests` and any `GraphArticleUserResolver` tests pass unchanged.
- `dotnet build` and `dotnet format --verify-no-changes` are clean.
- One commit created containing exactly the two files listed above.
