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
        _logger.LogInformation("Mock GraphService: Returning mock group members for group {GroupId}", groupId);

        var mockMembers = new List<UserDto>
        {
            new UserDto
            {
                Id = "mock-user-1",
                DisplayName = "Mock User 1",
                Email = "mock.user1@anela-heblo.com"
            },
            new UserDto
            {
                Id = "mock-user-2",
                DisplayName = "Mock User 2",
                Email = "mock.user2@anela-heblo.com"
            },
            new UserDto
            {
                Id = "mock-user-3",
                DisplayName = "Mock Administrator",
                Email = "mock.admin@anela-heblo.com"
            }
        };

        return Task.FromResult(mockMembers);
    }
}