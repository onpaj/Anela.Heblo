using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.UpdateUser;

public class UpdateUserHandler : IRequestHandler<UpdateUserRequest, UpdateUserResponse>
{
    private readonly IAuthorizationRepository _repo;
    private readonly IPermissionResolver _resolver;

    public UpdateUserHandler(IAuthorizationRepository repo, IPermissionResolver resolver)
    {
        _repo = repo;
        _resolver = resolver;
    }

    public async Task<UpdateUserResponse> Handle(UpdateUserRequest request, CancellationToken ct)
    {
        var user = await _repo.GetUserByIdAsync(request.UserId, ct);
        if (user is null)
            return new UpdateUserResponse(ErrorCodes.AuthorizationUserNotFound);

        user.DisplayName = request.DisplayName.Trim();
        user.Email = request.Email?.Trim() ?? string.Empty;
        user.CanPack = request.CanPack;
        await _repo.SaveChangesAsync(ct);

        if (user.EntraObjectId is not null)
            _resolver.InvalidateCache(user.EntraObjectId);

        return new UpdateUserResponse();
    }
}
