using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;

internal sealed class DisableTileHandler : IRequestHandler<DisableTileRequest, DisableTileResponse>
{
    private readonly IUserDashboardSettingsMutator _mutator;

    public DisableTileHandler(IUserDashboardSettingsMutator mutator)
    {
        _mutator = mutator;
    }

    public async Task<DisableTileResponse> Handle(DisableTileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.TileId))
        {
            return new DisableTileResponse(ErrorCodes.RequiredFieldMissing);
        }

        await _mutator.MutateAsync(
            request.UserId,
            request.TileId,
            onTileFound: static (_, tile) => tile.IsVisible = false,
            onTileMissing: null,
            cancellationToken);

        return new DisableTileResponse();
    }
}
