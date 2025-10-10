using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetAvailableTiles;

public class GetAvailableTilesHandler : IRequestHandler<GetAvailableTilesRequest, GetAvailableTilesResponse>
{
    private readonly ITileRegistry _tileRegistry;

    public GetAvailableTilesHandler(ITileRegistry tileRegistry)
    {
        _tileRegistry = tileRegistry;
    }

    public Task<GetAvailableTilesResponse> Handle(GetAvailableTilesRequest request, CancellationToken cancellationToken)
    {
        var tiles = _tileRegistry.GetAvailableTiles();
        
        var result = tiles.Select(t => new DashboardTileDto
        {
            TileId = t.GetTileId(),
            Title = t.Title,
            Description = t.Description,
            Size = t.Size.ToString(),
            Category = t.Category.ToString(),
            DefaultEnabled = t.DefaultEnabled,
            AutoShow = t.AutoShow,
            RequiredPermissions = t.RequiredPermissions
        });

        return Task.FromResult(new GetAvailableTilesResponse
        {
            Tiles = result
        });
    }
}