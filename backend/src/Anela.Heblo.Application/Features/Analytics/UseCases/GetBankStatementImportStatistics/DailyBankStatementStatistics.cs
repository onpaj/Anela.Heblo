namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetBankStatementImportStatistics;

/// <summary>
/// Represents bank statement import statistics for a specific day
/// </summary>
public class DailyBankStatementStatistics
{
    /// <summary>
    /// Date for this statistics (only date part, time is ignored)
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Number of bank statement imports for this date
    /// </summary>
    public int ImportCount { get; set; }

    /// <summary>
    /// Total number of items across all bank statements for this date
    /// </summary>
    public int TotalItemCount { get; set; }
}