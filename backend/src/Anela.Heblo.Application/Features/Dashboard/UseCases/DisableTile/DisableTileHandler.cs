using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;

internal sealed class DisableTileHandler : IRequestHandler<DisableTileRequest, DisableTileResponse>
{
    private readonly IUserDashboardSettingsMutator _mutator;
    private readonly ICurrentUserService _currentUserService;

    public DisableTileHandler(IUserDashboardSettingsMutator mutator, ICurrentUserService currentUserService)
    {
        _mutator = mutator;
        _currentUserService = currentUserService;
    }

    public async Task<DisableTileResponse> Handle(DisableTileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.TileId))
        {
            return new DisableTileResponse(ErrorCodes.RequiredFieldMissing);
        }

        var currentUser = _currentUserService.GetCurrentUser();
        var userId = currentUser.Id;

        await _mutator.MutateAsync(
            userId,
            request.TileId,
            onTileFound: static (_, tile) => tile.IsVisible = false,
            onTileMissing: null,
            cancellationToken);

        return new DisableTileResponse();
    }
}
