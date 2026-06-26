using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetMe;

public class GetMeHandler : IRequestHandler<GetMeRequest, GetMeResponse>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionResolver _resolver;

    public GetMeHandler(ICurrentUserService currentUser, IPermissionResolver resolver)
    {
        _currentUser = currentUser;
        _resolver = resolver;
    }

    public async Task<GetMeResponse> Handle(GetMeRequest request, CancellationToken ct)
    {
        var user = _currentUser.GetCurrentUser();

        if (_currentUser.IsInRole(AccessRoles.SuperUser))
        {
            return new GetMeResponse
            {
                Email = user.Email,
                DisplayName = user.Name,
                IsSuperUser = true,
                Permissions = AccessMatrix.AllRoleValues().Append(AccessRoles.Base).ToList(),
                Groups = new List<string>(),
            };
        }

        var resolved = await _resolver.ResolveAsync(user.Id ?? string.Empty, user.Email, user.Name, ct);
        return new GetMeResponse
        {
            Email = user.Email,
            DisplayName = user.Name,
            IsSuperUser = false,
            Permissions = resolved.Permissions.ToList(),
            Groups = resolved.Groups.ToList(),
        };
    }
}
