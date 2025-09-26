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
            // Validate groupId
            if (string.IsNullOrWhiteSpace(groupId))
            {
                _logger.LogWarning("GroupId is null or empty for MS Entra group lookup");
                return new List<UserDto>();
            }

            _logger.LogInformation("GroupId validation passed. GroupId: {GroupId}", groupId);

            // Acquire Graph API token
            var scopes = new[] { "https://graph.microsoft.com/GroupMember.Read.All" };
            _logger.LogInformation("Attempting to acquire MS Graph token with scopes: {Scopes}", string.Join(", ", scopes));
            
            string graphToken;
            try
            {
                var tokenStart = DateTime.UtcNow;
                graphToken = await _tokenAcquisition.GetAccessTokenForUserAsync(scopes);
                var tokenDuration = DateTime.UtcNow - tokenStart;
                _logger.LogInformation("Successfully acquired MS Graph token in {Duration}ms", tokenDuration.TotalMilliseconds);
                
                // Log token length for troubleshooting (not the actual token)
                _logger.LogDebug("Token acquired with length: {TokenLength} characters", graphToken?.Length ?? 0);
            }
            catch (MsalException msalEx)
            {
                _logger.LogError(msalEx, "Failed to acquire Graph API token. MSAL Error: {ErrorCode} - {ErrorDescription}. GroupId: {GroupId}, Scopes: {Scopes}", 
                    msalEx.ErrorCode, msalEx.Message, groupId, string.Join(", ", scopes));
                return new List<UserDto>();
            }
            catch (Exception tokenEx)
            {
                _logger.LogError(tokenEx, "Unexpected error acquiring Graph API token for groupId: {GroupId}, scopes: {Scopes}", groupId, string.Join(", ", scopes));
                return new List<UserDto>();
            }

            // Get group members from Microsoft Graph
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);

            var requestUrl = $"https://graph.microsoft.com/v1.0/groups/{groupId}/members?$select=id,displayName,mail,userPrincipalName";
            _logger.LogInformation("Making MS Graph API request to: {RequestUrl}", requestUrl);
            
            var apiCallStart = DateTime.UtcNow;
            var response = await httpClient.GetAsync(requestUrl, cancellationToken);
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
            
            using var jsonDocument = System.Text.Json.JsonDocument.Parse(responseContent);

            var members = new List<UserDto>();
            var totalMembers = 0;
            var userMembers = 0;

            if (jsonDocument.RootElement.TryGetProperty("value", out var valueElement) && valueElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                _logger.LogInformation("Processing MS Graph response array with {ArrayLength} total members", valueElement.GetArrayLength());
                
                foreach (var memberElement in valueElement.EnumerateArray())
                {
                    totalMembers++;
                    
                    // Check if this is a user object (has userPrincipalName or @odata.type indicates user)
                    if (memberElement.TryGetProperty("@odata.type", out var odataType) &&
                        odataType.GetString()?.Contains("user", StringComparison.OrdinalIgnoreCase) == true ||
                        memberElement.TryGetProperty("userPrincipalName", out _))
                    {
                        userMembers++;
                        
                        var id = memberElement.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                        var displayName = memberElement.TryGetProperty("displayName", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                        var mail = memberElement.TryGetProperty("mail", out var mailProp) ? mailProp.GetString() : null;
                        var userPrincipalName = memberElement.TryGetProperty("userPrincipalName", out var upnProp) ? upnProp.GetString() : null;

                        _logger.LogDebug("Processing user member: Id={UserId}, DisplayName={DisplayName}, HasMail={HasMail}, HasUPN={HasUPN}", 
                            id, displayName, !string.IsNullOrEmpty(mail), !string.IsNullOrEmpty(userPrincipalName));

                        members.Add(new UserDto
                        {
                            Id = id,
                            DisplayName = displayName,
                            Email = mail ?? userPrincipalName ?? string.Empty
                        });
                    }
                    else
                    {
                        var memberType = memberElement.TryGetProperty("@odata.type", out var type) ? type.GetString() : "unknown";
                        _logger.LogDebug("Skipping non-user member with type: {MemberType}", memberType);
                    }
                }
                
                _logger.LogInformation("Processed {TotalMembers} total members, {UserMembers} user members for group {GroupId}", 
                    totalMembers, userMembers, groupId);
            }
            else
            {
                _logger.LogWarning("MS Graph response does not contain expected 'value' array property for group {GroupId}", groupId);
            }

            // Cache the results
            _cache.Set(cacheKey, members, _cacheExpiration);
            _logger.LogInformation("Cached {Count} group members for group {GroupId} with expiration {CacheExpiration}", 
                members.Count, groupId, _cacheExpiration);

            _logger.LogInformation("Successfully completed GetGroupMembersAsync for group {GroupId}. Final result count: {Count}", groupId, members.Count);
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