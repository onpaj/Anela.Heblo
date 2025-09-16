using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Microsoft.Identity.Client;

namespace Anela.Heblo.Application.Features.UserManagement.Services;

public class GraphService : IGraphService
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GraphService> _logger;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(20);

    public GraphService(
        ITokenAcquisition tokenAcquisition,
        IMemoryCache cache,
        ILogger<GraphService> logger)
    {
        _tokenAcquisition = tokenAcquisition;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"group_members_{groupId}";

        // Try to get from cache first
        if (_cache.TryGetValue(cacheKey, out List<UserDto>? cachedMembers) && cachedMembers != null)
        {
            return cachedMembers;
        }

        try
        {
            // Validate groupId
            if (string.IsNullOrWhiteSpace(groupId))
            {
                _logger.LogWarning("GroupId is null or empty");
                return new List<UserDto>();
            }

            // Acquire Graph API token
            var scopes = new[] { "https://graph.microsoft.com/GroupMember.Read.All" };
            string graphToken;
            try
            {
                graphToken = await _tokenAcquisition.GetAccessTokenForUserAsync(scopes);
            }
            catch (MsalException msalEx)
            {
                _logger.LogError(msalEx, "Failed to acquire Graph API token. MSAL Error: {ErrorCode} - {ErrorDescription}", msalEx.ErrorCode, msalEx.Message);
                return new List<UserDto>();
            }
            catch (Exception tokenEx)
            {
                _logger.LogError(tokenEx, "Unexpected error acquiring Graph API token for scopes: {Scopes}", string.Join(", ", scopes));
                return new List<UserDto>();
            }

            // Get group members from Microsoft Graph
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);

            var requestUrl = $"https://graph.microsoft.com/v1.0/groups/{groupId}/members?$select=id,displayName,mail,userPrincipalName";
            var response = await httpClient.GetAsync(requestUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Microsoft Graph API call failed. Status: {StatusCode}, Content: {Content}", response.StatusCode, errorContent);
                return new List<UserDto>();
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var jsonDocument = System.Text.Json.JsonDocument.Parse(responseContent);

            var members = new List<UserDto>();

            if (jsonDocument.RootElement.TryGetProperty("value", out var valueElement) && valueElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var memberElement in valueElement.EnumerateArray())
                {
                    // Check if this is a user object (has userPrincipalName or @odata.type indicates user)
                    if (memberElement.TryGetProperty("@odata.type", out var odataType) &&
                        odataType.GetString()?.Contains("user", StringComparison.OrdinalIgnoreCase) == true ||
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
            }

            // Cache the results
            _cache.Set(cacheKey, members, _cacheExpiration);

            _logger.LogInformation("Successfully fetched {Count} group members for group {GroupId}", members.Count, groupId);
            return members;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
        {
            _logger.LogError(odataEx, "Microsoft Graph OData error fetching group members for group {GroupId}. Error: {ErrorCode}", groupId, odataEx.Error?.Code);
            return new List<UserDto>();
        }
        catch (UnauthorizedAccessException authEx)
        {
            _logger.LogError(authEx, "Unauthorized access when fetching group members for group {GroupId}. Check application permissions.", groupId);
            return new List<UserDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching group members for group {GroupId}", groupId);
            return new List<UserDto>();
        }
    }

}