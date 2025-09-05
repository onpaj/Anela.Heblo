using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Audit.UseCases.GetAuditSummary;

/// <summary>
/// Response containing audit summary statistics
/// </summary>
public class GetAuditSummaryResponse : BaseResponse
{
    /// <summary>
    /// Period start date for the summary
    /// </summary>
    public DateTime? PeriodFrom { get; set; }

    /// <summary>
    /// Period end date for the summary
    /// </summary>
    public DateTime? PeriodTo { get; set; }

    /// <summary>
    /// Summary statistics grouped by data type and source
    /// </summary>
    public IReadOnlyList<AuditSummaryItem> Summary { get; set; } = new List<AuditSummaryItem>();
}

/// <summary>
/// Summary statistics for a specific data type and source combination
/// </summary>
public class AuditSummaryItem
{
    /// <summary>
    /// Type of data (Stock, Purchase History, etc.)
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// Source of data (Flexi ERP, Shoptet E-shop, etc.)
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Total number of requests made
    /// </summary>
    public int TotalRequests { get; set; }

    /// <summary>
    /// Number of successful requests
    /// </summary>
    public int SuccessfulRequests { get; set; }

    /// <summary>
    /// Number of failed requests
    /// </summary>
    public int FailedRequests { get; set; }

    /// <summary>
    /// Total number of records loaded successfully
    /// </summary>
    public int TotalRecords { get; set; }

    /// <summary>
    /// Average duration of successful requests in milliseconds
    /// </summary>
    public double AverageDuration { get; set; }

    /// <summary>
    /// Timestamp of the last successful load
    /// </summary>
    public DateTime? LastSuccessfulLoad { get; set; }

    /// <summary>
    /// Timestamp of the last failed load
    /// </summary>
    public DateTime? LastFailedLoad { get; set; }
}