using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;

/// <summary>
/// Response containing invoice import statistics for monitoring
/// </summary>
public class GetInvoiceImportStatisticsResponse : BaseResponse
{
    /// <summary>
    /// Daily invoice counts for the requested period
    /// </summary>
    public List<DailyInvoiceCount> Data { get; set; } = new();

    /// <summary>
    /// Configured minimum threshold for marking days as problematic
    /// </summary>
    public int MinimumThreshold { get; set; }
}