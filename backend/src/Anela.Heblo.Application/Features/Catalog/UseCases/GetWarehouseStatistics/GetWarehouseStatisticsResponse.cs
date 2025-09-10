using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetWarehouseStatistics;

public class GetWarehouseStatisticsResponse : BaseResponse
{
    public decimal TotalQuantity { get; set; }
    public double TotalWeight { get; set; }
    public double WarehouseCapacityKg { get; set; } = 8500;
    public double WarehouseUtilizationPercentage { get; set; }
    public int TotalProductCount { get; set; }
    public DateTime LastUpdated { get; set; }

    public GetWarehouseStatisticsResponse() : base() { }

    public GetWarehouseStatisticsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) 
        : base(errorCode, parameters) { }
}