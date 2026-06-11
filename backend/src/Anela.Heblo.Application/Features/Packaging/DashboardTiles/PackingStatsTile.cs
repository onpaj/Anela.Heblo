using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.DashboardTiles;

[TileId("packingstats")]
public class PackingStatsTile : ITile
{
    private readonly IPackageRepository _repo;
    private readonly IPackingOrderClient _packingOrderClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PackingStatsTile> _logger;

    public string Title => "Stav balení";
    public string Description => "Balí se, zabaleno dnes a statistiky baličů";
    public TileSize Size => TileSize.Medium;
    public TileCategory Category => TileCategory.Orders;
    public bool DefaultEnabled => true;
    public bool AutoShow => true;
    public string[] RequiredPermissions => Array.Empty<string>();

    public PackingStatsTile(
        IPackageRepository repo,
        IPackingOrderClient packingOrderClient,
        TimeProvider timeProvider,
        ILogger<PackingStatsTile> logger)
    {
        _repo = repo;
        _packingOrderClient = packingOrderClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<object> LoadDataAsync(
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var now = _timeProvider.GetLocalNow();
            var start = new DateTimeOffset(now.Date, now.Offset);
            var end = start.AddDays(1);

            var (total, byPacker) = await _repo.GetPackedTodayByPackerAsync(start, end, cancellationToken);

            int? ordersBeingPackedCount = null;
            try
            {
                ordersBeingPackedCount = await _packingOrderClient.GetOrdersBeingPackedCountAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve orders-being-packed count from Shoptet");
            }

            var packers = byPacker
                .Select(p => new
                {
                    packerId = p.PackedByUserId,
                    packerName = p.PackedBy ?? "Neznámý",
                    orderCount = p.DistinctOrderCount,
                })
                .ToList();

            return new
            {
                status = "success",
                data = new
                {
                    ordersBeingPackedCount,
                    totalOrdersPackedToday = total,
                    packedByPacker = packers,
                },
                metadata = new
                {
                    lastUpdated = now,
                    source = "PackageRepository"
                },
                drillDown = new
                {
                    filters = new { },
                    enabled = true,
                    tooltip = "Přejít do modulu Balení"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load packing stats tile data");
            return new
            {
                status = "error",
                error = "Nepodařilo se načíst data balení"
            };
        }
    }
}
