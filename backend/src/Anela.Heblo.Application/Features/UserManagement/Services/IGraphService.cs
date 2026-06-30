using Anela.Heblo.Application.Features.UserManagement.Contracts;

namespace Anela.Heblo.Application.Features.UserManagement.Services;

public interface IGraphService
{
    /// <exception cref="GraphServiceAuthException">
    /// Thrown when token acquisition fails (MSAL auth error).
    /// </exception>
    /// <exception cref="GraphServiceException">
    /// Thrown when Microsoft Graph returns an OData error response.
    /// </exception>
    Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default);
    Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);
    Task<List<UserDto>> GetAppRoleMembersAsync(string appRoleValue, CancellationToken cancellationToken = default);
}
