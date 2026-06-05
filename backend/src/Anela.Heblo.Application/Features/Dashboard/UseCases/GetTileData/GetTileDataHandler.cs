using System.Collections.Concurrent;
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;

public class GetTileDataHandler : IRequestHandler<GetTileDataRequest, GetTileDataResponse>
{
    private readonly IMediator _mediator;
    private readonly ITileRegistry _tileRegistry;
    private readonly DashboardOptions _dashboardOptions;
    private readonly ILogger<GetTileDataHandler> _logger;
    private readonly ICurrentUserService _currentUserService;

    public GetTileDataHandler(
        IMediator mediator,
        ITileRegistry tileRegistry,
        IOptions<DashboardOptions> dashboardOptions,
        ILogger<GetTileDataHandler> logger,
        ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _tileRegistry = tileRegistry;
        _dashboardOptions = dashboardOptions.Value;
        _logger = logger;
        _currentUserService = currentUserService;
    }

    public async Task<GetTileDataResponse> Handle(GetTileDataRequest request, CancellationToken cancellationToken)
    {
        var settingsResponse = await _mediator.Send(
            new GetUserSettingsRequest(),
            cancellationToken);

        var visibleTiles = settingsResponse.Settings.Tiles
            .Where(t => t.IsVisible)
            .OrderBy(t => t.DisplayOrder)
            .ToList();

        var results = new ConcurrentBag<(int Index, TileData Data)>();

        await Parallel.ForEachAsync(
            visibleTiles.Select((tile, index) => (tile, index)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _dashboardOptions.MaxConcurrentTileLoads,
                CancellationToken = cancellationToken
            },
            async (item, ct) =>
            {
                var (tileSettings, index) = item;

                try
                {
                    var tile = _tileRegistry.GetTileMetadata(tileSettings.TileId);
                    if (tile == null)
                    {
                        results.Add((index, new TileData
                        {
                            TileId = tileSettings.TileId,
                            Title = "Error",
                            Description = $"Tile '{tileSettings.TileId}' not found",
                            Size = TileSize.Small,
                            Category = TileCategory.Error,
                            Data = new { Error = $"Tile '{tileSettings.TileId}' not found" }
                        }));
                        return;
                    }

                    var data = await _tileRegistry.GetTileDataAsync(tileSettings.TileId, request.TileParameters);

                    results.Add((index, new TileData
                    {
                        TileId = tile.TileId,
                        Title = tile.Title,
                        Description = tile.Description,
                        Size = tile.Size,
                        Category = tile.Category,
                        DefaultEnabled = tile.DefaultEnabled,
                        AutoShow = tile.AutoShow,
                        RequiredPermissions = tile.RequiredPermissions,
                        Data = data
                    }));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load tile {TileId}", tileSettings.TileId);
                    results.Add((index, new TileData
                    {
                        TileId = tileSettings.TileId,
                        Title = "Error",
                        Description = $"Failed to load tile '{tileSettings.TileId}'",
                        Size = TileSize.Small,
                        Category = TileCategory.Error,
                        Data = new { Error = "An error occurred while loading this tile." }
                    }));
                }
            });

        var tiles = results
            .OrderBy(r => r.Index)
            .Select(r => r.Data)
            .Select(td => new DashboardTileDto
            {
                TileId = td.TileId,
                Title = td.Title,
                Description = td.Description,
                Size = td.Size.ToString(),
                Category = td.Category.ToString(),
                DefaultEnabled = td.DefaultEnabled,
                AutoShow = td.AutoShow,
                RequiredPermissions = td.RequiredPermissions,
                Data = td.Data
            })
            .ToArray();

        return new GetTileDataResponse { Tiles = tiles };
    }
}
