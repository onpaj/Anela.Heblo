namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;

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