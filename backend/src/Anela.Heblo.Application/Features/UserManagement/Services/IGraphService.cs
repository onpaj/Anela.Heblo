using Anela.Heblo.Application.Features.UserManagement.Contracts;

namespace Anela.Heblo.Application.Features.UserManagement.Services;

public interface IGraphService
{
    Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default);
}