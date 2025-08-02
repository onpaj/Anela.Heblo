using Anela.Heblo.Application.Features.Audit.Model;
using Anela.Heblo.Xcc.Audit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Audit;

/// <summary>
/// Handler for retrieving data load audit logs
/// </summary>
public class GetAuditLogsHandler : IRequestHandler<GetAuditLogsRequest, GetAuditLogsResponse>
{
    private readonly IDataLoadAuditService _auditService;
    private readonly ILogger<GetAuditLogsHandler> _logger;

    public GetAuditLogsHandler(IDataLoadAuditService auditService, ILogger<GetAuditLogsHandler> logger)
    {
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GetAuditLogsResponse> Handle(GetAuditLogsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Retrieving data load audit logs with limit: {Limit}, fromDate: {FromDate}, toDate: {ToDate}",
                request.Limit, request.FromDate, request.ToDate);

            var auditLogs = await _auditService.GetAuditLogsAsync(request.Limit, request.FromDate, request.ToDate);

            var response = new GetAuditLogsResponse
            {
                Count = auditLogs.Count,
                Logs = auditLogs
            };

            _logger.LogDebug("Retrieved {Count} audit logs successfully", response.Count);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving data load audit logs");
            throw;
        }
    }
}