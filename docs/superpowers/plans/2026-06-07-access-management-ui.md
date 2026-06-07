# Access Management UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a full-scale admin UI for managing permission groups (CRUD), group↔permission and group↔group connections, and user↔group assignments with users loaded live from EntraID, on top of the existing RBAC backend.

**Architecture:** The backend RBAC slice already exists (entities, persistence, group CRUD API, claims transformation, cached resolver). This plan adds two backend capabilities — live Entra directory search and assign-groups-by-Entra-object-id (materialize on demand) — then replaces the minimal `AccessManagementPage.tsx` with object-centric editors (Groups + Users tabs). No database schema changes.

**Tech Stack:** .NET 8, MediatR, EF Core, Microsoft Graph (HTTP via `IHttpClientFactory` named client `MicrosoftGraph`), xUnit + FluentAssertions + Moq; React 18, TanStack Query, NSwag-generated fetch client, Tailwind, Jest + React Testing Library.

---

## Reference facts (verified against the codebase)

- **Permission value format:** `"{featureKey}.{level}"` where level ∈ `read|write|admin` (e.g. `administration.read`). Source: `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.cs:72-80`.
- **Catalogue shape** (`GetPermissionCatalogueResponse`): `Permissions: string[]`, `Features: { key, label, section, hasWrite, hasAdmin }[]`, `SystemGroups: { name, permissions[] }[]`.
- **Group editor request shape** (`CreateGroupRequest`/`UpdateGroupRequest`): `Name`, `Description?`, `Permissions: string[]`, `ParentGroupIds: Guid[]`.
- **Group detail** (`GroupDetailDto`): `Id, Name, Description?, IsSystem, Permissions: string[], ParentGroupIds: Guid[]`.
- **Graph DTO** (`UserDto`): `Id` (= Entra object id), `DisplayName`, `Email`.
- **Repository methods available:** `GetAllUsersAsync` (AsNoTracking + `Include(UserGroups)`), `GetUserByObjectIdAsync`, `AddUserAsync`, `GetAllGroupsAsync`, `SetUserGroupsAsync`, `SaveChangesAsync`. Source: `backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationRepository.cs`.
- **Error contract:** `BaseApiController.HandleResponse` returns the mapped HTTP status (e.g. 400/404/409) for `Success == false`. The NSwag fetch client therefore **throws** on error — either the typed `*Response` object (when a typed error result was generated) or a `SwaggerException` whose `.response` is the raw JSON body. Both carry `errorCode`.
- **MediatR handlers auto-register** via assembly scan (`ApplicationModule.cs:62`) — new handlers need no manual registration.
- **Existing route** `/admin/access` already renders `AccessManagementPage` gated by `RequireAccess requiredRole="administration.read"` (`frontend/src/App.tsx:490-494`). No `App.tsx` change needed.

## File structure (created / modified)

**Backend — created:**
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/SearchDirectoryUsers/SearchDirectoryUsersRequest.cs`
- `.../SearchDirectoryUsers/SearchDirectoryUsersResponse.cs` (+ `DirectoryUserDto`)
- `.../SearchDirectoryUsers/SearchDirectoryUsersHandler.cs`
- `.../AssignDirectoryUserGroups/AssignDirectoryUserGroupsRequest.cs`
- `.../AssignDirectoryUserGroups/AssignDirectoryUserGroupsResponse.cs`
- `.../AssignDirectoryUserGroups/AssignDirectoryUserGroupsHandler.cs`

**Backend — modified:**
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`
- `.../Services/GraphService.cs`
- `.../Services/MockGraphService.cs`
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`
- `backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs`

**Backend — tests created:**
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs`
- `backend/test/Anela.Heblo.Tests/Authorization/SearchDirectoryUsersHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Authorization/AssignDirectoryUserGroupsHandlerTests.cs`
- (modify) `backend/test/Anela.Heblo.Tests/Features/UserManagement/MockGraphServiceTests.cs`

**Frontend — created (feature folder `frontend/src/features/access-management/`):**
- `utils/permissionValues.ts`
- `utils/authzErrors.ts`
- `utils/useDebouncedValue.ts`
- `components/PermissionPicker.tsx`
- `components/ParentGroupSelect.tsx`
- `components/GroupEditorModal.tsx`
- `components/GroupsTab.tsx`
- `components/UsersTab.tsx`
- `__tests__/PermissionPicker.test.tsx`
- `__tests__/ParentGroupSelect.test.tsx`
- `__tests__/GroupsTab.test.tsx`
- `__tests__/UsersTab.test.tsx`

**Frontend — modified:**
- `frontend/src/api/hooks/useAccessManagement.ts` (add directory hooks)
- `frontend/src/pages/AccessManagementPage.tsx` (compose tabs)
- `frontend/src/api/generated/api-client.ts` (regenerated, not hand-edited)

---

# Phase A — Backend: Entra directory search

### Task 1: Add `SearchUsersAsync` to the Graph service

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs`:

```csharp
using System.Net;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.UserManagement;

public class GraphServiceSearchTests
{
    private const string MicrosoftGraphClientName = "MicrosoftGraph";

    private const string SampleUsersResponse = """
{
  "value": [
    { "id": "11111111-1111-1111-1111-111111111111", "displayName": "Alice Example", "mail": "alice@example.com", "userPrincipalName": "alice@example.com" },
    { "id": "22222222-2222-2222-2222-222222222222", "displayName": "Bob Example", "mail": null, "userPrincipalName": "bob@example.com" }
  ]
}
""";

    private static GraphService BuildService(HttpMessageHandler handler, out Mock<IHttpClientFactory> factoryMock)
    {
        factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(MicrosoftGraphClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var tokenMock = new Mock<ITokenAcquisition>();
        tokenMock.Setup(t => t.GetAccessTokenForAppAsync(
                "https://graph.microsoft.com/.default", It.IsAny<string?>(), It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("test-token");

        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<GraphService>>();
        return new GraphService(tokenMock.Object, cache, logger, factoryMock.Object);
    }

    [Fact]
    public async Task SearchUsersAsync_BuildsSearchRequest_AndParsesUsers()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SampleUsersResponse);
        var service = BuildService(handler, out var factoryMock);

        var result = await service.SearchUsersAsync("ali");

        factoryMock.Verify(f => f.CreateClient(MicrosoftGraphClientName), Times.Once);
        result.Should().HaveCount(2);
        result[0].Id.Should().Be("11111111-1111-1111-1111-111111111111");
        result[0].Email.Should().Be("alice@example.com");
        result[1].Email.Should().Be("bob@example.com"); // falls back to UPN when mail is null

        handler.LastRequestUri!.ToString().Should().Contain("https://graph.microsoft.com/v1.0/users?");
        handler.LastRequestUri!.ToString().Should().Contain("$search=");
        handler.LastRequestHeaders!.Contains("ConsistencyLevel").Should().BeTrue();
    }

    [Fact]
    public async Task SearchUsersAsync_NonSuccess_ReturnsEmpty()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"Forbidden\"}}");
        var service = BuildService(handler, out _);

        var result = await service.SearchUsersAsync("ali");

        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Verify the test references compile-fail / fail**

First confirm `FakeHttpMessageHandler` exposes `LastRequestHeaders`. Run:

```bash
grep -n "LastRequestHeaders\|LastRequestUri\|class FakeHttpMessageHandler" backend/test/Anela.Heblo.Tests/Helpers/FakeHttpMessageHandler.cs
```

Expected: `LastRequestUri` exists. If `LastRequestHeaders` does NOT exist, add it to the helper in this step:

```csharp
// In FakeHttpMessageHandler: capture request headers alongside the URI.
public System.Net.Http.Headers.HttpRequestHeaders? LastRequestHeaders { get; private set; }
// inside SendAsync, before returning: LastRequestHeaders = request.Headers;
```

Then run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GraphServiceSearchTests"
```

Expected: FAIL — `IGraphService` has no `SearchUsersAsync`.

- [ ] **Step 3: Add the interface method**

In `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`, add inside the interface:

```csharp
    Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement in `GraphService`**

In `GraphService.cs`, add this method to the class (after `GetGroupMembersAsync`):

```csharp
    private const int SearchResultLimit = 25;

    public async Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
    {
        var trimmed = (query ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return new List<UserDto>();
        }

        // Strip double quotes so user input cannot break the $search expression.
        var safe = trimmed.Replace("\"", string.Empty);

        string graphToken;
        try
        {
            graphToken = await _tokenAcquisition.GetAccessTokenForAppAsync("https://graph.microsoft.com/.default");
        }
        catch (Exception tokenEx)
        {
            _logger.LogError(tokenEx, "Failed to acquire Graph token for directory user search");
            return new List<UserDto>();
        }

        try
        {
            var searchExpr = Uri.EscapeDataString($"\"displayName:{safe}\" OR \"mail:{safe}\" OR \"userPrincipalName:{safe}\"");
            var requestUrl =
                $"https://graph.microsoft.com/v1.0/users?$search={searchExpr}&$select=id,displayName,mail,userPrincipalName&$top={SearchResultLimit}";

            var httpClient = _httpClientFactory.CreateClient("MicrosoftGraph");
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);
            request.Headers.Add("ConsistencyLevel", "eventual"); // required by Graph $search

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Graph directory search failed. Status: {StatusCode}, Body: {Content}",
                    response.StatusCode, errorContent);
                return new List<UserDto>();
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var jsonDocument = System.Text.Json.JsonDocument.Parse(responseContent);

            var users = new List<UserDto>();
            if (jsonDocument.RootElement.TryGetProperty("value", out var valueElement) &&
                valueElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in valueElement.EnumerateArray())
                {
                    var id = el.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                    var displayName = el.TryGetProperty("displayName", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                    var mail = el.TryGetProperty("mail", out var mailProp) ? mailProp.GetString() : null;
                    var upn = el.TryGetProperty("userPrincipalName", out var upnProp) ? upnProp.GetString() : null;

                    if (string.IsNullOrEmpty(id)) continue;

                    users.Add(new UserDto
                    {
                        Id = id,
                        DisplayName = displayName,
                        Email = mail ?? upn ?? string.Empty,
                    });
                }
            }

            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Graph directory user search");
            return new List<UserDto>();
        }
    }
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GraphServiceSearchTests"
```

Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs \
        backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs \
        backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs \
        backend/test/Anela.Heblo.Tests/Helpers/FakeHttpMessageHandler.cs
git commit -m "feat(authz): add Graph directory user search"
```

---

### Task 2: Implement `SearchUsersAsync` in `MockGraphService`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/MockGraphService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/UserManagement/MockGraphServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `MockGraphServiceTests.cs` (inside the existing test class):

```csharp
    [Fact]
    public async Task SearchUsersAsync_FiltersByQuery_CaseInsensitive()
    {
        var service = new MockGraphService(Mock.Of<ILogger<MockGraphService>>());

        var result = await service.SearchUsersAsync("admin");

        result.Should().ContainSingle();
        result[0].Email.Should().Be("mock.admin@anela-heblo.com");
    }

    [Fact]
    public async Task SearchUsersAsync_EmptyQuery_ReturnsAll()
    {
        var service = new MockGraphService(Mock.Of<ILogger<MockGraphService>>());

        var result = await service.SearchUsersAsync("");

        result.Should().HaveCount(3);
    }
```

Ensure the file has `using FluentAssertions; using Microsoft.Extensions.Logging; using Moq; using Xunit;` (add any missing).

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MockGraphServiceTests.SearchUsersAsync"
```

Expected: FAIL — `MockGraphService` has no `SearchUsersAsync`.

- [ ] **Step 3: Implement the method**

In `MockGraphService.cs`, refactor the mock list into a shared field and add the search method:

```csharp
    private static readonly List<UserDto> MockUsers = new()
    {
        new UserDto { Id = "mock-user-1", DisplayName = "Mock User 1", Email = "mock.user1@anela-heblo.com" },
        new UserDto { Id = "mock-user-2", DisplayName = "Mock User 2", Email = "mock.user2@anela-heblo.com" },
        new UserDto { Id = "mock-user-3", DisplayName = "Mock Administrator", Email = "mock.admin@anela-heblo.com" },
    };

    public Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Mock GraphService: directory search for '{Query}'", query);
        var trimmed = (query ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Task.FromResult(MockUsers.ToList());
        }

        var matches = MockUsers
            .Where(u => u.DisplayName.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                     || u.Email.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult(matches);
    }
```

Update the existing `GetGroupMembersAsync` to return `MockUsers.ToList()` instead of rebuilding the list (DRY), keeping behavior identical.

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MockGraphServiceTests"
```

Expected: PASS (existing + 2 new).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/UserManagement/Services/MockGraphService.cs \
        backend/test/Anela.Heblo.Tests/Features/UserManagement/MockGraphServiceTests.cs
git commit -m "feat(authz): mock directory user search"
```

---

### Task 3: Add `AuthorizationDirectorySearchTooShort` error code

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:401`

- [ ] **Step 1: Add the enum member**

In `ErrorCodes.cs`, immediately after `AuthorizationDuplicateGroupName = 3206,` add:

```csharp
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    AuthorizationDirectorySearchTooShort = 3207,
```

- [ ] **Step 2: Verify it compiles**

```bash
dotnet build Anela.Heblo.sln
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat(authz): add directory-search-too-short error code"
```

---

### Task 4: `SearchDirectoryUsers` use case

**Files:**
- Create: `.../UseCases/SearchDirectoryUsers/SearchDirectoryUsersRequest.cs`
- Create: `.../UseCases/SearchDirectoryUsers/SearchDirectoryUsersResponse.cs`
- Create: `.../UseCases/SearchDirectoryUsers/SearchDirectoryUsersHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/SearchDirectoryUsersHandlerTests.cs`

(All under `backend/src/Anela.Heblo.Application/Features/Authorization/`.)

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Authorization/SearchDirectoryUsersHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.SearchDirectoryUsers;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class SearchDirectoryUsersHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"dirsearch_{Guid.NewGuid()}").Options);

    private static IGraphService Graph(params UserDto[] users)
    {
        var mock = new Mock<IGraphService>();
        mock.Setup(g => g.SearchUsersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(users.ToList());
        return mock.Object;
    }

    [Fact]
    public async Task TooShortQuery_ReturnsError()
    {
        await using var db = NewDb();
        var handler = new SearchDirectoryUsersHandler(Graph(), new AuthorizationRepository(db));

        var result = await handler.Handle(new SearchDirectoryUsersRequest { Search = "a" }, default);

        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationDirectorySearchTooShort);
    }

    [Fact]
    public async Task MergesAppRecord_WhenUserExists()
    {
        await using var db = NewDb();
        var g = new PermissionGroup { Id = Guid.NewGuid(), Name = "G1", CreatedAt = DateTimeOffset.UtcNow };
        var user = new AppUser { Id = Guid.NewGuid(), EntraObjectId = "oid-1", Email = "u@x.cz", DisplayName = "U", IsActive = false, CreatedAt = DateTimeOffset.UtcNow };
        user.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = g.Id });
        db.AddRange(g, user);
        await db.SaveChangesAsync();

        var graph = Graph(
            new UserDto { Id = "oid-1", DisplayName = "U", Email = "u@x.cz" },
            new UserDto { Id = "oid-2", DisplayName = "New Person", Email = "new@x.cz" });
        var handler = new SearchDirectoryUsersHandler(graph, new AuthorizationRepository(db));

        var result = await handler.Handle(new SearchDirectoryUsersRequest { Search = "ne" }, default);

        result.Success.Should().BeTrue();
        result.Users.Should().HaveCount(2);
        var existing = result.Users.Single(u => u.EntraObjectId == "oid-1");
        existing.HasRecord.Should().BeTrue();
        existing.Id.Should().Be(user.Id);
        existing.IsActive.Should().BeFalse();
        existing.GroupIds.Should().BeEquivalentTo(new[] { g.Id });
        var fresh = result.Users.Single(u => u.EntraObjectId == "oid-2");
        fresh.HasRecord.Should().BeFalse();
        fresh.Id.Should().BeNull();
        fresh.IsActive.Should().BeTrue();
        fresh.GroupIds.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SearchDirectoryUsersHandlerTests"
```

Expected: FAIL — types do not exist yet.

- [ ] **Step 3: Create the request**

`SearchDirectoryUsersRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SearchDirectoryUsers;

public class SearchDirectoryUsersRequest : IRequest<SearchDirectoryUsersResponse>
{
    public string? Search { get; set; }
}
```

- [ ] **Step 4: Create the response + DTO**

`SearchDirectoryUsersResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SearchDirectoryUsers;

public class SearchDirectoryUsersResponse : BaseResponse
{
    public List<DirectoryUserDto> Users { get; set; } = new();
    public SearchDirectoryUsersResponse() { }
    public SearchDirectoryUsersResponse(ErrorCodes errorCode) : base(errorCode) { }
}

public class DirectoryUserDto
{
    public string EntraObjectId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public bool HasRecord { get; set; }
    public Guid? Id { get; set; }
    public bool IsActive { get; set; }
    public List<Guid> GroupIds { get; set; } = new();
}
```

- [ ] **Step 5: Create the handler**

`SearchDirectoryUsersHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SearchDirectoryUsers;

public class SearchDirectoryUsersHandler : IRequestHandler<SearchDirectoryUsersRequest, SearchDirectoryUsersResponse>
{
    public const int MinSearchLength = 2;

    private readonly IGraphService _graph;
    private readonly IAuthorizationRepository _repo;

    public SearchDirectoryUsersHandler(IGraphService graph, IAuthorizationRepository repo)
    {
        _graph = graph;
        _repo = repo;
    }

    public async Task<SearchDirectoryUsersResponse> Handle(SearchDirectoryUsersRequest request, CancellationToken ct)
    {
        var search = (request.Search ?? string.Empty).Trim();
        if (search.Length < MinSearchLength)
            return new SearchDirectoryUsersResponse(ErrorCodes.AuthorizationDirectorySearchTooShort);

        var directory = await _graph.SearchUsersAsync(search, ct);
        var appUsers = await _repo.GetAllUsersAsync(ct);
        var byObjectId = appUsers
            .Where(u => !string.IsNullOrEmpty(u.EntraObjectId))
            .GroupBy(u => u.EntraObjectId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return new SearchDirectoryUsersResponse
        {
            Users = directory.Select(d =>
            {
                byObjectId.TryGetValue(d.Id, out var existing);
                return new DirectoryUserDto
                {
                    EntraObjectId = d.Id,
                    DisplayName = d.DisplayName,
                    Email = d.Email,
                    HasRecord = existing is not null,
                    Id = existing?.Id,
                    IsActive = existing?.IsActive ?? true,
                    GroupIds = existing?.UserGroups.Select(ug => ug.GroupId).ToList() ?? new List<Guid>(),
                };
            }).OrderBy(u => u.DisplayName).ToList(),
        };
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SearchDirectoryUsersHandlerTests"
```

Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/SearchDirectoryUsers/ \
        backend/test/Anela.Heblo.Tests/Authorization/SearchDirectoryUsersHandlerTests.cs
git commit -m "feat(authz): search directory users use case"
```

---

# Phase B — Backend: Assign groups by Entra object id

### Task 5: `AssignDirectoryUserGroups` use case

**Files:**
- Create: `.../UseCases/AssignDirectoryUserGroups/AssignDirectoryUserGroupsRequest.cs`
- Create: `.../UseCases/AssignDirectoryUserGroups/AssignDirectoryUserGroupsResponse.cs`
- Create: `.../UseCases/AssignDirectoryUserGroups/AssignDirectoryUserGroupsHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Authorization/AssignDirectoryUserGroupsHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Authorization/AssignDirectoryUserGroupsHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.AssignDirectoryUserGroups;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class AssignDirectoryUserGroupsHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"dirassign_{Guid.NewGuid()}").Options);

    private static async Task<PermissionGroup> SeedGroup(ApplicationDbContext db)
    {
        var g = new PermissionGroup { Id = Guid.NewGuid(), Name = "G1", CreatedAt = DateTimeOffset.UtcNow };
        db.Add(g);
        await db.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task MaterializesNewUser_AndAssignsGroups()
    {
        await using var db = NewDb();
        var g = await SeedGroup(db);
        var resolver = new Mock<IPermissionResolver>();
        var handler = new AssignDirectoryUserGroupsHandler(new AuthorizationRepository(db), resolver.Object);

        var result = await handler.Handle(new AssignDirectoryUserGroupsRequest
        {
            EntraObjectId = "oid-new",
            DisplayName = "New Person",
            Email = "new@x.cz",
            GroupIds = new() { g.Id },
        }, default);

        result.Success.Should().BeTrue();
        var created = await db.AppUsers.Include(u => u.UserGroups).SingleAsync(u => u.EntraObjectId == "oid-new");
        created.DisplayName.Should().Be("New Person");
        created.Email.Should().Be("new@x.cz");
        created.IsActive.Should().BeTrue();
        created.UserGroups.Select(ug => ug.GroupId).Should().BeEquivalentTo(new[] { g.Id });
        resolver.Verify(r => r.InvalidateCache("oid-new"), Times.Once);
    }

    [Fact]
    public async Task ExistingUser_ReplacesGroups_NoDuplicateRecord()
    {
        await using var db = NewDb();
        var g = await SeedGroup(db);
        var user = new AppUser { Id = Guid.NewGuid(), EntraObjectId = "oid-1", Email = "u@x.cz", DisplayName = "U", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        db.Add(user);
        await db.SaveChangesAsync();
        var handler = new AssignDirectoryUserGroupsHandler(new AuthorizationRepository(db), Mock.Of<IPermissionResolver>());

        var result = await handler.Handle(new AssignDirectoryUserGroupsRequest
        {
            EntraObjectId = "oid-1", DisplayName = "U", Email = "u@x.cz", GroupIds = new() { g.Id },
        }, default);

        result.Success.Should().BeTrue();
        (await db.AppUsers.CountAsync(u => u.EntraObjectId == "oid-1")).Should().Be(1);
        (await db.UserGroups.CountAsync(ug => ug.UserId == user.Id)).Should().Be(1);
    }

    [Fact]
    public async Task UnknownGroupId_ReturnsGroupNotFound()
    {
        await using var db = NewDb();
        var handler = new AssignDirectoryUserGroupsHandler(new AuthorizationRepository(db), Mock.Of<IPermissionResolver>());

        var result = await handler.Handle(new AssignDirectoryUserGroupsRequest
        {
            EntraObjectId = "oid-x", GroupIds = new() { Guid.NewGuid() },
        }, default);

        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationGroupNotFound);
    }

    [Fact]
    public async Task EmptyEntraObjectId_ReturnsUserNotFound()
    {
        await using var db = NewDb();
        var handler = new AssignDirectoryUserGroupsHandler(new AuthorizationRepository(db), Mock.Of<IPermissionResolver>());

        var result = await handler.Handle(new AssignDirectoryUserGroupsRequest { EntraObjectId = "  ", GroupIds = new() }, default);

        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationUserNotFound);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AssignDirectoryUserGroupsHandlerTests"
```

Expected: FAIL — types do not exist yet.

- [ ] **Step 3: Create the request**

`AssignDirectoryUserGroupsRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AssignDirectoryUserGroups;

public class AssignDirectoryUserGroupsRequest : IRequest<AssignDirectoryUserGroupsResponse>
{
    public string EntraObjectId { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public List<Guid> GroupIds { get; set; } = new();
}
```

- [ ] **Step 4: Create the response**

`AssignDirectoryUserGroupsResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AssignDirectoryUserGroups;

public class AssignDirectoryUserGroupsResponse : BaseResponse
{
    public AssignDirectoryUserGroupsResponse() { }
    public AssignDirectoryUserGroupsResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 5: Create the handler**

`AssignDirectoryUserGroupsHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AssignDirectoryUserGroups;

public class AssignDirectoryUserGroupsHandler
    : IRequestHandler<AssignDirectoryUserGroupsRequest, AssignDirectoryUserGroupsResponse>
{
    private readonly IAuthorizationRepository _repo;
    private readonly IPermissionResolver _resolver;

    public AssignDirectoryUserGroupsHandler(IAuthorizationRepository repo, IPermissionResolver resolver)
    {
        _repo = repo;
        _resolver = resolver;
    }

    public async Task<AssignDirectoryUserGroupsResponse> Handle(AssignDirectoryUserGroupsRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EntraObjectId))
            return new AssignDirectoryUserGroupsResponse(ErrorCodes.AuthorizationUserNotFound);

        if (request.GroupIds.Count > 0)
        {
            var allGroups = await _repo.GetAllGroupsAsync(ct);
            var existingIds = allGroups.Select(g => g.Id).ToHashSet();
            if (request.GroupIds.Any(id => !existingIds.Contains(id)))
                return new AssignDirectoryUserGroupsResponse(ErrorCodes.AuthorizationGroupNotFound);
        }

        var user = await _repo.GetUserByObjectIdAsync(request.EntraObjectId, ct);
        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                EntraObjectId = request.EntraObjectId,
                Email = request.Email ?? request.EntraObjectId,
                DisplayName = request.DisplayName ?? request.Email ?? request.EntraObjectId,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await _repo.AddUserAsync(user, ct);
            await _repo.SaveChangesAsync(ct);
        }

        await _repo.SetUserGroupsAsync(user.Id, request.GroupIds, ct);
        await _repo.SaveChangesAsync(ct);
        _resolver.InvalidateCache(request.EntraObjectId);
        return new AssignDirectoryUserGroupsResponse();
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AssignDirectoryUserGroupsHandlerTests"
```

Expected: PASS (4 tests).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/AssignDirectoryUserGroups/ \
        backend/test/Anela.Heblo.Tests/Authorization/AssignDirectoryUserGroupsHandlerTests.cs
git commit -m "feat(authz): assign groups by Entra object id (materialize on demand)"
```

---

### Task 6: Controller endpoints

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs`

- [ ] **Step 1: Add `using` directives**

At the top of `AuthorizationController.cs`, add:

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.AssignDirectoryUserGroups;
using Anela.Heblo.Application.Features.Authorization.UseCases.SearchDirectoryUsers;
```

- [ ] **Step 2: Add the two endpoints**

Inside the controller class, before the closing brace, add:

```csharp
    [HttpGet("directory/users")]
    public async Task<ActionResult<SearchDirectoryUsersResponse>> SearchDirectory([FromQuery] string? search, CancellationToken ct)
        => HandleResponse(await _mediator.Send(new SearchDirectoryUsersRequest { Search = search }, ct));

    [HttpPut("directory/users/{entraObjectId}/groups")]
    [Authorize(Roles = AccessRoles.AdministrationWrite)]
    public async Task<ActionResult<AssignDirectoryUserGroupsResponse>> AssignDirectoryGroups(
        [FromRoute] string entraObjectId, [FromBody] AssignDirectoryUserGroupsRequest request, CancellationToken ct)
    {
        request.EntraObjectId = entraObjectId;
        return HandleResponse(await _mediator.Send(request, ct));
    }
```

- [ ] **Step 3: Build and run the full authz test suite**

```bash
dotnet build Anela.Heblo.sln && \
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Authorization|FullyQualifiedName~UserManagement"
```

Expected: Build succeeded; all tests PASS.

- [ ] **Step 4: Format and commit**

```bash
dotnet format Anela.Heblo.sln
git add backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs
git commit -m "feat(authz): directory search and assign endpoints"
```

---

# Phase C — Regenerate client + frontend hooks

### Task 7: Regenerate the TypeScript client and add directory hooks

**Files:**
- Modify (regenerated): `frontend/src/api/generated/api-client.ts`
- Modify: `frontend/src/api/hooks/useAccessManagement.ts`

- [ ] **Step 1: Regenerate the client**

```bash
cd frontend && npm run generate-client && cd ..
```

Expected: `api-client.ts` regenerated. Verify the new methods exist:

```bash
grep -n "authorization_SearchDirectory\|authorization_AssignDirectoryGroups\|class DirectoryUserDto\|class AssignDirectoryUserGroupsRequest" frontend/src/api/generated/api-client.ts | head
```

Expected: all four symbols present. (If method names differ, note the actual generated names and use them in Step 2 and downstream tasks.)

- [ ] **Step 2: Add hooks**

In `frontend/src/api/hooks/useAccessManagement.ts`:

Add to the type imports block (the `import type { ... }` list):

```typescript
  SearchDirectoryUsersResponse,
  AssignDirectoryUserGroupsResponse,
```

Add to the value imports block (the second `import { ... }`):

```typescript
  AssignDirectoryUserGroupsRequest,
```

Add `directory` to the `keys` object:

```typescript
  directory: (search: string) => ["authz", "directory", search] as const,
```

Append these hooks before the final `export type { ... }` line:

```typescript
export const useSearchDirectory = (search: string) => {
  const trimmed = search.trim();
  return useQuery({
    queryKey: keys.directory(trimmed),
    enabled: trimmed.length >= 2,
    queryFn: async (): Promise<SearchDirectoryUsersResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_SearchDirectory(trimmed);
    },
  });
};

export const useAssignDirectoryUserGroups = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      entraObjectId,
      request,
    }: {
      entraObjectId: string;
      request: AssignDirectoryUserGroupsRequest;
    }): Promise<AssignDirectoryUserGroupsResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_AssignDirectoryGroups(entraObjectId, request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["authz", "directory"] });
    },
  });
};
```

Add `AssignDirectoryUserGroupsRequest` to the final re-export:

```typescript
export type { CreateGroupRequest, UpdateGroupRequest, AssignUserGroupsRequest, SetUserActiveRequest, AssignDirectoryUserGroupsRequest };
```

- [ ] **Step 3: Type-check via lint**

```bash
cd frontend && npm run lint && cd ..
```

Expected: no errors in `useAccessManagement.ts`.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/generated/api-client.ts frontend/src/api/hooks/useAccessManagement.ts
git commit -m "feat(authz): regenerate client and add directory hooks"
```

---

# Phase D — Frontend components

### Task 8: Permission-value + error utilities

**Files:**
- Create: `frontend/src/features/access-management/utils/permissionValues.ts`
- Create: `frontend/src/features/access-management/utils/authzErrors.ts`
- Create: `frontend/src/features/access-management/utils/useDebouncedValue.ts`

- [ ] **Step 1: Create `permissionValues.ts`**

```typescript
export const PERMISSION_LEVELS = ["read", "write", "admin"] as const;
export type PermissionLevel = (typeof PERMISSION_LEVELS)[number];

export const PERMISSION_LEVEL_LABEL: Record<PermissionLevel, string> = {
  read: "Read",
  write: "Write",
  admin: "Admin",
};

export const permissionValue = (featureKey: string, level: PermissionLevel): string =>
  `${featureKey}.${level}`;
```

- [ ] **Step 2: Create `authzErrors.ts`**

```typescript
import { ErrorCodes } from "../../../api/generated/api-client";

// Errors thrown by the NSwag fetch client are either a typed *Response object
// (carrying `errorCode`) or a SwaggerException whose `.response` is the raw JSON body.
function extractErrorCode(error: unknown): string | undefined {
  if (error && typeof error === "object") {
    const e = error as { errorCode?: string; response?: string };
    if (typeof e.errorCode === "string") return e.errorCode;
    if (typeof e.response === "string") {
      try {
        return (JSON.parse(e.response) as { errorCode?: string })?.errorCode;
      } catch {
        return undefined;
      }
    }
  }
  return undefined;
}

export function getAuthzErrorMessage(error: unknown): string {
  switch (extractErrorCode(error)) {
    case ErrorCodes.AuthorizationDuplicateGroupName:
      return "A group with this name already exists.";
    case ErrorCodes.AuthorizationGroupCycleDetected:
      return "These parent groups would create a cycle.";
    case ErrorCodes.AuthorizationInvalidPermission:
      return "One or more selected permissions are invalid.";
    case ErrorCodes.AuthorizationSystemGroupImmutable:
      return "System groups cannot be modified.";
    case ErrorCodes.AuthorizationGroupNotFound:
      return "Group not found.";
    case ErrorCodes.AuthorizationUserNotFound:
      return "User not found.";
    case ErrorCodes.AuthorizationDirectorySearchTooShort:
      return "Type at least 2 characters to search.";
    default:
      return "Something went wrong. Please try again.";
  }
}
```

- [ ] **Step 3: Create `useDebouncedValue.ts`**

```typescript
import { useEffect, useState } from "react";

export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState<T>(value);
  useEffect(() => {
    const handle = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(handle);
  }, [value, delayMs]);
  return debounced;
}
```

- [ ] **Step 4: Lint and commit**

```bash
cd frontend && npm run lint && cd ..
git add frontend/src/features/access-management/utils/
git commit -m "feat(authz-ui): permission-value and error utilities"
```

---

### Task 9: `PermissionPicker` component

**Files:**
- Create: `frontend/src/features/access-management/components/PermissionPicker.tsx`
- Test: `frontend/src/features/access-management/__tests__/PermissionPicker.test.tsx`

- [ ] **Step 1: Write the failing test**

`__tests__/PermissionPicker.test.tsx`:

```typescript
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { PermissionPicker, PickerFeature } from "../components/PermissionPicker";

const features: PickerFeature[] = [
  { key: "catalog", label: "Katalog", section: "Produkty", hasWrite: true, hasAdmin: false },
  { key: "financial_overview", label: "Finanční přehled", section: "Finance", hasWrite: false, hasAdmin: false },
];

describe("PermissionPicker", () => {
  it("renders Write only for features that support it", () => {
    render(<PermissionPicker features={features} selected={[]} onChange={() => {}} />);
    // catalog row has Read + Write; financial_overview has Read only
    expect(screen.getByLabelText("catalog.write")).toBeInTheDocument();
    expect(screen.queryByLabelText("financial_overview.write")).toBeNull();
  });

  it("adds a value when a level is checked", () => {
    const onChange = jest.fn();
    render(<PermissionPicker features={features} selected={[]} onChange={onChange} />);
    fireEvent.click(screen.getByLabelText("catalog.read"));
    expect(onChange).toHaveBeenCalledWith(["catalog.read"]);
  });

  it("removes a value when an active level is unchecked", () => {
    const onChange = jest.fn();
    render(<PermissionPicker features={features} selected={["catalog.read"]} onChange={onChange} />);
    fireEvent.click(screen.getByLabelText("catalog.read"));
    expect(onChange).toHaveBeenCalledWith([]);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/features/access-management/__tests__/PermissionPicker.test.tsx; cd ..
```

Expected: FAIL — module not found.

- [ ] **Step 3: Implement the component**

`components/PermissionPicker.tsx`:

```typescript
import React from "react";
import {
  PERMISSION_LEVELS,
  PERMISSION_LEVEL_LABEL,
  PermissionLevel,
  permissionValue,
} from "../utils/permissionValues";

export interface PickerFeature {
  key: string;
  label: string;
  section: string;
  hasWrite: boolean;
  hasAdmin: boolean;
}

interface PermissionPickerProps {
  features: PickerFeature[];
  selected: string[];
  onChange: (next: string[]) => void;
  disabled?: boolean;
}

function featureSupportsLevel(feature: PickerFeature, level: PermissionLevel): boolean {
  if (level === "read") return true;
  if (level === "write") return feature.hasWrite;
  return feature.hasAdmin;
}

export function PermissionPicker({ features, selected, onChange, disabled }: PermissionPickerProps) {
  const selectedSet = new Set(selected);

  const toggle = (value: string) => {
    const next = new Set(selectedSet);
    if (next.has(value)) {
      next.delete(value);
    } else {
      next.add(value);
    }
    onChange(Array.from(next));
  };

  const sections = features.reduce<Record<string, PickerFeature[]>>((acc, f) => {
    (acc[f.section] ??= []).push(f);
    return acc;
  }, {});

  return (
    <div className="space-y-4">
      {Object.entries(sections).map(([section, sectionFeatures]) => (
        <div key={section}>
          <h4 className="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-1">{section}</h4>
          <div className="divide-y divide-gray-100">
            {sectionFeatures.map((feature) => (
              <div key={feature.key} className="flex items-center justify-between py-1.5">
                <span className="text-sm text-gray-800">{feature.label}</span>
                <div className="flex gap-3">
                  {PERMISSION_LEVELS.filter((level) => featureSupportsLevel(feature, level)).map((level) => {
                    const value = permissionValue(feature.key, level);
                    return (
                      <label key={value} className="flex items-center gap-1 text-xs text-gray-600">
                        <input
                          type="checkbox"
                          aria-label={value}
                          disabled={disabled}
                          checked={selectedSet.has(value)}
                          onChange={() => toggle(value)}
                        />
                        {PERMISSION_LEVEL_LABEL[level]}
                      </label>
                    );
                  })}
                </div>
              </div>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/features/access-management/__tests__/PermissionPicker.test.tsx; cd ..
```

Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/access-management/components/PermissionPicker.tsx \
        frontend/src/features/access-management/__tests__/PermissionPicker.test.tsx
git commit -m "feat(authz-ui): permission picker component"
```

---

### Task 10: `ParentGroupSelect` component

**Files:**
- Create: `frontend/src/features/access-management/components/ParentGroupSelect.tsx`
- Test: `frontend/src/features/access-management/__tests__/ParentGroupSelect.test.tsx`

- [ ] **Step 1: Write the failing test**

`__tests__/ParentGroupSelect.test.tsx`:

```typescript
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { ParentGroupSelect, GroupOption } from "../components/ParentGroupSelect";

const groups: GroupOption[] = [
  { id: "a", name: "Group A" },
  { id: "b", name: "Group B" },
];

describe("ParentGroupSelect", () => {
  it("excludes the group being edited", () => {
    render(<ParentGroupSelect groups={groups} selectedIds={[]} onChange={() => {}} excludeId="a" />);
    expect(screen.queryByLabelText("Group A")).toBeNull();
    expect(screen.getByLabelText("Group B")).toBeInTheDocument();
  });

  it("toggles a parent id", () => {
    const onChange = jest.fn();
    render(<ParentGroupSelect groups={groups} selectedIds={[]} onChange={onChange} />);
    fireEvent.click(screen.getByLabelText("Group A"));
    expect(onChange).toHaveBeenCalledWith(["a"]);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/features/access-management/__tests__/ParentGroupSelect.test.tsx; cd ..
```

Expected: FAIL — module not found.

- [ ] **Step 3: Implement the component**

`components/ParentGroupSelect.tsx`:

```typescript
import React from "react";

export interface GroupOption {
  id: string;
  name: string;
}

interface ParentGroupSelectProps {
  groups: GroupOption[];
  selectedIds: string[];
  onChange: (next: string[]) => void;
  excludeId?: string;
  disabled?: boolean;
}

export function ParentGroupSelect({ groups, selectedIds, onChange, excludeId, disabled }: ParentGroupSelectProps) {
  const selectedSet = new Set(selectedIds);
  const options = groups.filter((g) => g.id !== excludeId);

  const toggle = (id: string) => {
    const next = new Set(selectedSet);
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    onChange(Array.from(next));
  };

  if (options.length === 0) {
    return <p className="text-sm text-gray-400">No other groups available.</p>;
  }

  return (
    <div className="space-y-1">
      {options.map((g) => (
        <label key={g.id} className="flex items-center gap-2 text-sm text-gray-800">
          <input
            type="checkbox"
            aria-label={g.name}
            disabled={disabled}
            checked={selectedSet.has(g.id)}
            onChange={() => toggle(g.id)}
          />
          {g.name}
        </label>
      ))}
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/features/access-management/__tests__/ParentGroupSelect.test.tsx; cd ..
```

Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/access-management/components/ParentGroupSelect.tsx \
        frontend/src/features/access-management/__tests__/ParentGroupSelect.test.tsx
git commit -m "feat(authz-ui): parent group select component"
```

---

### Task 11: `GroupEditorModal` component

**Files:**
- Create: `frontend/src/features/access-management/components/GroupEditorModal.tsx`

This component composes `PermissionPicker` + `ParentGroupSelect`, fetches catalogue/groups/detail, and saves via create/update hooks. It is exercised by the `GroupsTab` test in Task 12 (which mocks the hooks), so no standalone test file is required here.

- [ ] **Step 1: Implement the component**

`components/GroupEditorModal.tsx`:

```typescript
import React, { useEffect, useState } from "react";
import {
  useCatalogue,
  useGroups,
  useGroup,
  useCreateGroup,
  useUpdateGroup,
} from "../../../api/hooks/useAccessManagement";
import { CreateGroupRequest, UpdateGroupRequest } from "../../../api/generated/api-client";
import { PermissionPicker, PickerFeature } from "./PermissionPicker";
import { ParentGroupSelect, GroupOption } from "./ParentGroupSelect";
import { getAuthzErrorMessage } from "../utils/authzErrors";

interface GroupEditorModalProps {
  groupId: string | null; // null = create
  onClose: () => void;
}

export function GroupEditorModal({ groupId, onClose }: GroupEditorModalProps) {
  const isEdit = groupId !== null;
  const catalogue = useCatalogue();
  const groups = useGroups();
  const detail = useGroup(groupId);
  const createGroup = useCreateGroup();
  const updateGroup = useUpdateGroup();

  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [permissions, setPermissions] = useState<string[]>([]);
  const [parentGroupIds, setParentGroupIds] = useState<string[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const loadedGroup = detail.data?.group;
  const isSystem = loadedGroup?.isSystem ?? false;

  useEffect(() => {
    if (isEdit && loadedGroup) {
      setName(loadedGroup.name ?? "");
      setDescription(loadedGroup.description ?? "");
      setPermissions(loadedGroup.permissions ?? []);
      setParentGroupIds((loadedGroup.parentGroupIds ?? []).map(String));
    }
  }, [isEdit, loadedGroup]);

  const features: PickerFeature[] = (catalogue.data?.features ?? []).map((f) => ({
    key: f.key ?? "",
    label: f.label ?? "",
    section: f.section ?? "",
    hasWrite: f.hasWrite ?? false,
    hasAdmin: f.hasAdmin ?? false,
  }));
  const groupOptions: GroupOption[] = (groups.data?.groups ?? []).map((g) => ({
    id: String(g.id),
    name: g.name ?? "",
  }));

  const isLoading = catalogue.isLoading || groups.isLoading || (isEdit && detail.isLoading);
  const isSaving = createGroup.isPending || updateGroup.isPending;

  const handleSave = async () => {
    setErrorMessage(null);
    try {
      if (isEdit && groupId) {
        await updateGroup.mutateAsync({
          id: groupId,
          request: new UpdateGroupRequest({
            id: groupId,
            name,
            description: description || undefined,
            permissions,
            parentGroupIds,
          }),
        });
      } else {
        await createGroup.mutateAsync(
          new CreateGroupRequest({
            name,
            description: description || undefined,
            permissions,
            parentGroupIds,
          }),
        );
      }
      onClose();
    } catch (error: unknown) {
      setErrorMessage(getAuthzErrorMessage(error));
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center bg-black/30 p-6 overflow-y-auto">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-2xl p-6 space-y-4">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold text-gray-900">
            {isEdit ? (isSystem ? "Group (system, read-only)" : "Edit group") : "New group"}
          </h2>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600" aria-label="Close">✕</button>
        </div>

        {isLoading ? (
          <div className="text-gray-500">Loading…</div>
        ) : (
          <>
            {errorMessage && (
              <div className="text-sm text-red-600 bg-red-50 border border-red-200 rounded p-2">{errorMessage}</div>
            )}

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Name</label>
              <input
                value={name}
                disabled={isSystem}
                onChange={(e) => setName(e.target.value)}
                className="w-full border border-gray-300 rounded px-3 py-2 text-sm disabled:bg-gray-100"
              />
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
              <input
                value={description}
                disabled={isSystem}
                onChange={(e) => setDescription(e.target.value)}
                className="w-full border border-gray-300 rounded px-3 py-2 text-sm disabled:bg-gray-100"
              />
            </div>

            <div>
              <h3 className="text-sm font-medium text-gray-700 mb-2">Permissions</h3>
              <PermissionPicker
                features={features}
                selected={permissions}
                onChange={setPermissions}
                disabled={isSystem}
              />
            </div>

            <div>
              <h3 className="text-sm font-medium text-gray-700 mb-2">Parent groups</h3>
              <ParentGroupSelect
                groups={groupOptions}
                selectedIds={parentGroupIds}
                onChange={setParentGroupIds}
                excludeId={groupId ?? undefined}
                disabled={isSystem}
              />
            </div>

            <div className="flex justify-end gap-2 pt-2">
              <button onClick={onClose} className="px-4 py-2 text-sm rounded bg-gray-100 text-gray-700">
                {isSystem ? "Close" : "Cancel"}
              </button>
              {!isSystem && (
                <button
                  onClick={handleSave}
                  disabled={isSaving || name.trim() === ""}
                  className="px-4 py-2 text-sm rounded bg-indigo-600 text-white disabled:opacity-50"
                >
                  {isSaving ? "Saving…" : "Save"}
                </button>
              )}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Lint and commit**

```bash
cd frontend && npm run lint && cd ..
git add frontend/src/features/access-management/components/GroupEditorModal.tsx
git commit -m "feat(authz-ui): group editor modal"
```

---

### Task 12: `GroupsTab` component

**Files:**
- Create: `frontend/src/features/access-management/components/GroupsTab.tsx`
- Test: `frontend/src/features/access-management/__tests__/GroupsTab.test.tsx`

- [ ] **Step 1: Write the failing test**

`__tests__/GroupsTab.test.tsx` (mocks the hooks module):

```typescript
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { GroupsTab } from "../components/GroupsTab";
import * as hooks from "../../../api/hooks/useAccessManagement";

jest.mock("../../../api/hooks/useAccessManagement");

const mocked = hooks as jest.Mocked<typeof hooks>;

function stubQuery<T>(data: T) {
  return { data, isLoading: false } as unknown as ReturnType<typeof hooks.useGroups>;
}
function stubMutation() {
  return { mutate: jest.fn(), isPending: false } as unknown as ReturnType<typeof hooks.useDeleteGroup>;
}

beforeEach(() => {
  mocked.useGroups.mockReturnValue(
    stubQuery({
      groups: [
        { id: "1", name: "Spravce", isSystem: true, permissionCount: 50, parentCount: 0, memberCount: 2 },
        { id: "2", name: "Custom", isSystem: false, permissionCount: 3, parentCount: 1, memberCount: 0 },
      ],
    }) as any,
  );
  mocked.useDeleteGroup.mockReturnValue(stubMutation() as any);
  // catalogue/group hooks are only used by the modal; provide minimal stubs.
  mocked.useCatalogue.mockReturnValue(stubQuery({ features: [], permissions: [], systemGroups: [] }) as any);
  mocked.useGroup.mockReturnValue(stubQuery({ group: undefined }) as any);
  mocked.useCreateGroup.mockReturnValue(stubMutation() as any);
  mocked.useUpdateGroup.mockReturnValue(stubMutation() as any);
});

describe("GroupsTab", () => {
  it("renders groups with a system badge and hides delete for system groups", () => {
    render(<GroupsTab />);
    expect(screen.getByText("Spravce")).toBeInTheDocument();
    expect(screen.getByText("system")).toBeInTheDocument();
    expect(screen.queryByLabelText("Delete Spravce")).toBeNull();
    expect(screen.getByLabelText("Delete Custom")).toBeInTheDocument();
  });

  it("opens the editor when New group is clicked", () => {
    render(<GroupsTab />);
    fireEvent.click(screen.getByText("New group"));
    expect(screen.getByText(/New group/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/features/access-management/__tests__/GroupsTab.test.tsx; cd ..
```

Expected: FAIL — module not found.

- [ ] **Step 3: Implement the component**

`components/GroupsTab.tsx`:

```typescript
import React, { useState } from "react";
import { useGroups, useDeleteGroup } from "../../../api/hooks/useAccessManagement";
import { GroupEditorModal } from "./GroupEditorModal";

// undefined = closed, null = create, string = edit that group id
type EditorState = string | null | undefined;

export function GroupsTab() {
  const groups = useGroups();
  const deleteGroup = useDeleteGroup();
  const [editor, setEditor] = useState<EditorState>(undefined);

  return (
    <div className="space-y-3">
      <div className="flex justify-end">
        <button
          onClick={() => setEditor(null)}
          className="px-3 py-1.5 text-sm rounded bg-indigo-600 text-white"
        >
          New group
        </button>
      </div>

      {groups.isLoading && <div className="text-gray-500">Loading groups…</div>}

      {groups.data?.groups?.map((g) => (
        <div key={g.id} className="flex items-center justify-between bg-white border border-gray-200 rounded-lg p-4">
          <div>
            <div className="flex items-center gap-2">
              <span className="font-medium text-gray-900">{g.name}</span>
              {g.isSystem && <span className="text-xs bg-gray-100 text-gray-700 px-2 py-0.5 rounded">system</span>}
            </div>
            <p className="text-sm text-gray-500">
              {g.permissionCount} permissions · {g.parentCount} parents · {g.memberCount} members
            </p>
          </div>
          <div className="flex items-center gap-3">
            <button
              onClick={() => g.id && setEditor(String(g.id))}
              className="text-sm text-indigo-600 hover:underline"
              aria-label={`${g.isSystem ? "View" : "Edit"} ${g.name}`}
            >
              {g.isSystem ? "View" : "Edit"}
            </button>
            {!g.isSystem && (
              <button
                onClick={() => g.id && deleteGroup.mutate(String(g.id))}
                disabled={deleteGroup.isPending}
                className="text-sm text-red-600 hover:underline"
                aria-label={`Delete ${g.name}`}
              >
                Delete
              </button>
            )}
          </div>
        </div>
      ))}

      {editor !== undefined && (
        <GroupEditorModal groupId={editor} onClose={() => setEditor(undefined)} />
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/features/access-management/__tests__/GroupsTab.test.tsx; cd ..
```

Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/access-management/components/GroupsTab.tsx \
        frontend/src/features/access-management/__tests__/GroupsTab.test.tsx
git commit -m "feat(authz-ui): groups tab"
```

---

### Task 13: `UsersTab` component (directory search + assignment)

**Files:**
- Create: `frontend/src/features/access-management/components/UsersTab.tsx`
- Test: `frontend/src/features/access-management/__tests__/UsersTab.test.tsx`

- [ ] **Step 1: Write the failing test**

`__tests__/UsersTab.test.tsx`:

```typescript
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { UsersTab } from "../components/UsersTab";
import * as hooks from "../../../api/hooks/useAccessManagement";

jest.mock("../../../api/hooks/useAccessManagement");
const mocked = hooks as jest.Mocked<typeof hooks>;

const assignMutate = jest.fn();

beforeEach(() => {
  assignMutate.mockReset();
  mocked.useGroups.mockReturnValue({
    data: { groups: [{ id: "g1", name: "Group One", isSystem: false }] },
    isLoading: false,
  } as any);
  mocked.useSearchDirectory.mockReturnValue({
    data: {
      users: [
        { entraObjectId: "oid-1", displayName: "Alice", email: "alice@x.cz", hasRecord: false, isActive: true, groupIds: [] },
      ],
    },
    isLoading: false,
  } as any);
  mocked.useAssignDirectoryUserGroups.mockReturnValue({ mutateAsync: assignMutate, isPending: false } as any);
  mocked.useSetUserActive.mockReturnValue({ mutate: jest.fn(), isPending: false } as any);
  assignMutate.mockResolvedValue({});
});

describe("UsersTab", () => {
  it("shows directory results after typing", () => {
    render(<UsersTab />);
    fireEvent.change(screen.getByPlaceholderText(/Search directory/i), { target: { value: "ali" } });
    expect(screen.getByText("Alice")).toBeInTheDocument();
    expect(screen.getByText("alice@x.cz")).toBeInTheDocument();
  });

  it("assigns groups for the selected user", async () => {
    render(<UsersTab />);
    fireEvent.change(screen.getByPlaceholderText(/Search directory/i), { target: { value: "ali" } });
    fireEvent.click(screen.getByText("Alice"));
    fireEvent.click(screen.getByLabelText("Group One"));
    fireEvent.click(screen.getByText("Save assignment"));
    expect(assignMutate).toHaveBeenCalledWith(
      expect.objectContaining({ entraObjectId: "oid-1" }),
    );
  });
});
```

Note: this test relies on `useSearchDirectory` being called with the live (un-debounced) input. To make the component testable without timers, `UsersTab` passes the **raw** input to `useSearchDirectory` but debounces only the query-key via `useDebouncedValue`; see the implementation, which calls `useSearchDirectory(debouncedSearch)` where `debouncedSearch` initializes to the input synchronously on first render. Because the mock ignores the argument, the rendered results come from the mock regardless of debounce timing.

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/features/access-management/__tests__/UsersTab.test.tsx; cd ..
```

Expected: FAIL — module not found.

- [ ] **Step 3: Implement the component**

`components/UsersTab.tsx`:

```typescript
import React, { useState } from "react";
import {
  useGroups,
  useSearchDirectory,
  useAssignDirectoryUserGroups,
  useSetUserActive,
} from "../../../api/hooks/useAccessManagement";
import {
  AssignDirectoryUserGroupsRequest,
  DirectoryUserDto,
  SetUserActiveRequest,
} from "../../../api/generated/api-client";
import { useDebouncedValue } from "../utils/useDebouncedValue";
import { getAuthzErrorMessage } from "../utils/authzErrors";

const SEARCH_DEBOUNCE_MS = 300;

export function UsersTab() {
  const [search, setSearch] = useState("");
  const debouncedSearch = useDebouncedValue(search, SEARCH_DEBOUNCE_MS);
  const directory = useSearchDirectory(debouncedSearch);
  const groups = useGroups();
  const assign = useAssignDirectoryUserGroups();
  const setActive = useSetUserActive();

  const [selected, setSelected] = useState<DirectoryUserDto | null>(null);
  const [selectedGroupIds, setSelectedGroupIds] = useState<string[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const selectUser = (user: DirectoryUserDto) => {
    setSelected(user);
    setSelectedGroupIds((user.groupIds ?? []).map(String));
    setErrorMessage(null);
  };

  const toggleGroup = (id: string) => {
    setSelectedGroupIds((prev) =>
      prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id],
    );
  };

  const handleSave = async () => {
    if (!selected?.entraObjectId) return;
    setErrorMessage(null);
    try {
      await assign.mutateAsync({
        entraObjectId: selected.entraObjectId,
        request: new AssignDirectoryUserGroupsRequest({
          entraObjectId: selected.entraObjectId,
          displayName: selected.displayName,
          email: selected.email,
          groupIds: selectedGroupIds,
        }),
      });
      setSelected(null);
    } catch (error: unknown) {
      setErrorMessage(getAuthzErrorMessage(error));
    }
  };

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
      <div className="space-y-3">
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search directory by name or email…"
          className="w-full border border-gray-300 rounded px-3 py-2 text-sm"
        />
        {search.trim().length < 2 && (
          <p className="text-xs text-gray-400">Type at least 2 characters.</p>
        )}
        {directory.isLoading && <div className="text-gray-500">Searching…</div>}
        {directory.data?.users?.map((u) => (
          <button
            key={u.entraObjectId}
            onClick={() => selectUser(u)}
            className={`w-full text-left bg-white border rounded-lg p-3 ${
              selected?.entraObjectId === u.entraObjectId ? "border-indigo-500" : "border-gray-200"
            }`}
          >
            <div className="font-medium text-gray-900">{u.displayName}</div>
            <div className="text-sm text-gray-500">
              {u.email} · {u.groupIds?.length ?? 0} groups
              {u.hasRecord && !u.isActive && <span className="ml-2 text-red-600">disabled</span>}
            </div>
          </button>
        ))}
      </div>

      <div>
        {selected ? (
          <div className="bg-white border border-gray-200 rounded-lg p-4 space-y-3">
            <div>
              <div className="font-medium text-gray-900">{selected.displayName}</div>
              <div className="text-sm text-gray-500">{selected.email}</div>
            </div>

            {errorMessage && (
              <div className="text-sm text-red-600 bg-red-50 border border-red-200 rounded p-2">{errorMessage}</div>
            )}

            <div className="space-y-1">
              {groups.data?.groups?.map((g) => (
                <label key={g.id} className="flex items-center gap-2 text-sm text-gray-800">
                  <input
                    type="checkbox"
                    aria-label={g.name}
                    checked={selectedGroupIds.includes(String(g.id))}
                    onChange={() => toggleGroup(String(g.id))}
                  />
                  {g.name}
                </label>
              ))}
            </div>

            <div className="flex items-center justify-between pt-2">
              {selected.hasRecord && selected.id && (
                <button
                  onClick={() =>
                    setActive.mutate({
                      id: String(selected.id),
                      request: new SetUserActiveRequest({ userId: String(selected.id), isActive: !selected.isActive }),
                    })
                  }
                  className={`text-sm ${selected.isActive ? "text-red-600" : "text-green-600"} hover:underline`}
                >
                  {selected.isActive ? "Disable user" : "Enable user"}
                </button>
              )}
              <button
                onClick={handleSave}
                disabled={assign.isPending}
                className="ml-auto px-4 py-2 text-sm rounded bg-indigo-600 text-white disabled:opacity-50"
              >
                {assign.isPending ? "Saving…" : "Save assignment"}
              </button>
            </div>
          </div>
        ) : (
          <p className="text-sm text-gray-400">Select a user to manage their groups.</p>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/features/access-management/__tests__/UsersTab.test.tsx; cd ..
```

Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/access-management/components/UsersTab.tsx \
        frontend/src/features/access-management/__tests__/UsersTab.test.tsx
git commit -m "feat(authz-ui): users tab with directory search and assignment"
```

---

### Task 14: Compose `AccessManagementPage`

**Files:**
- Modify: `frontend/src/pages/AccessManagementPage.tsx`

- [ ] **Step 1: Replace the page content**

Replace the entire contents of `frontend/src/pages/AccessManagementPage.tsx` with:

```typescript
import React, { useState } from "react";
import { GroupsTab } from "../features/access-management/components/GroupsTab";
import { UsersTab } from "../features/access-management/components/UsersTab";

const AccessManagementPage: React.FC = () => {
  const [tab, setTab] = useState<"groups" | "users">("groups");

  return (
    <div className="p-8 max-w-5xl mx-auto">
      <h1 className="text-2xl font-semibold text-gray-900 mb-4">Access management</h1>

      <div className="flex gap-2 mb-6">
        <button
          onClick={() => setTab("groups")}
          className={`px-4 py-2 rounded ${tab === "groups" ? "bg-indigo-600 text-white" : "bg-gray-100"}`}
        >
          Groups
        </button>
        <button
          onClick={() => setTab("users")}
          className={`px-4 py-2 rounded ${tab === "users" ? "bg-indigo-600 text-white" : "bg-gray-100"}`}
        >
          Users
        </button>
      </div>

      {tab === "groups" ? <GroupsTab /> : <UsersTab />}
    </div>
  );
};

export default AccessManagementPage;
```

- [ ] **Step 2: Full frontend verification**

```bash
cd frontend && npm run lint && CI=true npx react-scripts test --watchAll=false src/features/access-management && npm run build && cd ..
```

Expected: lint clean; all access-management tests PASS; build succeeds (the `prebuild` step regenerates the client — backend must build).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/AccessManagementPage.tsx
git commit -m "feat(authz-ui): compose access management page from tabs"
```

---

### Task 15: Full-suite verification

- [ ] **Step 1: Backend build + format + targeted tests**

```bash
dotnet build Anela.Heblo.sln && \
dotnet format Anela.Heblo.sln --verify-no-changes && \
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Authorization|FullyQualifiedName~UserManagement"
```

Expected: build succeeds, format clean, all tests PASS. (If `dotnet format --verify-no-changes` fails, run `dotnet format Anela.Heblo.sln` and commit the result.)

- [ ] **Step 2: Frontend build + lint + tests**

```bash
cd frontend && npm run lint && npm run build && CI=true npx react-scripts test --watchAll=false src/features/access-management; cd ..
```

Expected: all green.

- [ ] **Step 3: Final commit (if formatting changed anything)**

```bash
git add -A && git commit -m "chore(authz): formatting" || echo "nothing to commit"
```

---

## Self-Review

**Spec coverage:**
- Groups CRUD → Tasks 11–12 (editor create/update via existing API, delete in GroupsTab), backend already present.
- Permission↔group → `PermissionPicker` (Task 9) feeding `CreateGroupRequest`/`UpdateGroupRequest.Permissions`.
- Group↔group nesting → `ParentGroupSelect` (Task 10) feeding `...ParentGroupIds`; cycle errors surfaced via `getAuthzErrorMessage` (Task 8) in the editor.
- User↔group from EntraID → directory search (Tasks 1–4), assign-by-object-id with materialize (Task 5), endpoints (Task 6), hooks (Task 7), `UsersTab` (Task 13).
- Replace page at `/admin/access` → Task 14 (no `App.tsx` change; route already wired).
- Error handling/validation → min-length (Task 4), system-group immutability/duplicate/cycle/invalid-permission messages (Task 8), Graph failures swallowed→empty results (Tasks 1–2, consistent with existing GraphService pattern).
- Active toggle for existing records → carried via `DirectoryUserDto.Id`/`HasRecord` and reuses existing `useSetUserActive` (Task 13).
- Testing: BE handler/service tests (Tasks 1,2,4,5); FE component tests (Tasks 9,10,12,13). E2E deferred per spec.

**Placeholder scan:** none — all steps contain full code/commands.

**Type consistency:** `DirectoryUserDto` fields (`entraObjectId`, `displayName`, `email`, `hasRecord`, `id`, `isActive`, `groupIds`) are consistent across handler, response DTO, and `UsersTab`. `AssignDirectoryUserGroupsRequest` (`entraObjectId`, `displayName`, `email`, `groupIds`) consistent across request, controller, hook, and `UsersTab`. Permission value format `{key}.{level}` consistent between `permissionValues.ts` and backend `AccessMatrix`. Generated client method names (`authorization_SearchDirectory`, `authorization_AssignDirectoryGroups`) verified-pending in Task 7 Step 1 with a fallback note.

**Risk note:** Generated client method/symbol names are confirmed in Task 7 Step 1; if NSwag emits different casing, downstream Tasks 7/13 must use the actual names.
