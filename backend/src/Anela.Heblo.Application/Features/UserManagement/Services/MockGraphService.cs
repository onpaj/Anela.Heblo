using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.UserManagement.Services;

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
