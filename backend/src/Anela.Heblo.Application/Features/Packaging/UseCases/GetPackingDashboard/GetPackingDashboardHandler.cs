using Anela.Heblo.Application.Features.Packaging.Contracts;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackingDashboard;

public class GetPackingDashboardHandler : IRequestHandler<GetPackingDashboardRequest, GetPackingDashboardResponse>
{
    private readonly IPackageRepository _repo;
    private readonly IPackingOrderCountSource _packingOrderClient;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<GetPackingDashboardHandler> _logger;

    public GetPackingDashboardHandler(
        IPackageRepository repo,
        IPackingOrderCountSource packingOrderClient,
        TimeProvider timeProvider,
        ICurrentUserService currentUserService,
        ILogger<GetPackingDashboardHandler> logger)
    {
        _repo = repo;
        _packingOrderClient = packingOrderClient;
        _timeProvider = timeProvider;
        _currentUserService = currentUserService;
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
        var canViewPackerBreakdown = PackerStatisticsAccess.CanViewBreakdown(_currentUserService);
        var visiblePackers = canViewPackerBreakdown
            ? byPacker
            : Array.Empty<PackerPackingSummary>();

        int? ordersBeingPackedCount = null;
        int? ordersBeingProcessedCount = null;
        DateTimeOffset? ordersBeingPackedCountLastSync = null;
        try
        {
            ordersBeingPackedCount = await _packingOrderClient.GetOrdersBeingPackedCountAsync(cancellationToken);
            ordersBeingPackedCountLastSync = now;
            ordersBeingProcessedCount = await _packingOrderClient.GetOrdersBeingProcessedCountAsync(cancellationToken);
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
            CanViewPackerBreakdown = canViewPackerBreakdown,
            PackedByPacker = visiblePackers
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
