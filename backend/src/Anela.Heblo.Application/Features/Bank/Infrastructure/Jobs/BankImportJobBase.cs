using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

public abstract class BankImportJobBase : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger _logger;

    public abstract RecurringJobMetadata Metadata { get; }

    protected BankImportJobBase(
        IMediator mediator,
        ILoggerFactory loggerFactory,
        IRecurringJobStatusChecker statusChecker)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(statusChecker);

        _mediator = mediator;
        _statusChecker = statusChecker;
        _logger = loggerFactory.CreateLogger(GetType());
    }

    internal abstract BankImportJobParameters GetParameters();

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation(
                "Job {JobName} is disabled. Skipping execution.",
                Metadata.JobName);
            return;
        }

        try
        {
            _logger.LogInformation("Starting {JobName}", Metadata.JobName);

            var parameters = GetParameters();
            var request = new ImportBankStatementRequest(
                parameters.AccountName,
                parameters.DateFrom,
                parameters.DateTo);

            var response = await _mediator.Send(request, cancellationToken);

            _logger.LogInformation(
                "{JobName} completed. Imported {Count} statements",
                Metadata.JobName,
                response.Statements.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }
}
