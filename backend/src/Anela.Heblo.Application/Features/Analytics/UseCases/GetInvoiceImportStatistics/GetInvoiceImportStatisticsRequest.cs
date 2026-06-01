using Anela.Heblo.Domain.Features.Analytics;
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