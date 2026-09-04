using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;

public class GetProductStatisticsResponse : BaseResponse
{
    /// <summary>
    /// Dense ascending month keys ("yyyy-MM") shared by every series in Products.
    /// </summary>
    public List<string> Months { get; set; } = new();

    public List<ProductStatisticsSeriesDto> Products { get; set; } = new();
}
