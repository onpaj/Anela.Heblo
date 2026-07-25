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
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the caller lacks permission to read the specified group.
    /// </exception>
    Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default);

    /// <exception cref="GraphServiceAuthException">
    /// Thrown when token acquisition fails (MSAL auth error).
    /// </exception>
    Task<List<UserDto>> GetAppRoleMembersAsync(string appRoleValue, CancellationToken cancellationToken = default);
}
