using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

public class ShoptetPayImportJob : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<ShoptetPayImportJob> _logger;
    private readonly IRecurringJobStatusChecker _statusChecker;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-shoptetpay-czk-import",
        DisplayName = "Daily ShoptetPay CZK Import",
        Description = "Imports ShoptetPay CZK payment statements from current day",
        CronExpression = "50 4 * * *", // Daily at 4:50 AM
        DefaultIsEnabled = true
    };

    public ShoptetPayImportJob(
        IMediator mediator,
        ILogger<ShoptetPayImportJob> logger,
        IRecurringJobStatusChecker statusChecker)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        try
        {
            _logger.LogInformation("Starting {JobName}", Metadata.JobName);

            var today = DateTime.Today;
            var request = new ImportBankStatementRequest("ShoptetPay-CZK", today, today);

            var response = await _mediator.Send(request, cancellationToken);

            _logger.LogInformation("{JobName} completed. Imported {Count} statements",
                Metadata.JobName, response.Statements.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }
}
