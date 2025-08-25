using System.Security.Claims;

namespace Anela.Heblo.Domain.Features.Users;

public interface ICurrentUserService
{
    CurrentUser GetCurrentUser();
    ClaimsPrincipal GetCurrentPrincipal();
}