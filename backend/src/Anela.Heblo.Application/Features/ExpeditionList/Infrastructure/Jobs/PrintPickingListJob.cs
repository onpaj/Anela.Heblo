using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionList.Infrastructure.Jobs;

public class PrintPickingListJob : IRecurringJob
{
    private readonly IExpeditionListService _expeditionListService;
    private readonly IOptions<PrintPickingListOptions> _options;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<PrintPickingListJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "print-picking-list",
        DisplayName = "Print Picking List",
        Description = "Generates expedition picking list, optionally sends email copy and copies to printer queue",
        CronExpression = "0 4,11 * * *", // Twice daily at 6:00 and 14:00 Prague time
        DefaultIsEnabled = true
    };

    public PrintPickingListJob(
        IExpeditionListService expeditionListService,
        IOptions<PrintPickingListOptions> options,
        IRecurringJobStatusChecker statusChecker,
        ILogger<PrintPickingListJob> logger)
    {
        _expeditionListService = expeditionListService;
        _options = options;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        _logger.LogInformation("Starting {JobName}", Metadata.JobName);

        try
        {
            var request = new PrintPickingListRequest
            {
                Carriers = PrintPickingListRequest.DefaultCarriers,
                SourceStateId = _options.Value.SourceStateId,
                DesiredStateId = _options.Value.DesiredStateId,
                ChangeOrderState = _options.Value.ChangeOrderStateByDefault,
                SendToPrinter = _options.Value.SendToPrinterByDefault,
            };

            var emailList = _options.Value.DefaultEmailRecipients.Count > 0
                ? (IList<string>)_options.Value.DefaultEmailRecipients
                : null;

            var result = await _expeditionListService.PrintPickingListAsync(request, emailList, cancellationToken);

            _logger.LogInformation("{JobName} completed. Total orders: {TotalCount}", Metadata.JobName, result.TotalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }
}
