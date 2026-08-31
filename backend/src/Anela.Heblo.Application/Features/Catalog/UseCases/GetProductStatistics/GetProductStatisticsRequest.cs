using Anela.Heblo.Application.Features.Catalog.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;

public class GetProductStatisticsRequest : IRequest<GetProductStatisticsResponse>
{
    public List<string> ProductCodes { get; set; } = new();

    public ProductStatisticsMetric Metric { get; set; } = ProductStatisticsMetric.Sales;

    /// <summary>Inclusive lower bound, "yyyy-MM".</summary>
    public string DateFrom { get; set; } = null!;

    /// <summary>Inclusive upper bound, "yyyy-MM".</summary>
    public string DateTo { get; set; } = null!;
}
