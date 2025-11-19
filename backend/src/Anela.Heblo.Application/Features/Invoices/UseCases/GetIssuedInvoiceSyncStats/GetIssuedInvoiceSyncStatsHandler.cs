using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceSyncStats;

/// <summary>
/// Handler for getting issued invoice synchronization statistics
/// </summary>
public class GetIssuedInvoiceSyncStatsHandler : IRequestHandler<GetIssuedInvoiceSyncStatsRequest, GetIssuedInvoiceSyncStatsResponse>
{
    private readonly IIssuedInvoiceRepository _repository;
    private readonly ILogger<GetIssuedInvoiceSyncStatsHandler> _logger;

    public GetIssuedInvoiceSyncStatsHandler(
        IIssuedInvoiceRepository repository,
        ILogger<GetIssuedInvoiceSyncStatsHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetIssuedInvoiceSyncStatsResponse> Handle(GetIssuedInvoiceSyncStatsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var fromDate = request.FromDate ?? DateTime.Now.Date.AddDays(-30);
            var toDate = request.ToDate ?? DateTime.Now.Date;

            _logger.LogInformation("Getting issued invoice sync stats from {FromDate} to {ToDate}", fromDate, toDate);

            var stats = await _repository.GetSyncStatsAsync(fromDate, toDate, cancellationToken);

            _logger.LogInformation("Retrieved sync stats: {TotalInvoices} total, {SyncedInvoices} synced, {UnsyncedInvoices} unsynced", 
                stats.TotalInvoices, stats.SyncedInvoices, stats.UnsyncedInvoices);

            return new GetIssuedInvoiceSyncStatsResponse
            {
                TotalInvoices = stats.TotalInvoices,
                SyncedInvoices = stats.SyncedInvoices,
                UnsyncedInvoices = stats.UnsyncedInvoices,
                InvoicesWithErrors = stats.InvoicesWithErrors,
                CriticalErrors = stats.CriticalErrors,
                LastSyncTime = stats.LastSyncTime,
                SyncSuccessRate = stats.SyncSuccessRate,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting issued invoice sync stats");
            return new GetIssuedInvoiceSyncStatsResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Exception,
                Params = new Dictionary<string, string> { { "ErrorMessage", "Chyba při načítání statistik synchronizace faktur" } }
            };
        }
    }
}