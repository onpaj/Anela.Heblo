using Anela.Heblo.Domain.Features.Analytics;
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetBankStatementImportStatistics;

/// <summary>
/// Request for getting bank statement import statistics for monitoring
/// </summary>
public class GetBankStatementImportStatisticsRequest : IRequest<GetBankStatementImportStatisticsResponse>
{
    /// <summary>
    /// Start date for statistics (optional)
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// End date for statistics (optional)
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Type of date to use for grouping statistics
    /// </summary>
    public BankStatementDateType DateType { get; set; } = BankStatementDateType.ImportDate;
}