using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Domain.Features.Analytics;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;

/// <summary>
/// Handler for getting invoice import statistics for monitoring purposes
/// </summary>
public class GetInvoiceImportStatisticsHandler : IRequestHandler<GetInvoiceImportStatisticsRequest, GetInvoiceImportStatisticsResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly InvoiceImportOptions _options;
    private readonly TimeProvider _timeProvider;

    public GetInvoiceImportStatisticsHandler(
        IAnalyticsRepository analyticsRepository,
        IOptions<InvoiceImportOptions> invoiceImportOptions,
        TimeProvider timeProvider)
    {
        _analyticsRepository = analyticsRepository;
        _options = invoiceImportOptions.Value;
        _timeProvider = timeProvider;
    }

    public async Task<GetInvoiceImportStatisticsResponse> Handle(
        GetInvoiceImportStatisticsRequest request,
        CancellationToken cancellationToken)
    {
        // Get configuration values
        var minimumThreshold = _options.MinimumDailyThreshold;
        var defaultDaysBack = _options.DefaultDaysBack;

        // Use provided days back or default from configuration
        var daysBack = request.DaysBack ?? defaultDaysBack;

        // Calculate date range - work with UTC dates for consistency
        // Repository will handle conversion to Unspecified for PostgreSQL timestamp without time zone
        var endDate = _timeProvider.GetUtcNow().Date;
        endDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

        var startDate = endDate.AddDays(-daysBack);
        startDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);

        // Get daily invoice counts from repository
        var dailyCounts = await _analyticsRepository.GetInvoiceImportStatisticsAsync(
            startDate,
            endDate,
            request.DateType,
            cancellationToken);

        // Project to DTOs, marking days below threshold as problematic
        var data = dailyCounts.Select(c => new DailyInvoiceCountDto
        {
            Date = c.Date,
            Count = c.Count,
            IsBelowThreshold = c.Count < minimumThreshold
        }).ToList();

        return new GetInvoiceImportStatisticsResponse
        {
            Data = data,
            MinimumThreshold = minimumThreshold
        };
    }
}