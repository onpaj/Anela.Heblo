using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SetUserActive;

public class SetUserActiveHandler : IRequestHandler<SetUserActiveRequest, SetUserActiveResponse>
{
    private readonly IAuthorizationRepository _repo;
    public SetUserActiveHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<SetUserActiveResponse> Handle(SetUserActiveRequest request, CancellationToken ct)
    {
        var user = await _repo.GetUserByIdAsync(request.UserId, ct);
        if (user is null)
            return new SetUserActiveResponse(ErrorCodes.AuthorizationUserNotFound);

        user.IsActive = request.IsActive;
        await _repo.SaveChangesAsync(ct);
        return new SetUserActiveResponse();
    }
}
