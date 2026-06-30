# Move GraphService to Adapters.Microsoft365 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development to implement this plan task-by-task.

**Goal:** Move `GraphService` and `MockGraphService` from the Application layer into the Microsoft365 adapter project, consolidating I/O-bound HTTP logic where it belongs.

**Architecture:** `IGraphService` stays in `Anela.Heblo.Application` (it is a port/contract), while the two concrete implementations move to `Anela.Heblo.Adapters.Microsoft365`. DI registration for the pair migrates into `AddMicrosoft365Adapter()` following the identical pattern already used for `IPhotobankGraphService`/`PhotobankGraphService`. The `UserManagementModule` loses the redundant registration block and the `AddHttpClient("MicrosoftGraph")` call.

**Tech Stack:** .NET 8, Microsoft.Graph 5.92.0, Microsoft.Identity.Web 3.14.1, xUnit + FluentAssertions + Moq (tests)

---

### task: move-graph-service-files

Move the two concrete service files to the adapter project with updated namespaces. The interface (`IGraphService.cs`) is NOT touched.

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/MockGraphService.cs`

- [ ] Create `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` with the content below. The only difference from the original is the `namespace` declaration and the `using` for the contract (`IGraphService` and `UserDto` still live in `Anela.Heblo.Application`):

```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Microsoft.Identity.Client;
using System.Net.Http;

namespace Anela.Heblo.Adapters.Microsoft365.UserManagement;

/// <summary>
/// Service for accessing Microsoft Graph API to retrieve group members.
/// Uses application permissions (GetAccessTokenForAppAsync) instead of user permissions
/// to avoid MSAL IDW10502 error that requires user context or login hint.
/// </summary>
public class GraphService : IGraphService
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GraphService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(20);
    private const int SearchResultLimit = 25;

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

    /// <summary>
    /// Parses Microsoft Graph API response JSON to extract user members.
    /// An entry is treated as a user if:
    /// - @odata.type contains "user" (case-insensitive), OR
    /// - userPrincipalName property exists
    /// </summary>
    internal static (List<UserDto> Users, int TotalCount) ParseMembersFromJson(string json)
    {
        using var jsonDocument = System.Text.Json.JsonDocument.Parse(json);

        var members = new List<UserDto>();
        var totalMembers = 0;

        if (!jsonDocument.RootElement.TryGetProperty("value", out var valueElement) ||
            valueElement.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return (members, totalMembers);
        }

        foreach (var memberElement in valueElement.EnumerateArray())
        {
            totalMembers++;

            // Discrimination rule: an entry is a user if:
            // (@odata.type exists AND Contains("user", OrdinalIgnoreCase)) OR (userPrincipalName property exists)
            if ((memberElement.TryGetProperty("@odata.type", out var odataType) &&
                 odataType.GetString()?.Contains("user", StringComparison.OrdinalIgnoreCase) == true) ||
                memberElement.TryGetProperty("userPrincipalName", out _))
            {
                var id = memberElement.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                var displayName = memberElement.TryGetProperty("displayName", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                var mail = memberElement.TryGetProperty("mail", out var mailProp) ? mailProp.GetString() : null;
                var userPrincipalName = memberElement.TryGetProperty("userPrincipalName", out var upnProp) ? upnProp.GetString() : null;

                members.Add(new UserDto
                {
                    Id = id,
                    DisplayName = displayName,
                    Email = mail ?? userPrincipalName ?? string.Empty
                });
            }
        }

        return (members, totalMembers);
    }

    private async Task<string> AcquireGraphTokenAsync(string groupId, CancellationToken cancellationToken)
    {
        // Acquire Graph API token using application permissions (not user context)
        var scope = "https://graph.microsoft.com/.default";
        _logger.LogInformation("Attempting to acquire MS Graph token with application scope: {Scope}", scope);

        var tokenStart = DateTime.UtcNow;
        var graphToken = await _tokenAcquisition.GetAccessTokenForAppAsync(scope);
        var tokenDuration = DateTime.UtcNow - tokenStart;
        _logger.LogInformation("Successfully acquired MS Graph application token in {Duration}ms", tokenDuration.TotalMilliseconds);

        // Log token length for troubleshooting (not the actual token)
        _logger.LogDebug("Token acquired with length: {TokenLength} characters", graphToken?.Length ?? 0);

        return graphToken;
    }

    public async Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting GetGroupMembersAsync for groupId: {GroupId}", groupId);
        var cacheKey = $"group_members_{groupId}";

        // Try to get from cache first
        if (_cache.TryGetValue(cacheKey, out List<UserDto>? cachedMembers) && cachedMembers != null)
        {
            _logger.LogInformation("Returning cached group members for {GroupId}. Count: {Count}", groupId, cachedMembers.Count);
            return cachedMembers;
        }

        _logger.LogInformation("No cached data found for group {GroupId}, proceeding with MS Graph API call", groupId);

        try
        {
            var graphToken = await AcquireGraphTokenAsync(groupId, cancellationToken);

            // Get group members from Microsoft Graph.
            // Matches the shared "MicrosoftGraph" named client used by Marketing/MeetingTasks/CatalogDocuments/KnowledgeBase/Photobank modules.
            var httpClient = _httpClientFactory.CreateClient("MicrosoftGraph");

            var requestUrl = $"https://graph.microsoft.com/v1.0/groups/{groupId}/members?$select=id,displayName,mail,userPrincipalName";
            _logger.LogInformation("Making MS Graph API request to: {RequestUrl}", requestUrl);

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);

            var apiCallStart = DateTime.UtcNow;
            var response = await httpClient.SendAsync(request, cancellationToken);
            var apiCallDuration = DateTime.UtcNow - apiCallStart;

            _logger.LogInformation("MS Graph API call completed in {Duration}ms with status: {StatusCode}",
                apiCallDuration.TotalMilliseconds, response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Microsoft Graph API call failed for groupId: {GroupId}. Status: {StatusCode}, RequestUrl: {RequestUrl}, ResponseContent: {Content}",
                    groupId, response.StatusCode, requestUrl, errorContent);

                // Log response headers for troubleshooting
                _logger.LogDebug("Response headers: {@Headers}", response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)));

                return new List<UserDto>();
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("MS Graph API response received. Content length: {ContentLength} characters", responseContent?.Length ?? 0);

            var (members, totalMembers) = ParseMembersFromJson(responseContent);
            _logger.LogInformation("Processed {TotalMembers} total members, {UserMembers} user members for group {GroupId}",
                totalMembers, members.Count, groupId);

            // Cache the results
            _cache.Set(cacheKey, members, _cacheExpiration);
            _logger.LogInformation("Cached {Count} group members for group {GroupId} with expiration {CacheExpiration}",
                members.Count, groupId, _cacheExpiration);

            _logger.LogInformation("Successfully completed GetGroupMembersAsync for group {GroupId}. Final result count: {Count}", groupId, members.Count);
            return members;
        }
        catch (MsalException msalEx)
        {
            _logger.LogError(msalEx, "Failed to acquire Graph API application token. MSAL Error: {ErrorCode} - {ErrorDescription}. GroupId: {GroupId}, Scope: {Scope}",
                msalEx.ErrorCode, msalEx.Message, groupId, "https://graph.microsoft.com/.default");
            throw;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
        {
            _logger.LogError(odataEx, "Microsoft Graph OData error fetching group members for group {GroupId}. Error: {ErrorCode}", groupId, odataEx.Error?.Code);
            throw;
        }
        catch (UnauthorizedAccessException authEx)
        {
            _logger.LogError(authEx, "Unauthorized access when fetching group members for group {GroupId}. Check application permissions.", groupId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching group members for group {GroupId}", groupId);
            throw;
        }
    }

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

    public async Task<List<UserDto>> GetAppRoleMembersAsync(string appRoleValue, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appRoleValue))
        {
            _logger.LogWarning("appRoleValue is null or empty — skipping app role member lookup");
            return new List<UserDto>();
        }

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
            var spJson = await spResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!spResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to resolve service principal. Status: {Status}, Body: {Body}", spResponse.StatusCode, spJson);
                return new List<UserDto>();
            }
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

            // Step 3: get all principals assigned to this role (paginated)
            var directUserIds = new HashSet<string>();
            var groupIdsToExpand = new List<string>();

            string? nextLink = $"https://graph.microsoft.com/v1.0/servicePrincipals/{spId}/appRoleAssignedTo?$top=100";
            while (nextLink != null)
            {
                using var assignRequest = new HttpRequestMessage(HttpMethod.Get, nextLink);
                assignRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);
                var assignResponse = await httpClient.SendAsync(assignRequest, cancellationToken);
                var assignJson = await assignResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!assignResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to get app role assignments. Status: {Status}, Body: {Body}", assignResponse.StatusCode, assignJson);
                    return new List<UserDto>();
                }

                using var assignDoc = System.Text.Json.JsonDocument.Parse(assignJson);
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

                nextLink = assignDoc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
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
}
```

- [ ] Create `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs`. Only the namespace changes from the original:

```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Microsoft365.UserManagement;

public class MockGraphService : IGraphService
{
    private readonly ILogger<MockGraphService> _logger;

    public MockGraphService(ILogger<MockGraphService> logger)
    {
        _logger = logger;
    }

    public Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Mock GraphService: GetGroupMembersAsync called for group {GroupId}", groupId);
        return Task.FromResult(new List<UserDto>());
    }

    public Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Mock GraphService: SearchUsersAsync called for query '{Query}'", query);
        return Task.FromResult(new List<UserDto>());
    }

    public Task<List<UserDto>> GetAppRoleMembersAsync(string appRoleValue, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Mock GraphService: GetAppRoleMembersAsync called for role '{AppRoleValue}'", appRoleValue);
        return Task.FromResult(new List<UserDto>());
    }
}
```

- [ ] Delete `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` (use `git rm` or simply delete the file — the build will fail if it stays).

- [ ] Delete `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/MockGraphService.cs`.

- [ ] Verify: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj` compiles cleanly (the Application project is already a ProjectReference).

---

### task: update-di-registrations

Wire the two implementations into `AddMicrosoft365Adapter()` and gut the now-redundant registration block from `UserManagementModule`.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs`

- [ ] Edit `Microsoft365AdapterServiceCollectionExtensions.cs`. Add two new `using` directives and extend the if/else logic. The complete file after the edit:

```csharp
using Anela.Heblo.Adapters.Microsoft365.Photobank;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Microsoft365;

public static class Microsoft365AdapterServiceCollectionExtensions
{
    public static IServiceCollection AddMicrosoft365Adapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
        var bypassJwt = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, false);

        if (!useMockAuth && !bypassJwt)
        {
            services.AddScoped<IOutlookCalendarSync, OutlookCalendarSyncService>();
            services.AddHttpClient("MicrosoftGraph", _ => { })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                });
            services.AddScoped<IPhotobankGraphService, PhotobankGraphService>();
            services.AddScoped<IGraphService, GraphService>();
        }
        else
        {
            services.AddScoped<IGraphService, MockGraphService>();
        }

        return services;
    }
}
```

- [ ] Edit `UserManagementModule.cs`. Remove: the entire `if (useMockAuth || bypassJwtValidation)` block that registers `IGraphService`, the `services.AddHttpClient("MicrosoftGraph")` call, and the `using Microsoft.Graph;` import at line 12. Also remove `using Anela.Heblo.Application.Features.UserManagement.Services;` if it is no longer needed (check whether `MockGraphService`/`GraphService` types are still referenced — after this task they will not be). The complete file after the edit:

```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Features.UserManagement.Validators;
using Anela.Heblo.Domain.Features.Configuration;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.UserManagement;

public static class UserManagementModule
{
    public static IServiceCollection AddUserManagement(this IServiceCollection services, IConfiguration configuration)
    {
        // Cross-module contract: UserManagement implements Article's IArticleUserResolver via adapter.
        // IGraphService is registered by AddMicrosoft365Adapter() in the Adapters layer.
        services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>();

        services.AddScoped<IValidator<GetGroupMembersRequest>, GetGroupMembersRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetGroupMembersRequest, GetGroupMembersResponse>,
            ValidationBehavior<GetGroupMembersRequest, GetGroupMembersResponse>>();

        // Note: HttpContextAccessor must be registered in the API layer

        return services;
    }
}
```

Note: `ConfigurationConstants` is no longer referenced in `UserManagementModule` after this change — remove its using import too (`using Anela.Heblo.Domain.Features.Configuration;`) only if it does not appear elsewhere in the file. Based on the current file content it is only used for `ConfigurationConstants.USE_MOCK_AUTH` and `ConfigurationConstants.BYPASS_JWT_VALIDATION` in the removed block, so it should be removed.

- [ ] Run `dotnet build backend/backend.sln` and confirm zero errors before committing.

---

### task: update-project-files

Patch the two `.csproj` files: add `InternalsVisibleTo` to the adapter project (so tests can call `ParseMembersFromJson`), and strip the now-redundant `Microsoft.Graph` and `Microsoft.Identity.Web` PackageReferences from the Application project.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj`
- Modify: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

- [ ] Add `<InternalsVisibleTo Include="Anela.Heblo.Tests" />` to `Anela.Heblo.Adapters.Microsoft365.csproj`. The current file has no `InternalsVisibleTo` element at all. Add a new `<ItemGroup>` for it. Complete file after edit:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Anela.Heblo.Adapters.Microsoft365</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Graph" Version="5.92.0" />
    <PackageReference Include="Microsoft.Identity.Web" Version="3.14.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Anela.Heblo.Tests" />
  </ItemGroup>

</Project>
```

- [ ] Remove the `Microsoft.Graph` and `Microsoft.Identity.Web` PackageReferences from `Anela.Heblo.Application.csproj`. These are the two lines:

```xml
    <PackageReference Include="Microsoft.Graph" Version="5.92.0" />
    <PackageReference Include="Microsoft.Identity.Web" Version="3.14.1" />
```

After removing them the `<ItemGroup>` containing those two lines will be empty — remove the whole `<ItemGroup>` block too. The affected section currently sits between `<PackageReference Include="Microsoft.FeatureManagement" .../>` and `<PackageReference Include="Polly" .../>`. The remaining file keeps all other PackageReferences intact.

- [ ] Run `dotnet build backend/backend.sln` — must still be zero errors. This confirms Application no longer needs those packages.

---

### task: fix-unit-tests

The two DI-registration tests in `GraphServiceTests.cs` call `AddUserManagement()` to verify that `IGraphService` is wired up. After this refactor, registration moves to `AddMicrosoft365Adapter()`. Update those two tests so they call both methods (or just `AddMicrosoft365Adapter()`). Also update the `using` directives because `GraphService` and `MockGraphService` have moved namespaces.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`

- [ ] Add `using Anela.Heblo.Adapters.Microsoft365;` and `using Anela.Heblo.Adapters.Microsoft365.UserManagement;` to the top of the file.

- [ ] Remove `using Anela.Heblo.Application.Features.UserManagement.Services;` — `GraphService` and `MockGraphService` no longer live there. The test still constructs `new GraphService(...)` directly, so it must import from the new namespace.

  The updated using block at the top of the file:

```csharp
using System.Net;
using Anela.Heblo.Adapters.Microsoft365;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.UserManagement;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Moq;
```

  Wait — after the move `GraphService` lives in `Anela.Heblo.Adapters.Microsoft365.UserManagement`, so `using Anela.Heblo.Application.Features.UserManagement.Services;` should be **replaced** by `using Anela.Heblo.Adapters.Microsoft365.UserManagement;`. Keep `IGraphService` import via `Anela.Heblo.Application.Features.UserManagement.Services` (that namespace still holds `IGraphService.cs`). The correct final using block:

```csharp
using System.Net;
using Anela.Heblo.Adapters.Microsoft365;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.UserManagement;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Moq;
```

  (`IGraphService` is in `Anela.Heblo.Application.Features.UserManagement.Services` — the `using` for that namespace stays.)

- [ ] Update `AddUserManagement_ProductionBranch_RegistersMicrosoftGraphNamedClient_AndResolvesGraphService`. The test must call `AddMicrosoft365Adapter(configuration)` in addition to `AddUserManagement(configuration)` so that `IGraphService` gets registered. Also add the services that `GraphService` constructor requires (`ITokenAcquisition`):

```csharp
[Fact]
public void AddUserManagement_ProductionBranch_RegistersMicrosoftGraphNamedClient_AndResolvesGraphService()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddMemoryCache();
    services.AddSingleton(Mock.Of<ITokenAcquisition>());
    var configuration = new ConfigurationBuilder().Build(); // no mock-auth keys => production branch
    services.AddSingleton<IConfiguration>(configuration);

    // Act
    services.AddMicrosoft365Adapter(configuration);
    services.AddUserManagement(configuration);

    // Assert
    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();
    var resolved = scope.ServiceProvider.GetRequiredService<IGraphService>();
    resolved.Should().BeOfType<GraphService>();

    var factory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
    var client = factory.CreateClient("MicrosoftGraph");
    client.Should().NotBeNull();
}
```

- [ ] Update `AddUserManagement_MockBranch_RegistersMockGraphService`. Same pattern — call `AddMicrosoft365Adapter(configuration)` so that `MockGraphService` is registered via the adapter's `else` branch:

```csharp
[Fact]
public void AddUserManagement_MockBranch_RegistersMockGraphService()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddMemoryCache();
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UseMockAuth"] = "true"
        })
        .Build();

    // Act
    services.AddMicrosoft365Adapter(configuration);
    services.AddUserManagement(configuration);

    // Assert
    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();
    var resolved = scope.ServiceProvider.GetRequiredService<IGraphService>();
    resolved.Should().BeOfType<MockGraphService>();
}
```

- [ ] Run all tests in the UserManagement test suite:

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UserManagement"
```

All tests must pass with zero failures.

- [ ] Run `dotnet format backend/backend.sln --verify-no-changes` to confirm no formatting drift. If it reports changes, run `dotnet format backend/backend.sln` and re-check.

- [ ] Final sanity check — full solution build:

```
dotnet build backend/backend.sln
```

Zero errors, zero warnings about missing types.
