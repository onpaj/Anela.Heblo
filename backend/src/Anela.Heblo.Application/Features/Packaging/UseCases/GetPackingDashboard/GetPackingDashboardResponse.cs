using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackingDashboard;

public class GetPackingDashboardResponse : BaseResponse
{
    public int? OrdersBeingPackedCount { get; set; }
    public int? OrdersBeingProcessedCount { get; set; }
    public DateTimeOffset? OrdersBeingPackedCountLastSync { get; set; }
    public int TotalOrdersPackedToday { get; set; }
    public List<PackerStatsDto> PackedByPacker { get; set; } = new();

    public GetPackingDashboardResponse() { }
    public GetPackingDashboardResponse(ErrorCodes errorCode) : base(errorCode) { }
}

public class PackerStatsDto
{
    public Guid? PackerId { get; set; }
    public string PackerName { get; set; } = string.Empty;
    public int OrderCount { get; set; }
}
