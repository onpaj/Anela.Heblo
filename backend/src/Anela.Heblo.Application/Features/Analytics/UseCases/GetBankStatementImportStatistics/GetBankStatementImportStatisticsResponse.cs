using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetBankStatementImportStatistics;

/// <summary>
/// Response containing bank statement import statistics for monitoring
/// </summary>
public class GetBankStatementImportStatisticsResponse : BaseResponse
{
    /// <summary>
    /// Daily bank statement statistics for the requested period
    /// </summary>
    public List<DailyBankStatementStatistics> Statistics { get; set; } = new();
}