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

/// <summary>
/// Represents invoice count for a specific day
/// </summary>
public class DailyInvoiceCount
{
    /// <summary>
    /// Date for this count (only date part, time is ignored)
    /// </summary>
    public DateTime Date { get; set; }
    
    /// <summary>
    /// Number of invoices for this date
    /// </summary>
    public int Count { get; set; }
    
    /// <summary>
    /// Whether this day is below the minimum threshold (problematic)
    /// </summary>
    public bool IsBelowThreshold { get; set; }
}