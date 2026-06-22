using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Bank;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

public abstract class BankImportJobBase : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly IBankImportStateRepository _stateRepository;
    private readonly IBankStatementImportRepository _statementRepository;
    private readonly BankImportWatermarkOptions _options;
    private readonly ILogger _logger;

    public abstract RecurringJobMetadata Metadata { get; }

    /// <summary>Account name (must match BankAccountSettings.Accounts[].Name).</summary>
    protected abstract string AccountName { get; }

    /// <summary>Inclusive end of the import window for this job (e.g. yesterday or today).</summary>
    protected abstract DateTime GetTargetEndDate(DateTime today);

    protected BankImportJobBase(
        IMediator mediator,
        ILoggerFactory loggerFactory,
        IRecurringJobStatusChecker statusChecker,
        IBankImportStateRepository stateRepository,
        IBankStatementImportRepository statementRepository,
        IOptions<BankImportWatermarkOptions> options)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(statusChecker);
        ArgumentNullException.ThrowIfNull(stateRepository);
        ArgumentNullException.ThrowIfNull(statementRepository);
        ArgumentNullException.ThrowIfNull(options);

        _mediator = mediator;
        _statusChecker = statusChecker;
        _stateRepository = stateRepository;
        _statementRepository = statementRepository;
        _options = options.Value;
        _logger = loggerFactory.CreateLogger(GetType());
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        var targetEnd = GetTargetEndDate(DateTime.Today);
        var state = await _stateRepository.GetByAccountAsync(AccountName, cancellationToken)
                    ?? new BankImportState(AccountName);
        var dateFrom = await ResolveDateFromAsync(state, targetEnd, cancellationToken);

        try
        {
            _logger.LogInformation(
                "Starting {JobName} - Account: {Account}, DateFrom: {DateFrom}, DateTo: {DateTo}",
                Metadata.JobName, AccountName, dateFrom, targetEnd);

            var response = await _mediator.Send(
                new ImportBankStatementRequest(AccountName, dateFrom, targetEnd), cancellationToken);

            if (response.HasErrors)
            {
                _logger.LogError(
                    "{JobName} completed WITH ERRORS - Success: {Success}, Errors: {Errors}, Skipped: {Skipped}. Watermark NOT advanced (stuck at {Watermark:yyyy-MM-dd}).",
                    Metadata.JobName, response.SuccessCount, response.ErrorCount, response.SkippedCount, state.LastValidImportDate);
            }
            else
            {
                _logger.LogInformation(
                    "{JobName} completed - Success: {Success}, Skipped: {Skipped}. Watermark advanced to {Watermark:yyyy-MM-dd}.",
                    Metadata.JobName, response.SuccessCount, response.SkippedCount, targetEnd);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }

    private async Task<DateTime> ResolveDateFromAsync(
        BankImportState state, DateTime targetEnd, CancellationToken cancellationToken)
    {
        DateTime dateFrom;
        if (state.LastValidImportDate.HasValue)
        {
            dateFrom = state.LastValidImportDate.Value.Date;
        }
        else
        {
            var maxExisting = await _statementRepository.GetMaxStatementDateAsync(AccountName, cancellationToken);
            dateFrom = maxExisting?.Date ?? targetEnd;
            _logger.LogInformation(
                "{JobName} bootstrap - no watermark; derived DateFrom {DateFrom:yyyy-MM-dd} from existing data.",
                Metadata.JobName, dateFrom);
        }

        if (dateFrom > targetEnd)
        {
            dateFrom = targetEnd;
        }

        var span = (targetEnd.Date - dateFrom.Date).Days;
        if (span > _options.MaxBackfillDays)
        {
            var capped = targetEnd.Date.AddDays(-_options.MaxBackfillDays);
            _logger.LogError(
                "{JobName} watermark is {Span} days behind (>{Cap}). Clamping DateFrom {Original:yyyy-MM-dd} -> {Capped:yyyy-MM-dd}. Earlier data will NOT be imported.",
                Metadata.JobName, span, _options.MaxBackfillDays, dateFrom, capped);
            dateFrom = capped;
        }
        else if (span > _options.StaleWarningDays)
        {
            _logger.LogWarning(
                "{JobName} watermark is {Span} days behind; importing range {DateFrom:yyyy-MM-dd}..{DateTo:yyyy-MM-dd}.",
                Metadata.JobName, span, dateFrom, targetEnd);
        }

        return dateFrom;
    }
}
