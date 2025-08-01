using Anela.Heblo.Application.Features.Audit.Contracts;
using Anela.Heblo.Xcc.Audit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Audit.Application;

/// <summary>
/// Handler for retrieving audit summary statistics
/// </summary>
public class GetAuditSummaryHandler : IRequestHandler<GetAuditSummaryRequest, GetAuditSummaryResponse>
{
    private readonly IDataLoadAuditService _auditService;
    private readonly ILogger<GetAuditSummaryHandler> _logger;

    public GetAuditSummaryHandler(IDataLoadAuditService auditService, ILogger<GetAuditSummaryHandler> logger)
    {
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GetAuditSummaryResponse> Handle(GetAuditSummaryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Retrieving audit summary for period fromDate: {FromDate}, toDate: {ToDate}",
                request.FromDate, request.ToDate);

            var auditLogs = await _auditService.GetAuditLogsAsync(null, request.FromDate, request.ToDate);

            var summary = auditLogs
                .GroupBy(x => new { x.DataType, x.Source })
                .Select(g => new AuditSummaryItem
                {
                    DataType = g.Key.DataType,
                    Source = g.Key.Source,
                    TotalRequests = g.Count(),
                    SuccessfulRequests = g.Count(x => x.Success),
                    FailedRequests = g.Count(x => !x.Success),
                    TotalRecords = g.Where(x => x.Success).Sum(x => x.RecordCount),
                    AverageDuration = g.Where(x => x.Success).Any()
                        ? g.Where(x => x.Success).Average(x => x.Duration.TotalMilliseconds)
                        : 0,
                    LastSuccessfulLoad = g.Where(x => x.Success).Max(x => (DateTime?)x.Timestamp),
                    LastFailedLoad = g.Where(x => !x.Success).Max(x => (DateTime?)x.Timestamp)
                })
                .OrderBy(x => x.DataType)
                .ThenBy(x => x.Source)
                .ToList();

            var response = new GetAuditSummaryResponse
            {
                PeriodFrom = request.FromDate,
                PeriodTo = request.ToDate,
                Summary = summary
            };

            _logger.LogDebug("Generated audit summary with {SummaryCount} items", summary.Count);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit summary");
            throw;
        }
    }
}