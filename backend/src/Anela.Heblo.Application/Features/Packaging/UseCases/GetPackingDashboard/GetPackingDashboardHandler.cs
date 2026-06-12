using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Packaging;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackingDashboard;

public class GetPackingDashboardHandler : IRequestHandler<GetPackingDashboardRequest, GetPackingDashboardResponse>
{
    private readonly IPackageRepository _repo;
    private readonly IPackingOrderClient _packingOrderClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetPackingDashboardHandler> _logger;

    public GetPackingDashboardHandler(
        IPackageRepository repo,
        IPackingOrderClient packingOrderClient,
        TimeProvider timeProvider,
        ILogger<GetPackingDashboardHandler> logger)
    {
        _repo = repo;
        _packingOrderClient = packingOrderClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GetPackingDashboardResponse> Handle(
        GetPackingDashboardRequest request,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetLocalNow();
        var start = new DateTimeOffset(now.Date, now.Offset);
        var end = start.AddDays(1);

        var (total, byPacker) = await _repo.GetPackedTodayByPackerAsync(start, end, cancellationToken);

        int? ordersBeingPackedCount = null;
        int? ordersBeingProcessedCount = null;
        DateTimeOffset? ordersBeingPackedCountLastSync = null;
        try
        {
            ordersBeingPackedCount = await _packingOrderClient.GetOrdersBeingPackedCountAsync(cancellationToken);
            ordersBeingProcessedCount = await _packingOrderClient.GetOrdersBeingProcessedCountAsync(cancellationToken);
            ordersBeingPackedCountLastSync = now;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve order counts from Shoptet; dashboard will show null");
        }

        return new GetPackingDashboardResponse
        {
            OrdersBeingPackedCount = ordersBeingPackedCount,
            OrdersBeingProcessedCount = ordersBeingProcessedCount,
            OrdersBeingPackedCountLastSync = ordersBeingPackedCountLastSync,
            TotalOrdersPackedToday = total,
            PackedByPacker = byPacker
                .Select(p => new PackerStatsDto
                {
                    PackerId = p.PackedByUserId,
                    PackerName = p.PackedBy ?? "Neznámý",
                    OrderCount = p.DistinctOrderCount,
                })
                .ToList(),
        };
    }
}
