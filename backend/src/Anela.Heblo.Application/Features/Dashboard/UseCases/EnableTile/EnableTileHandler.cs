using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;

internal sealed class EnableTileHandler : IRequestHandler<EnableTileRequest, EnableTileResponse>
{
    private readonly IUserDashboardSettingsMutator _mutator;

    public EnableTileHandler(IUserDashboardSettingsMutator mutator)
    {
        _mutator = mutator;
    }

    public async Task<EnableTileResponse> Handle(EnableTileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.TileId))
        {
            return new EnableTileResponse(ErrorCodes.RequiredFieldMissing);
        }

        await _mutator.MutateAsync(
            request.UserId,
            request.TileId,
            onTileFound: static (_, tile) => tile.IsVisible = true,
            onTileMissing: (settings, resolvedUserId) =>
            {
                var maxOrder = settings.Tiles.Any() ? settings.Tiles.Max(t => t.DisplayOrder) : -1;
                return new UserDashboardTile
                {
                    UserId = resolvedUserId,
                    TileId = request.TileId,
                    IsVisible = true,
                    DisplayOrder = maxOrder + 1,
                    DashboardSettings = settings
                };
            },
            cancellationToken);

        return new EnableTileResponse();
    }
}
