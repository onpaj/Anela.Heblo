using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Anela.Heblo.Application.Features.UserManagement.Services;

public class GraphService : IGraphService
{
    private readonly GraphServiceClient _graphServiceClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GraphService> _logger;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(20); // 20 minutes cache

    public GraphService(
        GraphServiceClient graphServiceClient,
        IMemoryCache cache,
        ILogger<GraphService> logger)
    {
        _graphServiceClient = graphServiceClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"group_members_{groupId}";

        // Try to get from cache first
        if (_cache.TryGetValue(cacheKey, out List<UserDto>? cachedMembers) && cachedMembers != null)
        {
            _logger.LogDebug("Returning cached group members for group {GroupId}", groupId);
            return cachedMembers;
        }

        try
        {
            _logger.LogInformation("Fetching group members from Microsoft Graph for group {GroupId}", groupId);

            // Get group members from Microsoft Graph
            var membersResponse = await _graphServiceClient.Groups[groupId].Members
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Select = new[] { "id", "displayName", "mail" };
                    requestConfiguration.QueryParameters.Filter = "accountEnabled eq true";
                }, cancellationToken);

            var members = new List<UserDto>();

            if (membersResponse?.Value != null)
            {
                foreach (var member in membersResponse.Value)
                {
                    if (member is User user)
                    {
                        members.Add(new UserDto
                        {
                            Id = user.Id ?? string.Empty,
                            DisplayName = user.DisplayName ?? string.Empty,
                            Email = user.Mail ?? user.UserPrincipalName ?? string.Empty
                        });
                    }
                }
            }

            // Cache the results
            _cache.Set(cacheKey, members, _cacheExpiration);

            _logger.LogInformation("Successfully fetched {Count} group members for group {GroupId}", members.Count, groupId);
            return members;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching group members for group {GroupId}", groupId);

            // Return empty list on error to avoid breaking the application
            return new List<UserDto>();
        }
    }
}