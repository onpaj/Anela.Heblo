using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetBankStatementImportStatistics;

/// <summary>
/// Handler for getting bank statement import statistics for monitoring purposes
/// </summary>
public class GetBankStatementImportStatisticsHandler : IRequestHandler<GetBankStatementImportStatisticsRequest, GetBankStatementImportStatisticsResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;

    public GetBankStatementImportStatisticsHandler(IAnalyticsRepository analyticsRepository)
    {
        _analyticsRepository = analyticsRepository;
    }

    public async Task<GetBankStatementImportStatisticsResponse> Handle(
        GetBankStatementImportStatisticsRequest request,
        CancellationToken cancellationToken)
    {
        // Set default date range if not provided (last 30 days)
        var endDate = request.EndDate ?? DateTime.UtcNow.Date;
        var startDate = request.StartDate ?? endDate.AddDays(-30);

        // Ensure dates are UTC for consistency
        if (startDate.Kind != DateTimeKind.Utc)
            startDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        if (endDate.Kind != DateTimeKind.Utc)
            endDate = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);

        // Get daily bank statement statistics from repository
        var dailyStatistics = await _analyticsRepository.GetBankStatementImportStatisticsAsync(
            startDate,
            endDate,
            request.DateType,
            cancellationToken);

        return new GetBankStatementImportStatisticsResponse
        {
            Statistics = dailyStatistics
        };
    }
}