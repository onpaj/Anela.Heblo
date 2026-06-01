using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Xcc.Telemetry;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.InvoiceClassification.Infrastructure.Jobs;

public class InvoiceClassificationJob : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<InvoiceClassificationJob> _logger;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ITelemetryService _telemetryService;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "invoice-classification",
        DisplayName = "Invoice Classification",
        Description = "Classifies and categorizes incoming invoices",
        CronExpression = "0 * * * *", // Hourly at the top of each hour
        DefaultIsEnabled = true
    };

    public InvoiceClassificationJob(
        IMediator mediator,
        ILogger<InvoiceClassificationJob> logger,
        IRecurringJobStatusChecker statusChecker,
        ITelemetryService telemetryService)
    {
        _mediator = mediator;
        _logger = logger;
        _statusChecker = statusChecker;
        _telemetryService = telemetryService;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        try
        {
            _logger.LogInformation("Starting {JobName} at {Timestamp}", Metadata.JobName, DateTime.UtcNow);

            var request = new ClassifyInvoicesRequest
            {
                InvoiceIds = null, // Batch mode - process all unclassified invoices
                ManualTrigger = false
            };

            var response = await _mediator.Send(request, cancellationToken);

            _logger.LogInformation("{JobName} completed - Success: {SuccessCount}, Manual Review: {ManualReviewCount}, Errors: {ErrorCount}, Total: {TotalCount}",
                Metadata.JobName, response.SuccessfulClassifications, response.ManualReviewRequired, response.Errors, response.TotalInvoicesProcessed);

            _telemetryService.TrackBusinessEvent("InvoiceClassification", new Dictionary<string, string>
            {
                ["Status"] = response.Errors == 0 ? "Success" : "PartialSuccess",
                ["SuccessfulClassifications"] = response.SuccessfulClassifications.ToString(),
                ["ManualReviewRequired"] = response.ManualReviewRequired.ToString(),
                ["Errors"] = response.Errors.ToString(),
                ["TotalInvoicesProcessed"] = response.TotalInvoicesProcessed.ToString(),
                ["Timestamp"] = DateTime.UtcNow.ToString("O")
            });

            if (response.Errors > 0 && response.ErrorMessages != null && response.ErrorMessages.Count > 0)
            {
                _logger.LogWarning("{JobName} completed with {ErrorCount} errors: {ErrorMessages}",
                    Metadata.JobName, response.Errors, string.Join("; ", response.ErrorMessages));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed at {Timestamp}", Metadata.JobName, DateTime.UtcNow);
            _telemetryService.TrackException(ex, new Dictionary<string, string>
            {
                ["Job"] = Metadata.JobName,
                ["Timestamp"] = DateTime.UtcNow.ToString("O")
            });
            throw;
        }
    }
}
