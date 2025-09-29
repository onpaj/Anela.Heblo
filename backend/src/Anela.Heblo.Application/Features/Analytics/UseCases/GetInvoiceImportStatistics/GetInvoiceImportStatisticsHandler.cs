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
        // Get minimum threshold from configuration
        var minimumThreshold = _configuration.GetValue<int>("InvoiceImport:MinimumDailyThreshold", 10);
        
        // Calculate date range - ensure proper UTC handling
        var endDate = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        var startDate = DateTime.SpecifyKind(endDate.AddDays(-request.DaysBack), DateTimeKind.Utc);
        
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