# Entra Member Provisioning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow admins to add Entra users who hold the `heblo_user` app role to a permission group before their first login, provisioning an `AppUser` row on the spot.

**Architecture:** A new `GetAppRoleMembersAsync` method on `GraphService` resolves Entra app-role assignments via the MS Graph API. Two new use cases handle the read (list Entra candidates) and write (provision + add to group) sides. The FE gets a searchable `EntraMemberSearch` combobox rendered above the existing `MembersPicker`, and a "Never logged in" badge on unprovisioned members in `TransferList`.

**Tech Stack:** .NET 8 / MediatR / EF Core (in-memory for tests) · xUnit + FluentAssertions + Moq · React + TanStack Query + Tailwind CSS

---

## File Map

**New backend files:**
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetEntraAccessUsers/GetEntraAccessUsersRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetEntraAccessUsers/GetEntraAccessUsersHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/AddGroupMember/AddGroupMemberRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/AddGroupMember/AddGroupMemberHandler.cs`
- `backend/test/Anela.Heblo.Tests/Authorization/GetEntraAccessUsersHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Authorization/AddGroupMemberHandlerTests.cs`

**Modified backend files:**
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs` — add `GetAppRoleMembersAsync`
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` — implement + inject `IConfiguration`
- `backend/src/Anela.Heblo.Domain/Features/Authorization/IAuthorizationRepository.cs` — add `AddUserToGroupAsync`
- `backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationRepository.cs` — implement `AddUserToGroupAsync`
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UserDtos.cs` — add `EntraObjectId` to `AppUserDto`
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUsers/GetUsersHandler.cs` — populate `EntraObjectId`
- `backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs` — add 2 endpoints
- `backend/test/Anela.Heblo.Tests/Common/HebloWebApplicationFactory.cs` — add `MockGraphService`
- `backend/test/Anela.Heblo.Tests/Authorization/AuthorizationIntegrationTests.cs` — add integration tests for new endpoints

**New frontend files:**
- `frontend/src/components/access-management/EntraMemberSearch.tsx`

**Modified frontend files:**
- `frontend/src/api/hooks/useAccessManagement.ts` — add `useEntraAccessUsers` and `useAddGroupMember`
- `frontend/src/components/access-management/TransferList.tsx` — add optional `badge` to `TransferItem` and render it
- `frontend/src/components/access-management/MembersPicker.tsx` — set badge when `lastLoginAt == null`
- `frontend/src/pages/GroupDetailPage.tsx` — add `EntraMemberSearch` above `MembersPicker` in edit mode

**New docs:**
- `docs/features/entra-member-provisioning.md`

---

## Task 1: Add `EntraObjectId` to `AppUserDto` and populate it in `GetUsersHandler`

The FE needs `entraObjectId` on each user to match Entra candidates against already-provisioned group members. This is a purely additive change.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UserDtos.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUsers/GetUsersHandler.cs`

- [ ] **Step 1: Add `EntraObjectId` to `AppUserDto`**

Replace the current `AppUserDto` in `UserDtos.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Authorization.UseCases;

public class AppUserDto
{
    public Guid Id { get; set; }
    public string EntraObjectId { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public List<Guid> GroupIds { get; set; } = new();
}
```

- [ ] **Step 2: Populate `EntraObjectId` in `GetUsersHandler`**

Replace the `AppUserDto` initializer in `GetUsersHandler.cs` (the `Handle` method) so it maps the new field:

```csharp
return new GetUsersResponse
{
    Users = users.Select(u => new AppUserDto
    {
        Id = u.Id,
        EntraObjectId = u.EntraObjectId,
        Email = u.Email,
        DisplayName = u.DisplayName,
        IsActive = u.IsActive,
        LastLoginAt = u.LastLoginAt,
        GroupIds = u.UserGroups.Select(ug => ug.GroupId).ToList(),
    }).OrderBy(u => u.DisplayName).ToList(),
};
```

- [ ] **Step 3: Build to verify nothing breaks**

```bash
cd backend && dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/UserDtos.cs
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUsers/GetUsersHandler.cs
git commit -m "feat(authz): add EntraObjectId to AppUserDto for Entra member matching"
```

---

## Task 2: Add `GetAppRoleMembersAsync` to `IGraphService` and `GraphService`

This method lists all Entra users who hold a specific app role (e.g. `heblo_user`) by resolving app-role assignments on the service principal. It caches results for 20 minutes and fails soft (returns empty list + logs on any error), matching the existing patterns in `GraphService`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`

- [ ] **Step 1: Add method signature to `IGraphService`**

Replace the full content of `IGraphService.cs`:

```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;

namespace Anela.Heblo.Application.Features.UserManagement.Services;

public interface IGraphService
{
    Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default);
    Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);
    Task<List<UserDto>> GetAppRoleMembersAsync(string appRoleValue, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Inject `IConfiguration` into `GraphService`**

Add `using Microsoft.Extensions.Configuration;` to the top of `GraphService.cs`. Then add `IConfiguration` to the private field list and constructor (append it as the last parameter so existing DI registration still compiles — ASP.NET Core DI auto-resolves `IConfiguration`):

```csharp
private readonly IConfiguration _configuration;

public GraphService(
    ITokenAcquisition tokenAcquisition,
    IMemoryCache cache,
    ILogger<GraphService> logger,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration)
{
    _tokenAcquisition = tokenAcquisition;
    _cache = cache;
    _logger = logger;
    _httpClientFactory = httpClientFactory;
    _configuration = configuration;
}
```

- [ ] **Step 3: Implement `GetAppRoleMembersAsync` in `GraphService`**

Add the following method at the end of the class, before the closing `}`:

```csharp
public async Task<List<UserDto>> GetAppRoleMembersAsync(string appRoleValue, CancellationToken cancellationToken = default)
{
    var cacheKey = $"app_role_members_{appRoleValue}";
    if (_cache.TryGetValue(cacheKey, out List<UserDto>? cached) && cached != null)
        return cached;

    try
    {
        var clientId = _configuration["AzureAd:ClientId"];
        if (string.IsNullOrEmpty(clientId))
        {
            _logger.LogError("AzureAd:ClientId is not configured — cannot resolve app role members");
            return new List<UserDto>();
        }

        string graphToken;
        try
        {
            graphToken = await _tokenAcquisition.GetAccessTokenForAppAsync("https://graph.microsoft.com/.default");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Graph token for app role member lookup");
            return new List<UserDto>();
        }

        var httpClient = _httpClientFactory.CreateClient("MicrosoftGraph");

        // Step 1: resolve the service principal id and app roles for this app registration
        var spUrl = $"https://graph.microsoft.com/v1.0/servicePrincipals(appId='{clientId}')?$select=id,appRoles";
        using var spRequest = new HttpRequestMessage(HttpMethod.Get, spUrl);
        spRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);
        var spResponse = await httpClient.SendAsync(spRequest, cancellationToken);
        if (!spResponse.IsSuccessStatusCode)
        {
            var body = await spResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to resolve service principal. Status: {Status}, Body: {Body}", spResponse.StatusCode, body);
            return new List<UserDto>();
        }

        var spJson = await spResponse.Content.ReadAsStringAsync(cancellationToken);
        using var spDoc = System.Text.Json.JsonDocument.Parse(spJson);
        var spId = spDoc.RootElement.TryGetProperty("id", out var spIdProp) ? spIdProp.GetString() : null;
        if (string.IsNullOrEmpty(spId))
        {
            _logger.LogError("Service principal id not found in Graph response for clientId {ClientId}", clientId);
            return new List<UserDto>();
        }

        // Step 2: find the appRoleId for the requested role value
        string? appRoleId = null;
        if (spDoc.RootElement.TryGetProperty("appRoles", out var appRolesEl))
        {
            foreach (var role in appRolesEl.EnumerateArray())
            {
                if (role.TryGetProperty("value", out var roleName) && roleName.GetString() == appRoleValue)
                {
                    appRoleId = role.TryGetProperty("id", out var rid) ? rid.GetString() : null;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(appRoleId))
        {
            _logger.LogWarning("App role '{RoleValue}' not found on service principal {SpId}", appRoleValue, spId);
            return new List<UserDto>();
        }

        // Step 3: get all principals assigned to this role
        var assignUrl = $"https://graph.microsoft.com/v1.0/servicePrincipals/{spId}/appRoleAssignedTo?$top=999";
        using var assignRequest = new HttpRequestMessage(HttpMethod.Get, assignUrl);
        assignRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);
        var assignResponse = await httpClient.SendAsync(assignRequest, cancellationToken);
        if (!assignResponse.IsSuccessStatusCode)
        {
            var body = await assignResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to get app role assignments. Status: {Status}, Body: {Body}", assignResponse.StatusCode, body);
            return new List<UserDto>();
        }

        var assignJson = await assignResponse.Content.ReadAsStringAsync(cancellationToken);
        using var assignDoc = System.Text.Json.JsonDocument.Parse(assignJson);

        var directUserIds = new HashSet<string>();
        var groupIdsToExpand = new List<string>();

        if (assignDoc.RootElement.TryGetProperty("value", out var assignments))
        {
            foreach (var assignment in assignments.EnumerateArray())
            {
                var roleId = assignment.TryGetProperty("appRoleId", out var rid) ? rid.GetString() : null;
                if (roleId != appRoleId) continue;

                var principalType = assignment.TryGetProperty("principalType", out var pt) ? pt.GetString() : null;
                var principalId = assignment.TryGetProperty("principalId", out var pid) ? pid.GetString() : null;
                if (string.IsNullOrEmpty(principalId)) continue;

                if (principalType == "User")
                    directUserIds.Add(principalId);
                else if (principalType == "Group")
                    groupIdsToExpand.Add(principalId);
            }
        }

        // Step 4: expand group members (reuse existing method)
        foreach (var groupId in groupIdsToExpand)
        {
            var members = await GetGroupMembersAsync(groupId, cancellationToken);
            foreach (var member in members)
                if (!string.IsNullOrEmpty(member.Id))
                    directUserIds.Add(member.Id);
        }

        // Step 5: resolve display name + email for each user id
        var users = new List<UserDto>();
        foreach (var userId in directUserIds)
        {
            var userUrl = $"https://graph.microsoft.com/v1.0/users/{userId}?$select=id,displayName,mail,userPrincipalName";
            using var userRequest = new HttpRequestMessage(HttpMethod.Get, userUrl);
            userRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);
            var userResponse = await httpClient.SendAsync(userRequest, cancellationToken);
            if (!userResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Could not resolve user {UserId}", userId);
                continue;
            }
            var userJson = await userResponse.Content.ReadAsStringAsync(cancellationToken);
            using var userDoc = System.Text.Json.JsonDocument.Parse(userJson);
            var displayName = userDoc.RootElement.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
            var mail = userDoc.RootElement.TryGetProperty("mail", out var m) ? m.GetString() : null;
            var upn = userDoc.RootElement.TryGetProperty("userPrincipalName", out var u) ? u.GetString() : null;
            users.Add(new UserDto { Id = userId, DisplayName = displayName, Email = mail ?? upn ?? "" });
        }

        _cache.Set(cacheKey, users, _cacheExpiration);
        _logger.LogInformation("Resolved {Count} app role members for role '{RoleValue}'", users.Count, appRoleValue);
        return users;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error fetching app role members for role '{RoleValue}'", appRoleValue);
        return new List<UserDto>();
    }
}
```

- [ ] **Step 4: Build to verify**

```bash
cd backend && dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs
git add backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs
git commit -m "feat(authz): add GetAppRoleMembersAsync to GraphService for Entra app role lookup"
```

---

## Task 3: Add `AddUserToGroupAsync` to `IAuthorizationRepository` and `AuthorizationRepository`

This is the idempotent membership insert: adds a `UserGroup` row only when it doesn't already exist.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Authorization/IAuthorizationRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationRepository.cs`

- [ ] **Step 1: Write the failing test first**

Create `backend/test/Anela.Heblo.Tests/Authorization/AddGroupMemberHandlerTests.cs` with repository idempotency tests (the full handler tests come in Task 5; add these tests now to drive the repo change):

```csharp
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class AddGroupMemberHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"addmember_{Guid.NewGuid()}").Options);

    [Fact]
    public async Task AddUserToGroupAsync_WhenNotMember_AddsMembership()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var repo = new AuthorizationRepository(db);

        await repo.AddUserToGroupAsync(userId, groupId);
        await repo.SaveChangesAsync();

        db.UserGroups.Should().ContainSingle(ug => ug.UserId == userId && ug.GroupId == groupId);
    }

    [Fact]
    public async Task AddUserToGroupAsync_WhenAlreadyMember_IsIdempotent()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = groupId });
        await db.SaveChangesAsync();

        var repo = new AuthorizationRepository(db);
        await repo.AddUserToGroupAsync(userId, groupId);
        await repo.SaveChangesAsync();

        db.UserGroups.Count(ug => ug.UserId == userId && ug.GroupId == groupId).Should().Be(1);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd backend && dotnet test --filter "AddGroupMemberHandlerTests" --no-build 2>&1 | tail -20
```

Expected: FAIL — `AddUserToGroupAsync` not defined on `IAuthorizationRepository`.

- [ ] **Step 3: Add method to `IAuthorizationRepository`**

Add this line in the `// Edges` section of `IAuthorizationRepository.cs` (after the existing `SetUserGroupsAsync` line):

```csharp
/// <summary>Inserts a UserGroup row for (userId, groupId) if one does not already exist (idempotent).</summary>
Task AddUserToGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default);
```

- [ ] **Step 4: Implement in `AuthorizationRepository`**

Add at the end of `AuthorizationRepository.cs`, before the closing `}`:

```csharp
public async Task AddUserToGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default)
{
    var exists = await _db.UserGroups.AnyAsync(ug => ug.UserId == userId && ug.GroupId == groupId, ct);
    if (!exists)
        _db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = groupId });
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
cd backend && dotnet test --filter "AddGroupMemberHandlerTests" --no-build 2>&1 | tail -20
```

Expected: 2 tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/IAuthorizationRepository.cs
git add backend/src/Anela.Heblo.Persistence/Features/Authorization/AuthorizationRepository.cs
git add backend/test/Anela.Heblo.Tests/Authorization/AddGroupMemberHandlerTests.cs
git commit -m "feat(authz): add idempotent AddUserToGroupAsync to repository"
```

---

## Task 4: `GetEntraAccessUsers` use case + endpoint

Lists Entra users who hold the `heblo_user` app role. The FE uses this list as the candidate pool for `EntraMemberSearch`.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetEntraAccessUsers/GetEntraAccessUsersRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetEntraAccessUsers/GetEntraAccessUsersHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Authorization/GetEntraAccessUsersHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs`

- [ ] **Step 1: Write the failing handler test**

Create `backend/test/Anela.Heblo.Tests/Authorization/GetEntraAccessUsersHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GetEntraAccessUsersHandlerTests
{
    private static GetEntraAccessUsersHandler NewHandler(IGraphService graphService)
        => new(graphService);

    [Fact]
    public async Task Handle_ReturnsEntraUsersOrderedByDisplayName()
    {
        var mock = new Mock<IGraphService>();
        mock.Setup(g => g.GetAppRoleMembersAsync("heblo_user", default))
            .ReturnsAsync(new List<UserDto>
            {
                new() { Id = "obj-2", DisplayName = "Zdenek Novak", Email = "z@x.cz" },
                new() { Id = "obj-1", DisplayName = "Anna Novak", Email = "a@x.cz" },
            });

        var result = await NewHandler(mock.Object).Handle(new GetEntraAccessUsersRequest(), default);

        result.Success.Should().BeTrue();
        result.Users.Should().HaveCount(2);
        result.Users[0].DisplayName.Should().Be("Anna Novak");
        result.Users[0].EntraObjectId.Should().Be("obj-1");
        result.Users[1].DisplayName.Should().Be("Zdenek Novak");
    }

    [Fact]
    public async Task Handle_WhenGraphReturnsEmpty_ReturnsEmptyList()
    {
        var mock = new Mock<IGraphService>();
        mock.Setup(g => g.GetAppRoleMembersAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new List<UserDto>());

        var result = await NewHandler(mock.Object).Handle(new GetEntraAccessUsersRequest(), default);

        result.Success.Should().BeTrue();
        result.Users.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test — verify it fails**

```bash
cd backend && dotnet build 2>&1 | tail -5
```

Expected: compilation error — `GetEntraAccessUsersRequest` type not found.

- [ ] **Step 3: Create `GetEntraAccessUsersRequest.cs`**

```csharp
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;

public class GetEntraAccessUsersRequest : IRequest<GetEntraAccessUsersResponse> { }

public class EntraUserDto
{
    public string EntraObjectId { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
}

public class GetEntraAccessUsersResponse : BaseResponse
{
    public List<EntraUserDto> Users { get; set; } = new();
}
```

- [ ] **Step 4: Create `GetEntraAccessUsersHandler.cs`**

```csharp
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;

public class GetEntraAccessUsersHandler : IRequestHandler<GetEntraAccessUsersRequest, GetEntraAccessUsersResponse>
{
    private readonly IGraphService _graphService;

    public GetEntraAccessUsersHandler(IGraphService graphService) => _graphService = graphService;

    public async Task<GetEntraAccessUsersResponse> Handle(GetEntraAccessUsersRequest request, CancellationToken ct)
    {
        var users = await _graphService.GetAppRoleMembersAsync(AccessRoles.Base, ct);
        return new GetEntraAccessUsersResponse
        {
            Users = users.Select(u => new EntraUserDto
            {
                EntraObjectId = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName,
            }).OrderBy(u => u.DisplayName).ToList(),
        };
    }
}
```

- [ ] **Step 5: Run tests — verify they pass**

```bash
cd backend && dotnet test --filter "GetEntraAccessUsersHandlerTests" 2>&1 | tail -10
```

Expected: 2 tests pass.

- [ ] **Step 6: Add endpoint to `AuthorizationController`**

Add this using at the top of `AuthorizationController.cs`:
```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;
```

Add this action method after the `GetUsers` action:

```csharp
[HttpGet("entra-users")]
public async Task<ActionResult<GetEntraAccessUsersResponse>> GetEntraUsers(CancellationToken ct)
    => HandleResponse(await _mediator.Send(new GetEntraAccessUsersRequest(), ct));
```

- [ ] **Step 7: Build**

```bash
cd backend && dotnet build
```

Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetEntraAccessUsers/
git add backend/test/Anela.Heblo.Tests/Authorization/GetEntraAccessUsersHandlerTests.cs
git add backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs
git commit -m "feat(authz): add GetEntraAccessUsers use case and GET /entra-users endpoint"
```

---

## Task 5: `AddGroupMember` use case + endpoint

Provisions the `AppUser` if not found, then adds the group membership idempotently. Invalidates the permission cache.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/AddGroupMember/AddGroupMemberRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/AddGroupMember/AddGroupMemberHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/AddGroupMemberHandlerTests.cs` (extend with handler tests)
- Modify: `backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs`

- [ ] **Step 1: Write failing handler tests**

Append these test methods to `AddGroupMemberHandlerTests.cs` (inside the existing class, after the repository tests):

```csharp
// ── Handler tests ──────────────────────────────────────────────────────────

private static PermissionGroup MakeGroup(ApplicationDbContext db)
{
    var g = new PermissionGroup { Id = Guid.NewGuid(), Name = "Test", CreatedAt = DateTimeOffset.UtcNow };
    db.PermissionGroups.Add(g);
    db.SaveChanges();
    return g;
}

private static (AddGroupMemberHandler Handler, Mock<IPermissionResolver> Resolver) NewHandler(ApplicationDbContext db)
{
    var resolver = new Mock<IPermissionResolver>();
    return (new AddGroupMemberHandler(new AuthorizationRepository(db), resolver.Object), resolver);
}

[Fact]
public async Task Handle_NewEntraUser_IsProvisionedAndAddedToGroup()
{
    await using var db = NewDb();
    var group = MakeGroup(db);
    var (handler, _) = NewHandler(db);

    var result = await handler.Handle(new AddGroupMemberRequest
    {
        GroupId = group.Id,
        EntraObjectId = "entra-new",
        Email = "new@x.cz",
        DisplayName = "New User",
    }, default);

    result.Success.Should().BeTrue();
    result.User.Should().NotBeNull();
    result.User!.Email.Should().Be("new@x.cz");
    result.User.LastLoginAt.Should().BeNull();

    var user = await db.AppUsers.SingleAsync(u => u.EntraObjectId == "entra-new");
    user.DisplayName.Should().Be("New User");
    db.UserGroups.Should().ContainSingle(ug => ug.UserId == user.Id && ug.GroupId == group.Id);
}

[Fact]
public async Task Handle_ExistingEntraUser_AddedToGroupWithoutDuplicateProvision()
{
    await using var db = NewDb();
    var group = MakeGroup(db);
    var existingUser = new AppUser
    {
        Id = Guid.NewGuid(),
        EntraObjectId = "entra-existing",
        Email = "existing@x.cz",
        DisplayName = "Existing",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        LastLoginAt = DateTimeOffset.UtcNow.AddDays(-5),
    };
    db.AppUsers.Add(existingUser);
    await db.SaveChangesAsync();

    var (handler, _) = NewHandler(db);
    var result = await handler.Handle(new AddGroupMemberRequest
    {
        GroupId = group.Id,
        EntraObjectId = "entra-existing",
        Email = "existing@x.cz",
        DisplayName = "Existing",
    }, default);

    result.Success.Should().BeTrue();
    db.AppUsers.Count(u => u.EntraObjectId == "entra-existing").Should().Be(1);
    db.UserGroups.Should().ContainSingle(ug => ug.UserId == existingUser.Id && ug.GroupId == group.Id);
}

[Fact]
public async Task Handle_ReAdd_IsIdempotent()
{
    await using var db = NewDb();
    var group = MakeGroup(db);
    var (handler, _) = NewHandler(db);
    var req = new AddGroupMemberRequest
    {
        GroupId = group.Id,
        EntraObjectId = "entra-idem",
        Email = "idem@x.cz",
        DisplayName = "Idempotent",
    };

    await handler.Handle(req, default);
    var result = await handler.Handle(req, default);

    result.Success.Should().BeTrue();
    db.UserGroups.Count(ug => ug.GroupId == group.Id).Should().Be(1);
}

[Fact]
public async Task Handle_GroupNotFound_ReturnsError()
{
    await using var db = NewDb();
    var (handler, _) = NewHandler(db);

    var result = await handler.Handle(new AddGroupMemberRequest
    {
        GroupId = Guid.NewGuid(),
        EntraObjectId = "entra-x",
        Email = "x@x.cz",
        DisplayName = "X",
    }, default);

    result.Success.Should().BeFalse();
    result.ErrorCode.Should().Be(ErrorCodes.AuthorizationGroupNotFound);
}

[Fact]
public async Task Handle_CacheIsInvalidatedForNewUser()
{
    await using var db = NewDb();
    var group = MakeGroup(db);
    var (handler, resolver) = NewHandler(db);

    await handler.Handle(new AddGroupMemberRequest
    {
        GroupId = group.Id,
        EntraObjectId = "entra-cache",
        Email = "cache@x.cz",
        DisplayName = "Cache Test",
    }, default);

    resolver.Verify(r => r.InvalidateCache("entra-cache"), Times.Once);
}
```

Also add the following `using` directives at the top of `AddGroupMemberHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.AddGroupMember;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Moq;
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
cd backend && dotnet build 2>&1 | tail -5
```

Expected: compilation errors — `AddGroupMemberRequest` not found, `AddGroupMemberHandler` not found.

- [ ] **Step 3: Create `AddGroupMemberRequest.cs`**

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AddGroupMember;

public class AddGroupMemberRequest : IRequest<AddGroupMemberResponse>
{
    public Guid GroupId { get; set; }
    public string EntraObjectId { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
}

public class AddGroupMemberResponse : BaseResponse
{
    public AppUserDto? User { get; set; }

    public AddGroupMemberResponse() { }
    public AddGroupMemberResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 4: Create `AddGroupMemberHandler.cs`**

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AddGroupMember;

public class AddGroupMemberHandler : IRequestHandler<AddGroupMemberRequest, AddGroupMemberResponse>
{
    private readonly IAuthorizationRepository _repo;
    private readonly IPermissionResolver _resolver;

    public AddGroupMemberHandler(IAuthorizationRepository repo, IPermissionResolver resolver)
    {
        _repo = repo;
        _resolver = resolver;
    }

    public async Task<AddGroupMemberResponse> Handle(AddGroupMemberRequest request, CancellationToken ct)
    {
        var group = await _repo.GetGroupByIdAsync(request.GroupId, ct);
        if (group is null)
            return new AddGroupMemberResponse(ErrorCodes.AuthorizationGroupNotFound);

        var user = await _repo.GetUserByObjectIdAsync(request.EntraObjectId, ct);
        if (user is null)
        {
            user = await _repo.AddUserAsync(new AppUser
            {
                Id = Guid.NewGuid(),
                EntraObjectId = request.EntraObjectId,
                Email = request.Email,
                DisplayName = request.DisplayName,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                LastLoginAt = null,
            }, ct);
        }

        await _repo.AddUserToGroupAsync(user.Id, request.GroupId, ct);
        await _repo.SaveChangesAsync(ct);
        _resolver.InvalidateCache(request.EntraObjectId);

        var groups = await _repo.GetUserGroupsAsync(user.Id, ct);
        return new AddGroupMemberResponse
        {
            User = new AppUserDto
            {
                Id = user.Id,
                EntraObjectId = user.EntraObjectId,
                Email = user.Email,
                DisplayName = user.DisplayName,
                IsActive = user.IsActive,
                LastLoginAt = user.LastLoginAt,
                GroupIds = groups.Select(ug => ug.GroupId).ToList(),
            }
        };
    }
}
```

- [ ] **Step 5: Run tests — verify they pass**

```bash
cd backend && dotnet test --filter "AddGroupMemberHandlerTests" 2>&1 | tail -15
```

Expected: 7 tests pass (2 repo + 5 handler).

- [ ] **Step 6: Add endpoint to `AuthorizationController`**

Add this using:
```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.AddGroupMember;
```

Add this action after the `UpdateGroup` action:

```csharp
[HttpPost("groups/{id:guid}/members")]
[Authorize(Roles = AccessRoles.AdministrationWrite)]
public async Task<ActionResult<AddGroupMemberResponse>> AddGroupMember([FromRoute] Guid id, [FromBody] AddGroupMemberRequest request, CancellationToken ct)
{
    request.GroupId = id;
    return HandleResponse(await _mediator.Send(request, ct));
}
```

- [ ] **Step 7: Build**

```bash
cd backend && dotnet build
```

Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/AddGroupMember/
git add backend/test/Anela.Heblo.Tests/Authorization/AddGroupMemberHandlerTests.cs
git add backend/src/Anela.Heblo.API/Controllers/AuthorizationController.cs
git commit -m "feat(authz): add AddGroupMember use case and POST /groups/{id}/members endpoint"
```

---

## Task 6: Integration tests for new endpoints + mock `IGraphService` in factory

`IGraphService` is not yet mocked in `HebloWebApplicationFactory`. Adding a `MockGraphService` class there lets integration tests call the new Entra endpoint without real MS Graph.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Common/HebloWebApplicationFactory.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/AuthorizationIntegrationTests.cs`

- [ ] **Step 1: Add `MockGraphService` to `HebloWebApplicationFactory.cs`**

At the bottom of `HebloWebApplicationFactory.cs` (after the existing mock classes), add:

```csharp
public class MockGraphService : IGraphService
{
    public Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<UserDto>());

    public Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<UserDto>());

    public Task<List<UserDto>> GetAppRoleMembersAsync(string appRoleValue, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<UserDto>());
}
```

Add the following `using` directives at the top of `HebloWebApplicationFactory.cs` if not already present:

```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
```

Inside `ConfigureWebHost`'s `ConfigureServices` lambda, register the mock (after the existing `services.AddScoped<IE2ESessionService, MockE2ESessionService>()` line):

```csharp
services.AddScoped<IGraphService, MockGraphService>();
```

- [ ] **Step 2: Write integration tests**

Add these tests to `AuthorizationIntegrationTests.cs` (add the missing `using` imports too):

```csharp
using System.Net.Http.Json;
using Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;
using Anela.Heblo.Application.Features.Authorization.UseCases.AddGroupMember;
```

```csharp
[Fact]
public async Task GetEntraUsers_ReturnsOkWithEmptyList()
{
    var client = _factory.CreateClient();

    var response = await client.GetAsync("/api/admin/authorization/entra-users");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<GetEntraAccessUsersResponse>();
    body!.Success.Should().BeTrue();
    body.Users.Should().NotBeNull();
    body.Users.Should().BeEmpty(); // MockGraphService returns empty
}

[Fact]
public async Task AddGroupMember_GroupNotFound_Returns404()
{
    var client = _factory.CreateClient();
    var nonExistentId = Guid.NewGuid();

    var response = await client.PostAsJsonAsync(
        $"/api/admin/authorization/groups/{nonExistentId}/members",
        new { entraObjectId = "obj-1", email = "x@x.cz", displayName = "X" });

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
}

[Fact]
public async Task AddGroupMember_ExistingGroup_ProvisionsMemberAndReturnsOk()
{
    var client = _factory.CreateClient();

    // Create a group first
    var createResp = await client.PostAsJsonAsync(
        "/api/admin/authorization/groups",
        new { name = "EntraTestGroup", permissions = new string[] { } });
    createResp.EnsureSuccessStatusCode();

    // Fetch groups to get the id
    var groupsResp = await client.GetFromJsonAsync<GetGroupsResponse>("/api/admin/authorization/groups");
    var group = groupsResp!.Groups.First(g => g.Name == "EntraTestGroup");

    var response = await client.PostAsJsonAsync(
        $"/api/admin/authorization/groups/{group.Id}/members",
        new { entraObjectId = "entra-integration-test", email = "int@x.cz", displayName = "Integration User" });

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<AddGroupMemberResponse>();
    body!.Success.Should().BeTrue();
    body.User!.Email.Should().Be("int@x.cz");
    body.User.LastLoginAt.Should().BeNull();
}
```

- [ ] **Step 3: Run all authorization tests**

```bash
cd backend && dotnet test --filter "Authorization" 2>&1 | tail -20
```

Expected: all existing tests + 3 new integration tests pass.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Common/HebloWebApplicationFactory.cs
git add backend/test/Anela.Heblo.Tests/Authorization/AuthorizationIntegrationTests.cs
git commit -m "test(authz): add MockGraphService + integration tests for entra-users and AddGroupMember endpoints"
```

---

## Task 7: Backend validation — `dotnet build` + `dotnet format`

- [ ] **Step 1: Full build**

```bash
cd backend && dotnet build
```

Expected: 0 errors, 0 warnings (or only pre-existing ones).

- [ ] **Step 2: Format**

```bash
cd backend && dotnet format
```

- [ ] **Step 3: Check for formatting diff**

```bash
git diff --name-only
```

If any files were reformatted, stage and commit them:

```bash
git add -u && git commit -m "chore: dotnet format"
```

- [ ] **Step 4: Run full test suite**

```bash
cd backend && dotnet test 2>&1 | tail -20
```

Expected: all tests pass.

---

## Task 8: Frontend — regenerate TS client and add hooks

After the new BE endpoints are in place, rebuilding the FE regenerates the TypeScript client with `GetEntraAccessUsersResponse`, `EntraUserDto`, `AddGroupMemberRequest`, `AddGroupMemberResponse`, and the updated `AppUserDto` (with `entraObjectId`).

**Files:**
- Modify: `frontend/src/api/hooks/useAccessManagement.ts`

- [ ] **Step 1: Regenerate the TypeScript client**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: build succeeds; `src/api/generated/api-client.ts` (or `.js`) is updated.

- [ ] **Step 2: Add new `keys` entry**

In `useAccessManagement.ts`, add `entraUsers` to the `keys` object:

```typescript
const keys = {
  catalogue: ["authz", "catalogue"] as const,
  groups: ["authz", "groups"] as const,
  group: (id: string) => ["authz", "group", id] as const,
  users: ["authz", "users"] as const,
  entraUsers: ["authz", "entra-users"] as const,
};
```

- [ ] **Step 3: Add import types**

Add the following to the existing `import type { ... } from "../generated/api-client"` block:

```typescript
GetEntraAccessUsersResponse,
AddGroupMemberResponse,
```

Add the following to the existing `import { ... } from "../generated/api-client"` block:

```typescript
AddGroupMemberRequest,
```

- [ ] **Step 4: Add `useEntraAccessUsers` hook**

Append after `useUsers`:

```typescript
export const useEntraAccessUsers = () => {
  return useQuery({
    queryKey: keys.entraUsers,
    queryFn: async (): Promise<GetEntraAccessUsersResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_GetEntraUsers();
    },
    staleTime: 5 * 60 * 1000,
  });
};
```

- [ ] **Step 5: Add `useAddGroupMember` hook**

Append after `useEntraAccessUsers`:

```typescript
export const useAddGroupMember = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      groupId,
      request,
    }: {
      groupId: string;
      request: AddGroupMemberRequest;
    }): Promise<AddGroupMemberResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_AddGroupMember(groupId, request);
    },
    onSuccess: (_data, { groupId }) => {
      queryClient.invalidateQueries({ queryKey: keys.users });
      queryClient.invalidateQueries({ queryKey: keys.group(groupId) });
    },
  });
};
```

Also add this export at the bottom of the file alongside the other re-exports:

```typescript
export type { AddGroupMemberRequest };
```

- [ ] **Step 6: Build to confirm types**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/api/hooks/useAccessManagement.ts
git commit -m "feat(authz): add useEntraAccessUsers and useAddGroupMember hooks"
```

---

## Task 9: Frontend — badge in `TransferList` and `MembersPicker`

Members who have never logged in get a small amber "Never logged in" pill in the `MembersPicker`'s assigned column. This requires an optional `badge` field on `TransferItem`.

**Files:**
- Modify: `frontend/src/components/access-management/TransferList.tsx`
- Modify: `frontend/src/components/access-management/MembersPicker.tsx`

- [ ] **Step 1: Add `badge` to `TransferItem` type**

In `TransferList.tsx`, update the `TransferItem` type:

```typescript
export type TransferItem = {
  id: string;
  label: string;
  sublabel?: string;
  badge?: string;
};
```

- [ ] **Step 2: Render badge in `ItemRow`**

Replace the `<div className="flex flex-col min-w-0 flex-1">` block inside `ItemRow` with:

```tsx
<div className="flex flex-col min-w-0 flex-1">
  <div className="flex items-center gap-2 min-w-0">
    <span className="text-sm text-gray-900 truncate">{item.label}</span>
    {item.badge && (
      <span className="flex-shrink-0 text-xs px-1.5 py-0.5 rounded bg-amber-100 text-amber-700 font-medium">
        {item.badge}
      </span>
    )}
  </div>
  {item.sublabel && (
    <span className="text-xs text-gray-500 truncate">{item.sublabel}</span>
  )}
</div>
```

- [ ] **Step 3: Use `lastLoginAt` in `MembersPicker`**

Replace the `items` useMemo in `MembersPicker.tsx`:

```typescript
const items: TransferItem[] = useMemo(
  () =>
    (users.data?.users ?? []).map((u) => ({
      id: u.id ?? "",
      label: u.displayName ?? u.email ?? u.id ?? "",
      sublabel: u.email,
      badge: u.lastLoginAt == null ? "Never logged in" : undefined,
    })),
  [users.data]
);
```

- [ ] **Step 4: Build + lint**

```bash
cd frontend && npm run build && npm run lint 2>&1 | tail -20
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/access-management/TransferList.tsx
git add frontend/src/components/access-management/MembersPicker.tsx
git commit -m "feat(authz): add Never logged in badge to TransferList and MembersPicker"
```

---

## Task 10: Frontend — `EntraMemberSearch` component

A searchable dropdown that lists Entra candidates not already in the group. Picking a person immediately calls `useAddGroupMember`, then notifies the parent so the draft `memberUserIds` is updated.

**Files:**
- Create: `frontend/src/components/access-management/EntraMemberSearch.tsx`

- [ ] **Step 1: Create the component**

```tsx
import React, { useMemo, useState } from "react";
import {
  useEntraAccessUsers,
  useUsers,
  useAddGroupMember,
} from "../../api/hooks/useAccessManagement";
import { useToast } from "../../contexts/ToastContext";
import { AddGroupMemberRequest } from "../../api/generated/api-client";

interface EntraMemberSearchProps {
  groupId: string;
  currentMemberIds: string[];
  onMemberAdded: (userId: string) => void;
}

export default function EntraMemberSearch({
  groupId,
  currentMemberIds,
  onMemberAdded,
}: EntraMemberSearchProps) {
  const entraUsers = useEntraAccessUsers();
  const provisionedUsers = useUsers();
  const addMember = useAddGroupMember();
  const toast = useToast();
  const [query, setQuery] = useState("");
  const [isOpen, setIsOpen] = useState(false);

  const currentMemberEntraIds = useMemo(() => {
    const inGroup = (provisionedUsers.data?.users ?? []).filter((u) =>
      currentMemberIds.includes(u.id ?? "")
    );
    return new Set(
      inGroup.map((u) => u.entraObjectId).filter((id): id is string => Boolean(id))
    );
  }, [provisionedUsers.data, currentMemberIds]);

  const candidates = useMemo(() => {
    const available = (entraUsers.data?.users ?? []).filter(
      (u) => !currentMemberEntraIds.has(u.entraObjectId ?? "")
    );
    if (!query.trim()) return available;
    const q = query.toLowerCase();
    return available.filter(
      (u) =>
        u.displayName?.toLowerCase().includes(q) ||
        u.email?.toLowerCase().includes(q)
    );
  }, [entraUsers.data, currentMemberEntraIds, query]);

  const handleSelect = async (user: {
    entraObjectId?: string | null;
    email?: string | null;
    displayName?: string | null;
  }) => {
    if (!user.entraObjectId) return;
    setQuery("");
    setIsOpen(false);
    try {
      const result = await addMember.mutateAsync({
        groupId,
        request: new AddGroupMemberRequest({
          entraObjectId: user.entraObjectId,
          email: user.email ?? "",
          displayName: user.displayName ?? "",
        }),
      });
      if (result.user?.id) {
        onMemberAdded(result.user.id);
        toast.showSuccess(
          "Member added",
          `${user.displayName ?? user.email} added to group`
        );
      }
    } catch {
      toast.showError("Add failed", "Could not add member to group");
    }
  };

  const isLoading =
    entraUsers.isLoading || provisionedUsers.isLoading || addMember.isPending;

  return (
    <div className="relative mb-4">
      <label className="block text-xs font-semibold text-gray-600 uppercase tracking-wider mb-1">
        Add Entra user
      </label>
      <input
        type="text"
        value={query}
        onChange={(e) => {
          setQuery(e.target.value);
          setIsOpen(true);
        }}
        onFocus={() => setIsOpen(true)}
        onBlur={() => setTimeout(() => setIsOpen(false), 150)}
        placeholder={
          entraUsers.isLoading
            ? "Loading Entra users…"
            : addMember.isPending
            ? "Adding…"
            : "Search by name or email…"
        }
        disabled={isLoading}
        className="w-full rounded border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-400 disabled:bg-gray-50 disabled:text-gray-400"
      />
      {isOpen && candidates.length > 0 && (
        <ul className="absolute z-20 mt-1 w-full bg-white border border-gray-200 rounded shadow-lg max-h-48 overflow-y-auto">
          {candidates.map((u) => (
            <li
              key={u.entraObjectId}
              onMouseDown={() => handleSelect(u)}
              className="flex flex-col px-3 py-2 hover:bg-indigo-50 cursor-pointer"
            >
              <span className="text-sm text-gray-900">{u.displayName}</span>
              <span className="text-xs text-gray-500">{u.email}</span>
            </li>
          ))}
        </ul>
      )}
      {isOpen && !entraUsers.isLoading && candidates.length === 0 && query.trim() && (
        <div className="absolute z-20 mt-1 w-full bg-white border border-gray-200 rounded shadow-lg px-3 py-2 text-sm text-gray-400">
          No matching Entra users
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Build + lint**

```bash
cd frontend && npm run build && npm run lint 2>&1 | tail -20
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/access-management/EntraMemberSearch.tsx
git commit -m "feat(authz): add EntraMemberSearch combobox component"
```

---

## Task 11: Frontend — wire `EntraMemberSearch` into `GroupDetailPage`

Add `EntraMemberSearch` above the `MembersPicker` in edit mode only (not create mode, since `groupId` would be `"new"`). When a member is added, update `draft.memberUserIds` so they immediately appear in the "Members" column.

**Files:**
- Modify: `frontend/src/pages/GroupDetailPage.tsx`

- [ ] **Step 1: Add import**

Add to the import section of `GroupDetailPage.tsx`:

```typescript
import EntraMemberSearch from "../components/access-management/EntraMemberSearch";
```

- [ ] **Step 2: Find the `MembersPicker` JSX**

Search in `GroupDetailPage.tsx` for the `<MembersPicker` usage. It will look like:

```tsx
<MembersPicker
  value={draft.memberUserIds}
  onChange={(ids) => updateDraft({ memberUserIds: ids })}
/>
```

- [ ] **Step 3: Add `EntraMemberSearch` above `MembersPicker`**

Replace that `<MembersPicker` block with:

```tsx
{!isCreateMode && (
  <EntraMemberSearch
    groupId={id}
    currentMemberIds={draft.memberUserIds}
    onMemberAdded={(userId) =>
      updateDraft({ memberUserIds: [...draft.memberUserIds, userId] })
    }
  />
)}
<MembersPicker
  value={draft.memberUserIds}
  onChange={(ids) => updateDraft({ memberUserIds: ids })}
/>
```

- [ ] **Step 4: Build + lint**

```bash
cd frontend && npm run build && npm run lint 2>&1 | tail -20
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/GroupDetailPage.tsx
git commit -m "feat(authz): wire EntraMemberSearch into GroupDetailPage above MembersPicker"
```

---

## Task 12: Docs — document Graph permission prerequisite

**Files:**
- Create: `docs/features/entra-member-provisioning.md`

- [ ] **Step 1: Create the feature doc**

```markdown
# Entra Member Provisioning

Allows admins to add users who hold the `heblo_user` Entra app role to a permission group before their first login.

## How it works

1. Admin opens a group's detail page in Access Management.
2. The **Add Entra user** search box lists all principals (users and group members) assigned the `heblo_user` app role on the application's service principal.
3. Selecting a person immediately provisions an `AppUser` row (if needed) and adds a `UserGroup` membership.
4. The member appears in the `MembersPicker` with a **"Never logged in"** badge until they sign in.
5. On first login, `PermissionResolver.ResolveAsync` finds the pre-provisioned user by `EntraObjectId`, sets `LastLoginAt`, and the badge disappears.

## Required Microsoft Graph permissions (application)

The app registration needs the following **application** permissions with admin consent granted. These are in addition to the existing `User.Read.All` / `GroupMember.Read.All`:

| Permission | Reason |
|---|---|
| `Application.Read.All` | Read the service principal's app roles and assignments via `/servicePrincipals/.../appRoleAssignedTo` |

Alternatively, `Directory.Read.All` grants the same access with broader scope.

### Granting consent

```bash
# Replace <tenant-id> and <app-object-id> with real values from Entra portal
az ad app permission grant --id <app-object-id> --api 00000003-0000-0000-c000-000000000000 --scope Application.Read.All
az ad app permission admin-consent --id <app-object-id>
```

Or grant in the Entra portal under **App registrations → <app> → API permissions → Add a permission → Microsoft Graph → Application permissions → Application.Read.All → Grant admin consent**.

## Caching

Entra candidate results are cached for 20 minutes per app role value (same as group member results). The cache is keyed by role value; a server restart clears it.

## Database impact

- A new `AppUser` row is created with `LastLoginAt = null` when provisioning a never-logged-in user.
- A new `UserGroup` row is created. The insert is idempotent — re-adding the same person to the same group is a no-op.
```

- [ ] **Step 2: Commit**

```bash
git add docs/features/entra-member-provisioning.md
git commit -m "docs: document Entra member provisioning Graph permissions and behavior"
```

---

## Task 13: Final validation

- [ ] **Step 1: Full backend build + format**

```bash
cd backend && dotnet build && dotnet format
git diff --name-only
# commit formatting changes if any
```

- [ ] **Step 2: Full test suite**

```bash
cd backend && dotnet test 2>&1 | tail -20
```

Expected: all tests pass.

- [ ] **Step 3: Full frontend build + lint**

```bash
cd frontend && npm run build && npm run lint 2>&1 | tail -20
```

Expected: 0 errors, 0 lint violations.

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Covered by |
|---|---|
| Candidate source: `heblo_user` app role | Task 2 `GetAppRoleMembersAsync`, Task 4 handler uses `AccessRoles.Base` |
| Immediate provisioning on pick | Task 5 `AddGroupMemberHandler` creates `AppUser` + `UserGroup` on the spot |
| Idempotent re-add | Task 3 repo method + Task 5 handler test |
| `AppUser` pre-provisioned with `LastLoginAt = null` | Task 5 handler sets `LastLoginAt = null` |
| Cache invalidation after add | Task 5 handler calls `_resolver.InvalidateCache` + test verifies |
| Group-not-found error | Task 5 handler + test |
| Pending "Never logged in" badge | Task 9 `TransferList` + `MembersPicker` |
| `EntraMemberSearch` combobox | Task 10 component |
| Placed above `MembersPicker` in edit mode only | Task 11 |
| Adding member updates draft memberUserIds | Task 11 `onMemberAdded` callback |
| Graph permission prerequisite documented | Task 12 |
| Group members that expand (Group principals) | Task 2 `groupIdsToExpand` loop |
| 20-min cache keyed by role value | Task 2 `cacheKey = $"app_role_members_{appRoleValue}"` |

**Placeholder scan:** No TBDs, TODOs, or "similar to Task N" references — each step contains the actual code.

**Type consistency check:**
- `EntraUserDto.EntraObjectId` matches `AddGroupMemberRequest.EntraObjectId` — consistent across Tasks 4, 5, 10.
- `AppUserDto.EntraObjectId` added in Task 1, populated in `GetUsersHandler`, read in FE `EntraMemberSearch` (`u.entraObjectId`) — consistent.
- `AddGroupMemberResponse.User` typed as `AppUserDto?` — matches the null check `result.user?.id` in `EntraMemberSearch`.
- `onMemberAdded(result.user.id)` in Task 10, `onMemberAdded: (userId: string) => void` in Task 11 — consistent string parameter.
