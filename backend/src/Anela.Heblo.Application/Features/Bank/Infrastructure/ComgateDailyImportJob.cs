using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure;

public class ComgateDailyImportJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<ComgateDailyImportJob> _logger;
    private readonly IRecurringJobConfigurationRepository _jobConfigRepository;

    public ComgateDailyImportJob(
        IMediator mediator,
        ILogger<ComgateDailyImportJob> logger,
        IRecurringJobConfigurationRepository jobConfigRepository)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jobConfigRepository = jobConfigRepository ?? throw new ArgumentNullException(nameof(jobConfigRepository));
    }

    public async Task ImportComgateCzkStatementsAsync()
    {
        const string jobName = "daily-comgate-czk-import";

        var configuration = await _jobConfigRepository.GetByJobNameAsync(jobName);
        if (configuration != null && !configuration.IsEnabled)
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", jobName);
            return;
        }

        try
        {
            _logger.LogInformation("Starting daily CZK bank statement import from Comgate");

            var yesterdayDate = DateTime.Today.AddDays(-1);
            var request = new ImportBankStatementRequest("ComgateCZK", yesterdayDate);

            var response = await _mediator.Send(request);

            _logger.LogInformation("Daily CZK bank statement import completed. Imported {Count} statements", response.Statements.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during daily CZK bank statement import");
            throw;
        }
    }

    public async Task ImportComgateEurStatementsAsync()
    {
        const string jobName = "daily-comgate-eur-import";

        var configuration = await _jobConfigRepository.GetByJobNameAsync(jobName);
        if (configuration != null && !configuration.IsEnabled)
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", jobName);
            return;
        }

        try
        {
            _logger.LogInformation("Starting daily EUR bank statement import from Comgate");

            var yesterdayDate = DateTime.Today.AddDays(-1);
            var request = new ImportBankStatementRequest("ComgateEUR", yesterdayDate);

            var response = await _mediator.Send(request);

            _logger.LogInformation("Daily EUR bank statement import completed. Imported {Count} statements", response.Statements.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during daily EUR bank statement import");
            throw;
        }
    }
}