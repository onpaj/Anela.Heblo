using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SetUserActive;

public class SetUserActiveHandler : IRequestHandler<SetUserActiveRequest, SetUserActiveResponse>
{
    private readonly IAuthorizationRepository _repo;
    private readonly IPermissionResolver _resolver;

    public SetUserActiveHandler(IAuthorizationRepository repo, IPermissionResolver resolver)
    {
        _repo = repo;
        _resolver = resolver;
    }

    public async Task<SetUserActiveResponse> Handle(SetUserActiveRequest request, CancellationToken ct)
    {
        var user = await _repo.GetUserByIdAsync(request.UserId, ct);
        if (user is null)
            return new SetUserActiveResponse(ErrorCodes.AuthorizationUserNotFound);

        user.IsActive = request.IsActive;
        await _repo.SaveChangesAsync(ct);
        if (user.EntraObjectId is not null)
            _resolver.InvalidateCache(user.EntraObjectId);
        return new SetUserActiveResponse();
    }
}
