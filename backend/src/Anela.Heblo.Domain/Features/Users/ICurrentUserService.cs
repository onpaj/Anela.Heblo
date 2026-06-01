namespace Anela.Heblo.Domain.Features.Users;

public interface ICurrentUserService
{
    CurrentUser GetCurrentUser();
    bool IsInRole(string role);
}