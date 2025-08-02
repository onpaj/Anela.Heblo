using System.Security.Claims;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.AspNetCore.Http;

namespace Anela.Heblo.Application.Features.Users;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public CurrentUser GetCurrentUser()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var isAuthenticated = user?.Identity?.IsAuthenticated ?? false;

        var name = user?.Identity?.Name
                   ?? user?.FindFirst(ClaimTypes.Name)?.Value
                   ?? user?.FindFirst("name")?.Value
                   ?? (isAuthenticated ? "Unknown User" : "Anonymous");

        var id = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? user?.FindFirst("sub")?.Value;

        var email = user?.FindFirst(ClaimTypes.Email)?.Value
                    ?? user?.FindFirst("email")?.Value;

        return new CurrentUser(
            Id: id,
            Name: name,
            Email: email,
            IsAuthenticated: isAuthenticated
        );
    }
}