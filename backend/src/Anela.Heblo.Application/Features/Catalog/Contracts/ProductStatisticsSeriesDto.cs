namespace Anela.Heblo.Application.Features.Catalog.Contracts;

/// <summary>
/// One product's monthly values, index-aligned to GetProductStatisticsResponse.Months.
/// </summary>
public class ProductStatisticsSeriesDto
{
    public string ProductCode { get; set; } = null!;

    public string ProductName { get; set; } = null!;

    /// <summary>
    /// Same length and order as the response's Months. Months with no data are 0, not gaps.
    /// </summary>
    public List<double> Values { get; set; } = new();
}
