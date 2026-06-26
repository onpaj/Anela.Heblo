using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SetUserCanPack;

public class SetUserCanPackHandler : IRequestHandler<SetUserCanPackRequest, SetUserCanPackResponse>
{
    private readonly IAuthorizationRepository _repo;
    public SetUserCanPackHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<SetUserCanPackResponse> Handle(SetUserCanPackRequest request, CancellationToken ct)
    {
        var user = await _repo.GetUserByIdAsync(request.UserId, ct);
        if (user is null)
            return new SetUserCanPackResponse(ErrorCodes.AuthorizationUserNotFound);

        user.CanPack = request.CanPack;
        await _repo.SaveChangesAsync(ct);
        return new SetUserCanPackResponse();
    }
}
