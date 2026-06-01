using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;

/// <summary>
/// Request for getting invoice import statistics for monitoring
/// </summary>
public class GetInvoiceImportStatisticsRequest : IRequest<GetInvoiceImportStatisticsResponse>
{
    /// <summary>
    /// Type of date to use for grouping statistics
    /// </summary>
    public ImportDateType DateType { get; set; } = ImportDateType.InvoiceDate;

    /// <summary>
    /// Number of days to look back from today
    /// </summary>
    public int? DaysBack { get; set; }
}

/// <summary>
/// Enum defining which date field to use for statistics
/// </summary>
public enum ImportDateType
{
    /// <summary>
    /// Use InvoiceDate (when invoice was issued)
    /// </summary>
    InvoiceDate,

    /// <summary>
    /// Use LastSyncTime (when invoice was imported into our system)
    /// </summary>
    LastSyncTime
}