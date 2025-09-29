using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;

/// <summary>
/// Handler for getting invoice import statistics for monitoring purposes
/// </summary>
public class GetInvoiceImportStatisticsHandler : IRequestHandler<GetInvoiceImportStatisticsRequest, GetInvoiceImportStatisticsResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IConfiguration _configuration;

    public GetInvoiceImportStatisticsHandler(
        IAnalyticsRepository analyticsRepository,
        IConfiguration configuration)
    {
        _analyticsRepository = analyticsRepository;
        _configuration = configuration;
    }

    public async Task<GetInvoiceImportStatisticsResponse> Handle(
        GetInvoiceImportStatisticsRequest request,
        CancellationToken cancellationToken)
    {
        // Get configuration values
        var minimumThreshold = _configuration.GetValue<int>("InvoiceImport:MinimumDailyThreshold", 10);
        var defaultDaysBack = _configuration.GetValue<int>("InvoiceImport:DefaultDaysBack", 14);

        // Use provided days back or default from configuration
        var daysBack = request.DaysBack > 0 ? request.DaysBack : defaultDaysBack;

        // Calculate date range - work with UTC dates for consistency
        // Repository will handle conversion to Unspecified for PostgreSQL timestamp without time zone
        var endDate = DateTime.UtcNow.Date;
        endDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

        var startDate = endDate.AddDays(-daysBack);
        startDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);

        // Get daily invoice counts from repository
        var dailyCounts = await _analyticsRepository.GetInvoiceImportStatisticsAsync(
            startDate,
            endDate,
            request.DateType,
            cancellationToken);

        // Mark days below threshold as problematic
        foreach (var dayCount in dailyCounts)
        {
            dayCount.IsBelowThreshold = dayCount.Count < minimumThreshold;
        }

        return new GetInvoiceImportStatisticsResponse
        {
            Data = dailyCounts,
            MinimumThreshold = minimumThreshold
        };
    }
}